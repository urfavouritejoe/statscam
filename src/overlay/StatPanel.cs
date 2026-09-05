using System;
using System.Linq;
using StatsCam.Model;

namespace StatsCam.Overlay;

public enum PanelDensity { Full, Compact, ScoreOnly, Hidden }

/// <summary>
/// The top-right panel. Reads MatchState and draws; it owns no state of its own beyond
/// display preferences, so it is safe to call from OnGUI at any time.
/// </summary>
public sealed class StatPanel
{
    public PanelDensity Density = PanelDensity.Full;
    public PlayerStats Spotlight;

    private readonly MatchState _state;

    public StatPanel(MatchState state) => _state = state;

    public void Cycle() =>
        Density = Density switch
        {
            PanelDensity.Full => PanelDensity.Compact,
            PanelDensity.Compact => PanelDensity.ScoreOnly,
            PanelDensity.ScoreOnly => PanelDensity.Full,
            _ => PanelDensity.Full
        };

    public void Draw()
    {
        if (Density == PanelDensity.Hidden) return;

        var w = Theme.PanelWidth;
        var x = Gui.ScreenW - w - Theme.MarginX;
        var y = Theme.MarginY;

        TeamRollup a, b;
        float poss0;
        int players;
        lock (_state.Gate)
        {
            a = _state.Team(0);
            b = _state.Team(1);
            poss0 = _state.PossessionShare0();
            players = _state.Players.Values.Count(p => p.Present);
        }

        var h = MeasureHeight();
        Gui.Fill(x, y, w, h, Theme.Panel);
        Gui.Frame(x, y, w, h, Theme.PanelEdge);

        var cursor = y;
        cursor = DrawScoreline(x, cursor, w);

        if (Density == PanelDensity.ScoreOnly) return;

        cursor = DrawPossession(x, cursor, w, poss0);

        if (Density == PanelDensity.Compact)
        {
            Row(x, ref cursor, w, a.Shots.ToString(), "SHOTS", b.Shots.ToString());
            Row(x, ref cursor, w, a.Xg.ToString("0.00"), "xG", b.Xg.ToString("0.00"));
            return;
        }

        Row(x, ref cursor, w, a.Shots.ToString(), "SHOTS", b.Shots.ToString());
        Row(x, ref cursor, w, a.OnTarget.ToString(), "ON TARGET", b.OnTarget.ToString());
        Row(x, ref cursor, w, a.Xg.ToString("0.00"), "xG", b.Xg.ToString("0.00"));
        Row(x, ref cursor, w, a.Passes.ToString(), "PASSES", b.Passes.ToString());
        Row(x, ref cursor, w, Pct(a.PassAccuracy), "PASS ACC", Pct(b.PassAccuracy));
        Row(x, ref cursor, w, a.Saves.ToString(), "SAVES", b.Saves.ToString());
        Row(x, ref cursor, w, a.Touches.ToString(), "TOUCHES", b.Touches.ToString());

        cursor += 4;
        if (Spotlight != null) DrawSpotlight(x, ref cursor, w);
        else if (players == 0) Gui.Text(x + 14, cursor, w - 28, 18, "waiting for players", 11, Theme.Muted);
    }

    // ------------------------------------------------------------ sections

    private float DrawScoreline(float x, float y, float w)
    {
        const float h = 54f;
        var clockW = 86f;
        var half = (w - clockW) / 2f;

        Gui.Text(x + 14, y + 8, half - 14, 12, Theme.TeamName(0), 10, Theme.Muted);
        Gui.Text(x + 14, y + 20, half - 14, 30, _state.Score[0].ToString(), 30, Theme.Kit[0], Align.Left, true);

        Gui.Fill(x + half, y, clockW, h, Theme.Strip);
        Gui.Text(x + half, y + 12, clockW, 20, Clock(), 17, Theme.Ink, Align.Center, true);
        Gui.Text(x + half, y + 32, clockW, 14, PhaseLabel(), 9, Theme.Muted, Align.Center);

        Gui.Text(x + half + clockW, y + 8, half - 14, 12, Theme.TeamName(1), 10, Theme.Muted, Align.Right);
        Gui.Text(x + half + clockW, y + 20, half - 14, 30, _state.Score[1].ToString(), 30, Theme.Kit[1], Align.Right, true);

        Gui.Fill(x, y + h, w, 1, Theme.Divider);
        return y + h + 1;
    }

    private float DrawPossession(float x, float y, float w, float share0)
    {
        const float h = 34f;
        var pad = 14f;
        var barY = y + 20f;
        var barW = w - pad * 2;

        Gui.Text(x + pad, y + 4, 50, 12, Pct(share0), 10, Theme.Body);
        Gui.Text(x, y + 4, w, 12, "POSSESSION", 9, Theme.Muted, Align.Center);
        Gui.Text(x + w - pad - 50, y + 4, 50, 12, Pct(1f - share0), 10, Theme.Body, Align.Right);

        Gui.Fill(x + pad, barY, barW, 4, Theme.Kit[1]);
        Gui.Fill(x + pad, barY, barW * Math.Clamp(share0, 0f, 1f), 4, Theme.Kit[0]);

        Gui.Fill(x, y + h, w, 1, Theme.Divider);
        return y + h + 1;
    }

    private static void Row(float x, ref float y, float w, string left, string label, string right)
    {
        const float pad = 14f;
        Gui.Text(x + pad, y, 60, Theme.RowHeight, left, 13, Theme.Ink, Align.Left, true);
        Gui.Text(x, y, w, Theme.RowHeight, label, 9, Theme.Muted, Align.Center);
        Gui.Text(x + w - pad - 60, y, 60, Theme.RowHeight, right, 13, Theme.Ink, Align.Right, true);
        y += Theme.RowHeight;
    }

    private void DrawSpotlight(float x, ref float y, float w)
    {
        var p = Spotlight;
        Gui.Fill(x, y, w, 1, Theme.Divider);
        y += 1;
        Gui.Fill(x, y, w, 46, Theme.Strip);

        Gui.Fill(x, y, 3, 46, Theme.TeamColour(p.Team));
        Gui.Text(x + 14, y + 5, w - 28, 16, p.Label.ToUpperInvariant(), 13, Theme.Ink, Align.Left, true);

        var line = $"{p.Touches} TCH   {p.Shots} SH   {p.Xg:0.00} xG   " +
                   $"{p.Passes} PAS {Pct(p.PassAccuracy)}   {p.Saves} SV";
        Gui.Text(x + 14, y + 24, w - 28, 16, line, 10, Theme.Body);

        y += 46;
    }

    // ------------------------------------------------------------ helpers

    private float MeasureHeight() => Density switch
    {
        PanelDensity.ScoreOnly => 55f,
        PanelDensity.Compact => 55f + 35f + Theme.RowHeight * 2 + 8f,
        _ => 55f + 35f + Theme.RowHeight * 7 + 8f + (Spotlight != null ? 47f : 20f)
    };

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

    private static string Pct(float v) => $"{v * 100f:0}%";
}
