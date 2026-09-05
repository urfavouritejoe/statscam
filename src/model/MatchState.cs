using System;
using System.Collections.Generic;
using System.Linq;

namespace StatsCam.Model;

/// <summary>Everything the goal card needs, assembled at the moment the goal lands.</summary>
public sealed class GoalMoment
{
    public string Scorer = "";
    public string Assist;
    public int Team;
    public bool OwnGoal;
    public int Minute;
    public float Xg;
    public float DistanceMetres;
    public float BallSpeedKph;
    public int ScoreA, ScoreB;
    public int ScorerGoalsThisMatch;

    /// <summary>Low-xG or long-range strikes earn a marker. Cheap, and it lands every time.</summary>
    public bool IsWorldie => !OwnGoal && (Xg > 0f && Xg < 0.05f || DistanceMetres > 30f);

    public string Occasion => ScorerGoalsThisMatch switch
    {
        2 => "BRACE",
        3 => "HAT-TRICK",
        >= 4 => $"{ScorerGoalsThisMatch} GOALS",
        _ => null
    };
}

/// <summary>
/// Only the phases StatsCam can actually detect. Halves, extra time and penalties need
/// the match-flow events, which are not hooked yet.
/// </summary>
public enum MatchPhase { Idle, Live, Ended }

/// <summary>
/// The single source of truth for the overlay and the dashboard.
///
/// Every mutation goes through Apply(StatEvent), so live capture and replaying a
/// recorded event log produce byte-identical state. That property is what makes the
/// numbers trustworthy and the whole thing testable without the game running.
/// </summary>
public sealed class MatchState
{
    public readonly object Gate = new();

    public Guid MatchId { get; private set; } = Guid.NewGuid();
    public DateTime StartedUtc { get; private set; } = DateTime.UtcNow;
    public string RoomId = "";
    public MatchPhase Phase = MatchPhase.Idle;

    public float ClockSeconds;
    public readonly int[] Score = new int[2];

    public readonly Dictionary<string, PlayerStats> Players = new(StringComparer.Ordinal);
    public readonly List<StatEvent> Log = new();
    public readonly List<StatEvent> Shots = new();

    public event Action<GoalMoment> GoalScored;

    // ------------------------------------------------------------ roster

    public PlayerStats Ensure(string userId)
    {
        if (string.IsNullOrEmpty(userId)) userId = "?";
        if (!Players.TryGetValue(userId, out var p))
        {
            p = new PlayerStats { UserId = userId };
            Players[userId] = p;
        }
        return p;
    }

    public PlayerStats Find(string userId)
        => userId != null && Players.TryGetValue(userId, out var p) ? p : null;

    // ------------------------------------------------------------ lifecycle

    public void BeginMatch(string roomId)
    {
        lock (Gate)
        {
            MatchId = Guid.NewGuid();
            StartedUtc = DateTime.UtcNow;
            RoomId = roomId ?? "";
            Phase = MatchPhase.Live;
            ClockSeconds = 0;
            Array.Clear(Score, 0, 2);
            Log.Clear();
            Shots.Clear();

            // Keep identities, drop accumulated numbers.
            foreach (var key in Players.Keys.ToList())
            {
                var old = Players[key];
                Players[key] = new PlayerStats
                {
                    UserId = old.UserId, Name = old.Name, ActorNumber = old.ActorNumber,
                    Team = old.Team, Present = old.Present
                };
            }
        }
    }

    public void EndMatch()
    {
        lock (Gate) Phase = MatchPhase.Ended;
    }

    // ------------------------------------------------------------ the reducer

    public void Apply(StatEvent e)
    {
        if (e == null) return;
        GoalMoment moment = null;

        lock (Gate)
        {
            Log.Add(e);
            if (e.MatchTime > 0) ClockSeconds = e.MatchTime;

            var actor = string.IsNullOrEmpty(e.ActorId) ? null : Ensure(e.ActorId);
            if (actor != null && e.ActorTeam >= 0) actor.Team = e.ActorTeam;

            var second = string.IsNullOrEmpty(e.SecondaryId) ? null : Ensure(e.SecondaryId);
            if (second != null && e.SecondaryTeam >= 0) second.Team = e.SecondaryTeam;

            switch (e.Kind)
            {
                case EventKind.Shot:
                case EventKind.InferredShot:
                    Shots.Add(e);
                    if (actor != null)
                    {
                        actor.Shots++;
                        if (e.OnTarget) actor.ShotsOnTarget++;
                        actor.Xg += e.Value;
                        actor.Touches++;
                    }
                    break;

                case EventKind.Goal:
                    moment = RecordGoal(e, actor, second);
                    break;

                case EventKind.Pass:
                    if (actor != null)
                    {
                        actor.Passes++;
                        actor.Touches++;
                        actor.XtPass += e.Value;
                        // Receiver on the same team means the pass found its man.
                        if (second != null && e.SecondaryTeam == e.ActorTeam)
                        {
                            actor.PassesCompleted++;
                            second.Touches++;
                        }
                    }
                    break;

                case EventKind.Interception:
                    if (actor != null) { actor.Interceptions++; actor.Touches++; actor.RecoveredValue += e.Value; }
                    if (second != null) second.Turnovers++;
                    break;

                case EventKind.Turnover:
                    if (actor != null) { actor.Turnovers++; actor.LossValue += e.Value; }
                    break;

                case EventKind.Save:
                    if (actor != null)
                    {
                        actor.Saves++;
                        actor.SaveValue += e.Value;
                        actor.Prevented += e.Value;
                        actor.Touches++;
                    }
                    break;

                case EventKind.Block:
                    if (actor != null) { actor.Blocks++; actor.Prevented += e.Value; actor.Touches++; }
                    break;

                case EventKind.Clearance:
                    if (actor != null) { actor.Clearances++; actor.Touches++; }
                    break;

                case EventKind.Deflection:
                    if (actor != null) actor.Touches++;
                    break;

                case EventKind.ControlEpisode:
                    // Value carries the episode duration in seconds.
                    if (actor != null) { actor.PossessionSeconds += e.Value; actor.XtCarry += 0f; }
                    break;

                case EventKind.OwnershipChange:
                    if (actor != null) actor.Touches++;
                    break;
            }
        }

        // Raised outside the lock: handlers touch the overlay and must not block capture.
        if (moment != null)
        {
            try { GoalScored?.Invoke(moment); }
            catch (Exception ex) { Journal.Warn($"GoalScored handler threw: {ex.Message}"); }
        }
    }

    private GoalMoment RecordGoal(StatEvent e, PlayerStats actor, PlayerStats assist)
    {
        // ActorTeam is the team credited with the goal. An own goal is a scorer whose
        // own team does not match the credited team — confirmed against a live log
        // before this is trusted for anything permanent.
        var creditedTeam = e.ActorTeam >= 0 ? e.ActorTeam : 0;
        var own = actor != null && actor.Team >= 0 && actor.Team != creditedTeam;

        if (creditedTeam is 0 or 1) Score[creditedTeam]++;

        if (actor != null)
        {
            if (own) actor.OwnGoals++;
            else { actor.Goals++; actor.Touches++; }
        }

        // Secondary on a goal is the assist only when it is a team-mate of the scorer.
        var realAssist = !own && assist != null && assist.Team == actor?.Team ? assist : null;
        if (realAssist != null) { realAssist.Assists++; realAssist.Xa += e.Value; }

        var goalLine = GoalDistanceReference(creditedTeam);

        return new GoalMoment
        {
            Scorer = actor?.Label ?? "UNKNOWN",
            Assist = realAssist?.Label,
            Team = creditedTeam,
            OwnGoal = own,
            Minute = (int)(ClockSeconds / 60f),
            Xg = e.Value,
            DistanceMetres = e.BallPosition.FlatDistanceTo(goalLine),
            BallSpeedKph = e.BallSpeedKph,
            ScoreA = Score[0],
            ScoreB = Score[1],
            ScorerGoalsThisMatch = actor?.Goals ?? 0,
        };
    }

    /// <summary>
    /// Approximate goal-mouth position, replaced by PitchGeometry.GoalA / GoalB once
    /// those are read live. Only used for the distance figure on the goal card.
    /// </summary>
    private Vec3 GoalDistanceReference(int scoringTeam)
        => scoringTeam == 0 ? new Vec3(0, 0, Pitch.HalfLength) : new Vec3(0, 0, -Pitch.HalfLength);

    // ------------------------------------------------------------ rollups

    public TeamRollup Team(int team)
    {
        var r = new TeamRollup { Team = team, Goals = team is 0 or 1 ? Score[team] : 0 };
        foreach (var p in Players.Values)
        {
            if (p.Team != team) continue;
            r.Shots += p.Shots;
            r.OnTarget += p.ShotsOnTarget;
            r.Xg += p.Xg;
            r.Passes += p.Passes;
            r.PassesCompleted += p.PassesCompleted;
            r.Saves += p.Saves;
            r.Touches += p.Touches;
            r.PossessionSeconds += p.PossessionSeconds;
        }
        return r;
    }

    /// <summary>Possession share for team 0, in 0..1. Falls back to an even split.</summary>
    public float PossessionShare0()
    {
        float a = 0, b = 0;
        foreach (var p in Players.Values)
        {
            if (p.Team == 0) a += p.PossessionSeconds;
            else if (p.Team == 1) b += p.PossessionSeconds;
        }
        var tot = a + b;
        return tot < 0.001f ? 0.5f : a / tot;
    }

    public IEnumerable<PlayerStats> Ranked()
        => Players.Values.Where(p => p.Team >= 0).OrderByDescending(p => p.Rating);

    public PlayerStats PlayerOfTheMatch() => Ranked().FirstOrDefault();
}

public struct TeamRollup
{
    public int Team, Goals, Shots, OnTarget, Passes, PassesCompleted, Saves, Touches;
    public float Xg, PossessionSeconds;
    public float PassAccuracy => Passes == 0 ? 0f : PassesCompleted / (float)Passes;
}

/// <summary>
/// Pitch half-extents in metres. Catalogue overwrites these from PitchGeometry when it
/// can read it; otherwise these defaults stand and the shot map is proportionally
/// approximate rather than to scale.
/// </summary>
public static class Pitch
{
    public static float HalfLength = 30f;
    public static float HalfWidth = 20f;
}
