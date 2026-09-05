using System;
using System.Diagnostics;
using System.Reflection;
using MelonLoader;
using StatsCam;
using StatsCam.Capture;
using StatsCam.Model;
using StatsCam.Overlay;

[assembly: MelonInfo(typeof(Core), "StatsCam", "0.2.0", "joefant")]
[assembly: MelonGame]

namespace StatsCam;

/// <summary>
/// Live stats and broadcast graphics for the VRFS Camera client.
///
/// Capture taps MatchEventSink.Write, normalizes into StatEvent, and folds those into a
/// single MatchState. The live panel, the goal flash and the Y stats board are all pure
/// readers of that state, so they can never disagree with each other.
/// </summary>
public class Core : MelonMod
{
    private const double InstallRetrySeconds = 5;

    // Generous on purpose. A first launch does interop generation and shader compilation
    // before the first scene is up, and giving up early would look identical to the hook
    // failing — which is the one thing this build exists to measure.
    private const double InstallGiveUpSeconds = 600;
    private const double RosterSeconds = 2;
    private const double HeartbeatSeconds = 30;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Config _cfg;
    private MatchState _state;
    private Roster _roster;
    private SinkEventSource _source;
    private OverlayRoot _overlay;

    private double _nextInstall, _nextRoster, _nextHeartbeat = HeartbeatSeconds;
    private bool _installed, _gaveUp;
    private int _lastLoggedEvents;

    // ------------------------------------------------------------ lifecycle

    public override void OnInitializeMelon()
    {
        Journal.Open();
        Journal.Head("StatsCam 0.2.0");
        Journal.Line($"started   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Journal.Line($"runtime   .NET {Environment.Version}");
        Journal.Line($"unity     {UnityVersion()}");

        _cfg = Config.Load();
        _state = new MatchState();
        _roster = new Roster(_state);
        _overlay = new OverlayRoot(_state) { Enabled = _cfg.OverlayEnabled };
        _source = new SinkEventSource(OnEvent);

        Journal.Blank();
        Journal.Line("Y  full stats board");
        Journal.Line("F1 overlay   F2 density   F3 spotlight   F4 unpin   F8 replay goal");
        Journal.Line("F10 roster   F11 catalogue   F12 summary");
    }

    public override void OnLateInitializeMelon()
    {
        Reflect.Snapshot();
        TryInstall();
    }

    public override void OnUpdate()
    {
        var t = _clock.Elapsed.TotalSeconds;

        if (!_installed && !_gaveUp && t >= _nextInstall)
        {
            _nextInstall = t + InstallRetrySeconds;
            if (t > InstallGiveUpSeconds)
            {
                _gaveUp = true;
                Journal.Fail($"Gave up resolving game types after {InstallGiveUpSeconds:0}s.");
                Journal.Line("Interop generation probably did not produce Assembly-CSharp;");
                Journal.Line("check MelonLoader's own log for Cpp2IL errors on metadata v39.");
            }
            else TryInstall();
        }

        if (!_installed) { PollHotkeys(); return; }

        if (t >= _nextRoster)
        {
            _nextRoster = t + RosterSeconds;
            SyncMatchFlow();
        }

        if (t >= _nextHeartbeat)
        {
            _nextHeartbeat = t + HeartbeatSeconds;
            Heartbeat();
        }

        PollHotkeys();
    }

    public override void OnGUI() => _overlay?.Draw();

    public override void OnApplicationQuit()
    {
        MatchRecord.Save(_state, "session-end");
        Journal.Head("Session end");
        Journal.Line($"log written to {Journal.Path}");
        Journal.Close();
    }

    // ------------------------------------------------------------ install

    private void TryInstall()
    {
        if (Reflect.Find("VRFS.MatchAnalytics.MatchEventSink") == null &&
            Reflect.Find("PlayerView") == null)
            return;

        _installed = true;
        Journal.Line($"game types visible after {_clock.Elapsed.TotalSeconds:0.0}s");

        if (_cfg.Diagnostics) Catalogue.Run();

        Journal.Head("Capture");
        _source.Install(HarmonyInstance);
        _roster.Install();

        // One-off dump of the live CustomProperties hashtables. This is what resolves
        // the obfuscated PlayerPropertyNames / RoomPropertyNames key tables.
        if (_cfg.Diagnostics)
        {
            PhotonProbe.Install();
            PhotonProbe.Poll(force: true);
        }

        Journal.Head("Armed");
        Journal.Line(_source.Active
            ? "Join a lobby with the camera pin code. Stats start on the first event. Press Y for the board."
            : "Sink hook is NOT active — no events will be captured. See errors above.");
    }

    // ------------------------------------------------------------ capture

    private void OnEvent(StatEvent e)
    {
        // First event in an idle session means a match is under way, even if we joined late.
        if (_state.Phase == MatchPhase.Idle) _state.BeginMatch(_roster.RoomId);

        _state.Apply(e);

        if (_cfg.Diagnostics && e.Kind is EventKind.Goal or EventKind.Save or EventKind.Shot)
            Journal.Ev(e.Kind.ToString(), e.ToString());
    }

    /// <summary>
    /// Match boundaries are heuristic until the flow events are confirmed hookable:
    /// a new Photon room starts a match, leaving the room ends it.
    /// </summary>
    private void SyncMatchFlow()
    {
        var wasIn = _state.Phase is not MatchPhase.Idle and not MatchPhase.Ended;
        var roomChanged = _roster.Sync();
        var inRoom = _roster.InRoom();

        if (roomChanged && inRoom)
        {
            Journal.Head($"Match start · room {_roster.RoomId}");
            _state.BeginMatch(_roster.RoomId);
        }
        else if (wasIn && !inRoom)
        {
            Journal.Head("Match end");
            _state.EndMatch();
            MatchRecord.Save(_state, "left-room");
        }
    }

    private void Heartbeat()
    {
        int events, players;
        lock (_state.Gate)
        {
            events = _state.Log.Count;
            players = _state.Players.Count;
        }

        if (events == _lastLoggedEvents && _roster.InRoom() && events == 0)
        {
            Journal.Ev("HEARTBEAT", "in a room, still ZERO events — the sink is not firing here");
        }
        else
        {
            Journal.Ev("HEARTBEAT",
                $"{events} events · {players} players · {_state.Score[0]}-{_state.Score[1]} · {_state.Phase}");
        }
        _lastLoggedEvents = events;
    }

    // ------------------------------------------------------------ hotkeys

    private static MethodInfo _getKeyDown;
    private static object[] _keys;
    private static bool _keysReady, _keysFailed;
    private static readonly string[] KeyNames = { "Y", "F1", "F2", "F3", "F4", "F8", "F10", "F11", "F12" };

    private void PollHotkeys()
    {
        if (_keysFailed) return;

        if (!_keysReady)
        {
            try
            {
                var input = Reflect.Find("UnityEngine.Input");
                var keyCode = Reflect.Find("UnityEngine.KeyCode");
                if (input == null || keyCode == null) return;

                _getKeyDown = input.GetMethod("GetKeyDown", new[] { keyCode });
                if (_getKeyDown == null) { _keysFailed = true; return; }

                _keys = new object[KeyNames.Length];
                for (var i = 0; i < KeyNames.Length; i++) _keys[i] = Enum.Parse(keyCode, KeyNames[i]);
                _keysReady = true;
            }
            catch { _keysFailed = true; return; }
        }

        if (Down(0)) _overlay.Board.Toggle();
        if (Down(1)) { _overlay.Toggle(); Journal.Line($"overlay {(_overlay.Enabled ? "on" : "off")}"); }
        if (Down(2)) _overlay.Panel.Cycle();
        if (Down(3)) _overlay.CycleSpotlight();
        if (Down(4)) _overlay.UnpinSpotlight();
        if (Down(5)) _overlay.Flash.Replay();
        if (Down(6)) PhotonProbe.Poll(force: true);
        if (Down(7)) Catalogue.Run();
        if (Down(8)) Heartbeat();
    }

    private static bool Down(int i)
    {
        try { return _getKeyDown.Invoke(null, new[] { _keys[i] }) is bool b && b; }
        catch { _keysFailed = true; return false; }
    }

    private static string UnityVersion()
    {
        var app = Reflect.Find("UnityEngine.Application");
        return app == null ? "(unknown)" : Reflect.Show(Reflect.Static(app, "unityVersion"));
    }
}
