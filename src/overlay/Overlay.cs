using System;
using System.Linq;
using StatsCam.Model;

namespace StatsCam.Overlay;

/// <summary>
/// Owns everything drawn into the camera window. Called from MelonMod.OnGUI, which runs
/// several times per frame, so this must stay allocation-light and never throw.
/// </summary>
public sealed class OverlayRoot
{
    public bool Enabled = true;

    public readonly StatPanel Panel;
    public readonly GoalFlash Flash = new();
    public readonly StatsBoard Board;

    private readonly MatchState _state;
    private int _spotlightIndex = -1;
    private bool _spotlightPinned;
    private DateTime _lastRotate = DateTime.UtcNow;

    public OverlayRoot(MatchState state)
    {
        _state = state;
        Panel = new StatPanel(state);
        Board = new StatsBoard(state);
        state.GoalScored += OnGoal;
    }

    private void OnGoal(GoalMoment m)
    {
        Flash.Trigger(m);
        Journal.Ev("GOAL", $"{m.Scorer}{(m.OwnGoal ? " (OG)" : "")} {m.Minute}' " +
                           $"xG={m.Xg:0.00} {m.DistanceMetres:0.0}m {m.BallSpeedKph:0}km/h " +
                           $"-> {m.ScoreA}-{m.ScoreB}");
    }

    // ------------------------------------------------------------ frame

    public void Draw()
    {
        if (!Enabled) return;

        Gui.Init();
        if (!Gui.Ready) return;

        try
        {
            RotateSpotlight();
            Panel.Draw();
            Board.Draw();
            Flash.Draw();
        }
        catch (Exception e)
        {
            // A throwing OnGUI spams Unity and can wedge the frame. Log once, keep going.
            Journal.Warn($"overlay draw threw: {e.Message}");
            Enabled = false;
        }
    }

    // ------------------------------------------------------------ controls

    public void Toggle() => Enabled = !Enabled;

    public void CycleSpotlight()
    {
        PlayerStats[] pool;
        lock (_state.Gate) pool = _state.Ranked().ToArray();
        if (pool.Length == 0) { Panel.Spotlight = null; return; }

        _spotlightIndex = (_spotlightIndex + 1) % pool.Length;
        Panel.Spotlight = pool[_spotlightIndex];
        _spotlightPinned = true;
        _lastRotate = DateTime.UtcNow;
    }

    public void UnpinSpotlight()
    {
        _spotlightPinned = false;
        _spotlightIndex = -1;
        Panel.Spotlight = null;
    }

    /// <summary>When not pinned, cycle the spotlight through the top performers slowly.</summary>
    private void RotateSpotlight()
    {
        if (_spotlightPinned) return;
        if ((DateTime.UtcNow - _lastRotate).TotalSeconds < 12) return;
        _lastRotate = DateTime.UtcNow;

        PlayerStats[] pool;
        lock (_state.Gate) pool = _state.Ranked().Take(5).ToArray();
        if (pool.Length == 0) { Panel.Spotlight = null; return; }

        _spotlightIndex = (_spotlightIndex + 1) % pool.Length;
        Panel.Spotlight = pool[_spotlightIndex];
    }
}
