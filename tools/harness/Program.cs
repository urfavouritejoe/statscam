using System;
using System.Linq;
using System.IO;
using StatsCam;
using StatsCam.Model;
using StatsCam.Overlay;

namespace StatsCam.Harness;

/// <summary>
/// Drives a synthetic match through the real MatchState reducer, then renders the real
/// overlay layout code through an SVG backend — so the model, the JSON writer and the
/// on-screen geometry can all be verified without VRFS running.
/// </summary>
internal static class Program
{
    private static readonly (string id, string name, int team)[] Squad =
    {
        ("u-marcus",  "MARCUS_V",   0),
        ("u-joefant", "joefant",    0),
        ("u-riko",    "Riko",       0),
        ("u-dante",   "DANTE99",    1),
        ("u-kez",     "kez",        1),
        ("u-holm",    "Holmqvist",  1),
    };

    private static readonly Random Rng = new(7);
    private static GoalMoment LastGoal;

    private static int Main(string[] args)
    {
        Journal.Open();
        var state = new MatchState();

        state.GoalScored += m => LastGoal = m;
        state.GoalScored += m =>
            Console.WriteLine($"  GOAL  {m.Scorer}{(m.OwnGoal ? " (OG)" : "")} {m.Minute}' " +
                              $"xG={m.Xg:0.00} {m.DistanceMetres:0.0}m {m.BallSpeedKph:0}km/h " +
                              $"-> {m.ScoreA}-{m.ScoreB}" +
                              (m.Occasion != null ? $"  [{m.Occasion}]" : "") +
                              (m.IsWorldie ? "  [WORLDIE]" : ""));

        state.BeginMatch("VRFS-482913");
        foreach (var (id, name, team) in Squad)
        {
            var ps = state.Ensure(id);
            ps.Name = name;
            ps.Team = team;
        }

        Console.WriteLine("Simulating match...");
        Simulate(state);

        // Assertions: the reducer must agree with the events we fed it.
        var a = state.Team(0);
        var b = state.Team(1);
        var ok = true;

        ok &= Check("score sums to goal events",
            state.Score[0] + state.Score[1],
            state.Log.Count(e => e.Kind == EventKind.Goal));

        ok &= Check("team 0 shots", a.Shots,
            state.Log.Count(e => e.Kind is EventKind.Shot or EventKind.InferredShot && e.ActorTeam == 0));

        ok &= Check("on-target never exceeds shots", a.OnTarget <= a.Shots ? 1 : 0, 1);
        ok &= Check("pass completions never exceed passes", a.PassesCompleted <= a.Passes ? 1 : 0, 1);
        ok &= Check("possession share in range",
            state.PossessionShare0() is >= 0f and <= 1f ? 1 : 0, 1);

        var json = MatchRecord.Serialize(state);
        ok &= Check("json non-trivial", json.Length > 800 ? 1 : 0, 1);
        ok &= Check("json balanced braces",
            json.Count(c => c == '{') == json.Count(c => c == '}') ? 1 : 0, 1);
        ok &= Check("json balanced brackets",
            json.Count(c => c == '[') == json.Count(c => c == ']') ? 1 : 0, 1);

        // Render the real overlay layout code through an SVG backend.
        var outDir = Path.Combine(AppContext.BaseDirectory, "render");
        Directory.CreateDirectory(outDir);

        var board = Render(state, outDir, "board.svg", o => o.Board.Open = true);
        var live  = Render(state, outDir, "live.svg",  o => { });
        var flash = Render(state, outDir, "goal.svg",  o => o.Flash.Trigger(LastGoal));

        ok &= Check("board drew content", board.RectCount > 40 ? 1 : 0, 1);
        ok &= Check("board text inside screen",
            board.MinX >= -1 && board.MinY >= -1 &&
            board.MaxX <= board.ScreenW + 1 && board.MaxY <= board.ScreenH + 1 ? 1 : 0, 1);
        ok &= Check("live panel drew content", live.RectCount > 8 ? 1 : 0, 1);
        ok &= Check("live panel stays in the top-right",
            live.MinX > live.ScreenW * 0.5f ? 1 : 0, 1);
        ok &= Check("goal flash drew content", flash.TextCount > 8 ? 1 : 0, 1);

        Console.WriteLine();
        Console.WriteLine($"score      {state.Score[0]}-{state.Score[1]}");
        Console.WriteLine($"events     {state.Log.Count}");
        Console.WriteLine($"shots      {a.Shots} / {b.Shots}   xG {a.Xg:0.00} / {b.Xg:0.00}");
        Console.WriteLine($"passes     {a.Passes} ({a.PassAccuracy:P0}) / {b.Passes} ({b.PassAccuracy:P0})");
        Console.WriteLine($"possession {state.PossessionShare0():P0}");
        Console.WriteLine($"json       {json.Length} bytes");
        Console.WriteLine($"POTM       {state.PlayerOfTheMatch()?.Label}");
        Console.WriteLine($"render     {outDir}");
        Console.WriteLine($"           board.svg  {board.RectCount} rects, {board.TextCount} labels");
        Console.WriteLine($"           live.svg   {live.RectCount} rects, {live.TextCount} labels");
        Console.WriteLine($"           goal.svg   {flash.RectCount} rects, {flash.TextCount} labels");
        Console.WriteLine();
        Console.WriteLine(ok ? "ALL CHECKS PASSED" : "CHECKS FAILED");
        return ok ? 0 : 1;
    }

    /// <summary>Draw the overlay through the SVG backend and write it out.</summary>
    private static SvgDraw Render(MatchState state, string dir, string file, Action<OverlayRoot> setup)
    {
        var svg = new SvgDraw(1920, 1080);
        Gui.Backend = svg;

        var overlay = new OverlayRoot(state);
        overlay.Panel.Spotlight = state.Ranked().FirstOrDefault();
        setup(overlay);
        overlay.Draw();

        File.WriteAllText(Path.Combine(dir, file), svg.ToSvg());

        // A cropped copy of just the panel area, so the detail can be inspected at size.
        var bw = Math.Min(1160f, svg.ScreenW * 0.90f);
        var bh = Math.Min(700f, svg.ScreenH * 0.90f);
        File.WriteAllText(Path.Combine(dir, "zoom-" + file),
            svg.ToSvgCropped((svg.ScreenW - bw) / 2f, (svg.ScreenH - bh) / 2f, bw, bh));

        return svg;
    }

    private static bool Check(string what, int got, int want)
    {
        var ok = got == want;
        Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}: {got}" + (ok ? "" : $" (expected {want})"));
        return ok;
    }

    // ------------------------------------------------------------ simulation

    private static void Simulate(MatchState state)
    {
        float t = 0;

        for (var i = 0; i < 260; i++)
        {
            t += 2f + (float)Rng.NextDouble() * 8f;
            var team = Rng.Next(2);
            var actor = Pick(team);
            var roll = Rng.NextDouble();

            if (roll < 0.46) state.Apply(Pass(t, actor, team));
            else if (roll < 0.60) state.Apply(Simple(EventKind.OwnershipChange, t, actor, team));
            else if (roll < 0.70) state.Apply(Simple(EventKind.ControlEpisode, t, actor, team,
                                                     value: 1f + (float)Rng.NextDouble() * 6f));
            else if (roll < 0.78) state.Apply(Simple(EventKind.Interception, t, actor, team,
                                                     value: (float)Rng.NextDouble() * 0.2f));
            else if (roll < 0.84) state.Apply(Simple(EventKind.Turnover, t, actor, team,
                                                     value: (float)Rng.NextDouble() * 0.3f));
            else if (roll < 0.88) state.Apply(Simple(EventKind.Clearance, t, actor, team));
            else
            {
                state.Apply(Shot(t, actor, team));
                if (Pending != null) { state.Apply(Pending); Pending = null; }
            }
        }
    }

    private static (string id, int team) Pick(int team)
    {
        var pool = Squad.Where(s => s.team == team).ToArray();
        var s = pool[Rng.Next(pool.Length)];
        return (s.id, s.team);
    }

    private static StatEvent Pass(float t, (string id, int team) actor, int team)
    {
        var mate = Pick(team);
        var completed = Rng.NextDouble() < 0.78;
        return new StatEvent
        {
            Kind = EventKind.Pass,
            MatchTime = t,
            ActorId = actor.id,
            ActorTeam = team,
            SecondaryId = completed ? mate.id : Pick(1 - team).id,
            SecondaryTeam = completed ? team : 1 - team,
            Value = (float)Rng.NextDouble() * 0.08f,
        };
    }

    private static StatEvent Shot(float t, (string id, int team) actor, int team)
    {
        var dist = 6f + (float)Rng.NextDouble() * 28f;
        var xg = Math.Max(0.01f, 0.62f - dist * 0.019f) * (float)(0.5 + Rng.NextDouble());
        var onTarget = Rng.NextDouble() < 0.55;
        var scored = onTarget && Rng.NextDouble() < xg * 1.9;

        var z = team == 0 ? Pitch.HalfLength - dist : -Pitch.HalfLength + dist;
        var speed = 14f + (float)Rng.NextDouble() * 16f;

        var e = new StatEvent
        {
            Kind = scored ? EventKind.Goal : EventKind.Shot,
            MatchTime = t,
            ActorId = actor.id,
            ActorTeam = team,
            BallPosition = new Vec3((float)(Rng.NextDouble() * 2 - 1) * Pitch.HalfWidth * 0.7f, 0.4f, z),
            BallVelocity = new Vec3(0, 0, speed),
            OnTarget = onTarget,
            Value = xg,
        };

        // Goals get an assisting team-mate about half the time.
        if (scored && Rng.NextDouble() < 0.5)
        {
            var mate = Pick(team);
            if (mate.id != actor.id) { e.SecondaryId = mate.id; e.SecondaryTeam = team; }
        }

        // A saved shot produces a keeper save on the other side.
        if (!scored && onTarget)
        {
            var keeper = Pick(1 - team);
            var save = new StatEvent
            {
                Kind = EventKind.Save,
                MatchTime = t + 0.2f,
                ActorId = keeper.id,
                ActorTeam = 1 - team,
                Value = xg,
                BallPosition = e.BallPosition,
            };
            // Emit the shot first so ordering matches a real stream.
            return Chain(e, save);
        }

        return e;
    }

    /// <summary>Applies the follow-up immediately after the primary event.</summary>
    private static StatEvent Chain(StatEvent primary, StatEvent follow)
    {
        Pending = follow;
        return primary;
    }

    private static StatEvent Pending;

    private static StatEvent Simple(EventKind kind, float t, (string id, int team) actor, int team,
                                    float value = 0f)
        => new()
        {
            Kind = kind, MatchTime = t, ActorId = actor.id, ActorTeam = team, Value = value
        };
}
