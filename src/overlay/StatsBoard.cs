using System;
using System.Collections.Generic;
using System.Linq;
using StatsCam.Model;

namespace StatsCam.Overlay;

/// <summary>
/// The full stats screen, opened with Y.
///
/// Everything that used to justify a second monitor lives here instead: the player
/// table, the team comparison and the shot map, drawn straight into the camera window
/// so it lands in a recording like any other graphic.
/// </summary>
public sealed class StatsBoard
{
    public bool Open;

    private readonly MatchState _state;
    private DateTime _openedAt;

    public StatsBoard(MatchState state) => _state = state;

    public void Toggle()
    {
        Open = !Open;
        if (Open) _openedAt = DateTime.UtcNow;
    }

    // Column layout for the player table, as fractions of the table width.
    private static readonly (string Head, float W, bool Num)[] Columns =
    {
        ("PLAYER", 0.30f, false),
        ("G",      0.055f, true),
        ("A",      0.055f, true),
        ("SH",     0.065f, true),
        ("xG",     0.085f, true),
        ("TCH",    0.075f, true),
        ("PAS",    0.075f, true),
        ("ACC",    0.080f, true),
        ("SV",     0.060f, true),
        ("RTG",    0.085f, true),   // StatsCam's own ranking score, not the game's
    };

    public void Draw()
    {
        if (!Open) return;

        var sw = Gui.ScreenW;
        var sh = Gui.ScreenH;

        // Fade the board in quickly so it does not pop.
        var t = (float)(DateTime.UtcNow - _openedAt).TotalSeconds;
        var alpha = Math.Clamp(t / 0.18f, 0f, 1f);

        // Solid black, not a dim: the board is a screen you switch to, not an overlay
        // you read the pitch through. Fully opaque once faded in.
        Gui.Fill(0, 0, sw, sh, new Col(0, 0, 0, alpha));

        var w = Math.Min(1160f, sw * 0.90f);
        var h = Math.Min(700f, sh * 0.90f);
        var x = (sw - w) / 2f;
        var y = (sh - h) / 2f;

        Gui.Fill(x, y, w, h, Theme.Board.WithAlpha(alpha));
        Gui.Frame(x, y, w, h, Theme.PanelEdge.WithAlpha(alpha));

        var pad = 26f;
        var headH = Header(x, y, w, alpha);

        var bodyY = y + headH;
        var bodyH = h - headH - 38f;

        var leftW = (w - pad * 3) * 0.60f;
        var rightW = (w - pad * 3) * 0.40f;
        var leftX = x + pad;
        var rightX = x + pad * 2 + leftW;

        // Players on the left with the shot map filling whatever height is left under it,
        // comparison on the right. A six-player lobby would otherwise leave the left
        // column half empty while the map sat cramped in a narrow column.
        var tableH = PlayerTable(leftX, bodyY + 14, leftW, bodyH - 14, alpha);
        ShotMap(leftX, bodyY + 14 + tableH + 20, leftW, bodyH - tableH - 48, alpha);
        Comparison(rightX, bodyY + 14, rightW, alpha);

        Gui.Text(x + pad, y + h - 30, w - pad * 2, 18,
                 "Y · close     F3 · spotlight     F8 · replay goal     F1 · hide overlay",
                 11, Theme.Muted.WithAlpha(alpha));
    }

    // ------------------------------------------------------------ header

    private float Header(float x, float y, float w, float a)
    {
        const float h = 74f;
        Gui.Fill(x, y, w, h, Theme.Strip.WithAlpha(a));
        Gui.Fill(x, y + h, w, 1, Theme.Divider.WithAlpha(a));

        var pad = 26f;

        Gui.Text(x + pad, y + 14, 240, 14, Theme.KitName[0], 11, Theme.Muted.WithAlpha(a));
        Gui.Text(x + pad, y + 30, 240, 34, _state.Score[0].ToString(), 32,
                 Theme.Kit[0].WithAlpha(a), Align.Left, true);

        Gui.Text(x, y + 14, w, 14, "MATCH STATS", 11, Theme.Accent.WithAlpha(a), Align.Center);
        Gui.Text(x, y + 30, w, 26, Clock(), 22, Theme.Ink.WithAlpha(a), Align.Center, true);
        Gui.Text(x, y + 54, w, 14, PhaseLabel(), 10, Theme.Muted.WithAlpha(a), Align.Center);

        Gui.Text(x + w - pad - 240, y + 14, 240, 14, Theme.KitName[1], 11,
                 Theme.Muted.WithAlpha(a), Align.Right);
        Gui.Text(x + w - pad - 240, y + 30, 240, 34, _state.Score[1].ToString(), 32,
                 Theme.Kit[1].WithAlpha(a), Align.Right, true);

        return h + 1;
    }

    // ------------------------------------------------------------ player table

    /// <summary>Draws the table and returns the height it consumed.</summary>
    private float PlayerTable(float x, float y, float w, float h, float a)
    {
        PlayerStats[] rows;
        lock (_state.Gate) rows = _state.Ranked().ToArray();

        Gui.Text(x, y, w, 14, "PLAYERS", 10, Theme.Muted.WithAlpha(a));
        y += 20;

        // Header row
        Gui.Fill(x, y, w, 20, Theme.Strip.WithAlpha(a));
        var cx = x;
        foreach (var (head, frac, num) in Columns)
        {
            var cw = w * frac;
            Gui.Text(cx + 8, y, cw - 16, 20, head, 9, Theme.Muted.WithAlpha(a),
                     num ? Align.Right : Align.Left);
            cx += cw;
        }
        y += 21;

        if (rows.Length == 0)
        {
            Gui.Text(x, y + 12, w, 20, "no players yet", 12, Theme.Muted.WithAlpha(a));
            return 61f;
        }

        const float rowH = 23f;
        // Leave at least enough room under the table for a usable shot map.
        var max = (int)Math.Floor((h - 41f - 240f) / rowH);
        var shown = Math.Min(rows.Length, Math.Max(1, max));

        for (var i = 0; i < shown; i++)
        {
            var p = rows[i];
            var ry = y + i * rowH;

            if (i % 2 == 1) Gui.Fill(x, ry, w, rowH, Theme.Strip.WithAlpha(0.45f * a));
            Gui.Fill(x, ry + 4, 3, rowH - 8, Theme.TeamColour(p.Team).WithAlpha(a));

            var vals = new[]
            {
                p.Label,
                p.Goals.ToString(),
                p.Assists.ToString(),
                p.Shots.ToString(),
                p.Xg.ToString("0.00"),
                p.Touches.ToString(),
                p.Passes.ToString(),
                $"{p.PassAccuracy * 100f:0}%",
                p.Saves.ToString(),
                p.Rating.ToString("0.0"),
            };

            cx = x;
            for (var c = 0; c < Columns.Length; c++)
            {
                var (_, frac, num) = Columns[c];
                var cw = w * frac;
                var col = c == 0 ? Theme.Ink : (c == Columns.Length - 1 ? Theme.Accent : Theme.Body);
                Gui.Text(cx + (c == 0 ? 12 : 8), ry, cw - 16, rowH, vals[c],
                         c == 0 ? 12 : 12, col.WithAlpha(a), num ? Align.Right : Align.Left,
                         c == 0 || c == Columns.Length - 1);
                cx += cw;
            }
        }

        var used = 41f + shown * rowH;
        if (rows.Length > shown)
        {
            Gui.Text(x, y + shown * rowH + 4, w, 18,
                     $"+{rows.Length - shown} more", 10, Theme.Muted.WithAlpha(a));
            used += 20f;
        }
        return used;
    }

    // ------------------------------------------------------------ comparison

    private void Comparison(float x, float y, float w, float a)
    {
        TeamRollup ta, tb;
        float poss0;
        lock (_state.Gate)
        {
            ta = _state.Team(0);
            tb = _state.Team(1);
            poss0 = _state.PossessionShare0();
        }

        Gui.Text(x, y, w, 14, "TEAM COMPARISON", 10, Theme.Muted.WithAlpha(a));
        var top = y + 20;

        var rows = new List<(string Label, string L, string R, float LFrac)>
        {
            ("POSSESSION", $"{poss0 * 100:0}%", $"{(1 - poss0) * 100:0}%", poss0),
            ("SHOTS", ta.Shots.ToString(), tb.Shots.ToString(), Split(ta.Shots, tb.Shots)),
            ("ON TARGET", ta.OnTarget.ToString(), tb.OnTarget.ToString(), Split(ta.OnTarget, tb.OnTarget)),
            ("xG", ta.Xg.ToString("0.00"), tb.Xg.ToString("0.00"), Split(ta.Xg, tb.Xg)),
            ("PASSES", ta.Passes.ToString(), tb.Passes.ToString(), Split(ta.Passes, tb.Passes)),
            ("PASS ACC", $"{ta.PassAccuracy * 100:0}%", $"{tb.PassAccuracy * 100:0}%",
                         Split(ta.PassAccuracy, tb.PassAccuracy)),
            ("SAVES", ta.Saves.ToString(), tb.Saves.ToString(), Split(ta.Saves, tb.Saves)),
            ("TOUCHES", ta.Touches.ToString(), tb.Touches.ToString(), Split(ta.Touches, tb.Touches)),
        };

        const float rowH = 30f;
        for (var i = 0; i < rows.Count; i++)
        {
            var (label, l, r, frac) = rows[i];
            var ry = top + i * rowH;

            Gui.Text(x, ry, 54, 16, l, 12, Theme.Ink.WithAlpha(a), Align.Left, true);
            Gui.Text(x, ry, w, 16, label, 9, Theme.Muted.WithAlpha(a), Align.Center);
            Gui.Text(x + w - 54, ry, 54, 16, r, 12, Theme.Ink.WithAlpha(a), Align.Right, true);

            var barY = ry + 19;
            var barX = x + 58;
            var barW = w - 116;
            Gui.Fill(barX, barY, barW, 4, Theme.Divider.WithAlpha(a));
            var lw = barW * Math.Clamp(frac, 0f, 1f);
            Gui.Fill(barX, barY, lw, 4, Theme.Kit[0].WithAlpha(a));
            Gui.Fill(barX + lw, barY, barW - lw, 4, Theme.Kit[1].WithAlpha(a));
        }

    }

    private static float Split(float a, float b) => a + b <= 0.0001f ? 0.5f : a / (a + b);

    // ------------------------------------------------------------ shot map

    private void ShotMap(float x, float y, float w, float h, float a)
    {
        if (h < 90) return;

        StatEvent[] shots;
        lock (_state.Gate) shots = _state.Shots.ToArray();

        Gui.Text(x, y, w, 14, "SHOT MAP", 10, Theme.Muted.WithAlpha(a));
        y += 20;
        h -= 20;

        // Keep the pitch to its real proportions inside the space available.
        var ratio = Pitch.HalfWidth * 2f / (Pitch.HalfLength * 2f);
        var pw = Math.Min(w, h * ratio);
        var ph = pw / ratio;
        var px = x + (w - pw) / 2f;

        Gui.Fill(px, y, pw, ph, new Col(0.055f, 0.09f, 0.075f, 0.95f * a));
        Gui.Frame(px, y, pw, ph, Theme.Divider.WithAlpha(a));
        Gui.Fill(px, y + ph / 2f, pw, 1, Theme.Divider.WithAlpha(a));

        // Penalty areas, drawn as outlines at each end.
        var boxW = pw * 0.5f;
        var boxH = ph * 0.14f;
        Gui.Frame(px + (pw - boxW) / 2f, y, boxW, boxH, Theme.Divider.WithAlpha(a));
        Gui.Frame(px + (pw - boxW) / 2f, y + ph - boxH, boxW, boxH, Theme.Divider.WithAlpha(a));

        if (shots.Length == 0)
        {
            Gui.Text(px, y + ph / 2f - 10, pw, 20, "no shots yet", 11,
                     Theme.Muted.WithAlpha(a), Align.Center);
            return;
        }

        foreach (var s in shots)
        {
            // Team 0 attacks the top of the map, team 1 the bottom.
            var nx = Math.Clamp((s.BallPosition.X + Pitch.HalfWidth) / (Pitch.HalfWidth * 2f), 0f, 1f);
            var nz = Math.Clamp((Pitch.HalfLength - s.BallPosition.Z) / (Pitch.HalfLength * 2f), 0f, 1f);

            var size = Math.Clamp(4f + s.Value * 26f, 4f, 15f);
            var mx = px + nx * pw - size / 2f;
            var my = y + nz * ph - size / 2f;

            var kit = Theme.TeamColour(s.ActorTeam);
            var goal = s.Kind == EventKind.Goal;
            var op = goal ? 1f : s.OnTarget ? 0.75f : 0.4f;

            if (goal) Gui.Fill(mx, my, size, size, kit.WithAlpha(op * a));
            else Gui.Frame(mx, my, size, size, kit.WithAlpha(op * a), 1.5f);
        }

        Gui.Text(px, y + ph + 4, pw, 14,
                 "filled = goal   ·   size = xG", 9, Theme.Muted.WithAlpha(a), Align.Center);
    }

    // ------------------------------------------------------------ helpers

    private string Clock()
    {
        var t = Math.Max(0f, _state.ClockSeconds);
        return $"{(int)(t / 60):00}:{(int)(t % 60):00}";
    }

    private string PhaseLabel() => _state.Phase switch
    {
        MatchPhase.Live => "LIVE",
        MatchPhase.Ended => "ENDED",
        _ => "WAITING"
    };
}
