using System;
using StatsCam.Model;

namespace StatsCam.Overlay;

/// <summary>
/// The goal card. Fires on the Goal event rather than the net trigger, so it stays in
/// sync with the data rather than the physics.
///
/// Every value on it arrives with the event: scorer, assist, minute, distance, ball speed
/// and xG. Nothing here is computed after the fact.
/// </summary>
public sealed class GoalFlash
{
    private const float FadeIn = 0.28f;
    private const float Hold = 4.0f;
    private const float FadeOut = 0.7f;
    private const float Total = FadeIn + Hold + FadeOut;

    private GoalMoment _moment;
    private DateTime _firedAt;
    private GoalMoment _last;

    public bool Visible => _moment != null && Elapsed < Total;
    private float Elapsed => (float)(DateTime.UtcNow - _firedAt).TotalSeconds;

    public void Trigger(GoalMoment m)
    {
        if (m == null) return;
        _moment = m;
        _last = m;
        _firedAt = DateTime.UtcNow;
    }

    /// <summary>Replay the last card — for when the first one clipped mid-recording.</summary>
    public void Replay()
    {
        if (_last != null) Trigger(_last);
    }

    public void Draw()
    {
        if (!Visible) return;

        var t = Elapsed;
        var alpha = t < FadeIn ? t / FadeIn
                  : t < FadeIn + Hold ? 1f
                  : 1f - (t - FadeIn - Hold) / FadeOut;
        alpha = Math.Clamp(alpha, 0f, 1f);

        var m = _moment;
        var sw = Gui.ScreenW;
        var sh = Gui.ScreenH;

        var w = Math.Min(720f, sw * 0.56f);
        var h = 188f;
        var x = (sw - w) / 2f;
        var y = sh * 0.60f;

        // Slide up slightly as it fades in — motion, but only where it earns its place.
        y -= (1f - Math.Min(1f, t / FadeIn)) * 14f;

        var kit = Theme.TeamColour(m.OwnGoal ? 1 - m.Team : m.Team);

        Gui.Fill(x, y, w, h, Theme.Panel.WithAlpha(0.94f * alpha));
        Gui.Frame(x, y, w, h, Theme.PanelEdge.WithAlpha(alpha));
        Gui.Fill(x, y, 5f, h, kit.WithAlpha(alpha));

        var pad = 30f;
        var inner = w - pad * 2;

        // Eyebrow
        var kicker = m.OwnGoal ? $"OWN GOAL · {Theme.TeamName(m.Team)}" : $"GOAL · {Theme.TeamName(m.Team)}";
        Gui.Text(x + pad, y + 18, inner, 16, kicker, 12, kit.WithAlpha(alpha), Align.Left, true);

        var badge = m.OwnGoal ? null : m.Occasion ?? (m.IsWorldie ? "WORLDIE" : null);
        if (badge != null)
            Gui.Text(x + pad, y + 18, inner, 16, badge, 12, Theme.Accent.WithAlpha(alpha), Align.Right, true);

        // Scorer — the only thing that must read from across a room.
        Gui.Text(x + pad, y + 42, inner, 46, m.Scorer.ToUpperInvariant(), 38,
                 Theme.Ink.WithAlpha(alpha), Align.Left, true);

        // Assist and minute
        var sub = m.Assist != null ? $"Assist  {m.Assist}     {m.Minute}'" : $"{m.Minute}'";
        Gui.Text(x + pad, y + 92, inner, 18, sub, 14, Theme.Body.WithAlpha(alpha));

        Gui.Fill(x + pad, y + 118, inner, 1, Theme.Divider.WithAlpha(alpha));

        // Meta strip
        var cell = inner / 3f;
        Meta(x + pad + cell * 0, y + 128, cell, "DISTANCE", $"{m.DistanceMetres:0.0} m", alpha);
        Meta(x + pad + cell * 1, y + 128, cell, "BALL SPEED", $"{m.BallSpeedKph:0} km/h", alpha);
        Meta(x + pad + cell * 2, y + 128, cell, "xG", m.Xg > 0 ? $"{m.Xg:0.00}" : "—", alpha);

        // Updated scoreline, bottom right
        Gui.Text(x + pad, y + h - 30, inner, 20,
                 $"{Theme.KitName[0]} {m.ScoreA} — {m.ScoreB} {Theme.KitName[1]}",
                 13, Theme.Muted.WithAlpha(alpha), Align.Right, true);
    }

    private static void Meta(float x, float y, float w, string label, string value, float alpha)
    {
        Gui.Text(x, y, w, 12, label, 9, Theme.Muted.WithAlpha(alpha));
        Gui.Text(x, y + 14, w, 20, value, 16, Theme.Ink.WithAlpha(alpha), Align.Left, true);
    }

}
