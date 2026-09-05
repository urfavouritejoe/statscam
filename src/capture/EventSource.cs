using System;
using System.Reflection;
using HarmonyLib;
using StatsCam.Model;

namespace StatsCam.Capture;

/// <summary>
/// Where StatEvents come from. Two implementations are anticipated:
///
///   SinkEventSource     hooks MatchEventSink.Write — the primary path.
///   DerivedEventSource  reconstructs events from ball and player transforms, for the
///                       case where the sink turns out not to fire on a spectator client.
///
/// Everything downstream of this interface is identical either way, which is the whole
/// point: the one unresolved question in the design touches exactly one file.
/// </summary>
public interface IEventSource
{
    bool Active { get; }
    string Describe();
    void Install(Harmony harmony);
}

/// <summary>
/// Hooks VRFS.MatchAnalytics.MatchEventSink.Write and converts each il2cpp MatchEvent
/// into our own StatEvent. Nothing but this class knows the game's event type exists.
/// </summary>
public sealed class SinkEventSource : IEventSource
{
    private const string SinkType = "VRFS.MatchAnalytics.MatchEventSink";

    private static Action<StatEvent> _emit;
    private static bool _warnedShape;

    /// <summary>
    /// The first event logs the real runtime member list. That is the ground truth the
    /// static metadata read gets checked against, and it costs one line to keep.
    /// </summary>
    private static bool _shapeDumped;

    public bool Active { get; private set; }
    public string Describe() => Active ? "MatchEventSink.Write hook" : "MatchEventSink hook (not installed)";

    public SinkEventSource(Action<StatEvent> emit) => _emit = emit;

    public void Install(Harmony harmony)
    {
        var sink = Reflect.Find(SinkType);
        if (sink == null)
        {
            Journal.Fail($"{SinkType} not found — capture cannot start.");
            return;
        }

        var write = Reflect.Method(sink, "Write");
        if (write == null)
        {
            Journal.Fail("MatchEventSink.Write not found.");
            return;
        }

        try
        {
            harmony.Patch(write, prefix: new HarmonyMethod(
                typeof(SinkEventSource).GetMethod(nameof(OnWrite), BindingFlags.Static | BindingFlags.NonPublic)));
            Active = true;
            Journal.Line($"capture: hooked {sink.FullName}.Write");
        }
        catch (Exception e)
        {
            Journal.Fail($"capture: Harmony patch failed — {e.Message}");
        }
    }

    private static void OnWrite(object[] __args)
    {
        try
        {
            var raw = __args is { Length: > 0 } ? __args[0] : null;
            if (raw == null)
            {
                if (!_warnedShape)
                {
                    _warnedShape = true;
                    Journal.Warn("capture: sink fired but the argument did not marshal.");
                }
                return;
            }

            if (!_shapeDumped)
            {
                _shapeDumped = true;
                Journal.Head("MatchEvent runtime shape");
                Journal.Line(Reflect.Describe(raw.GetType()));
                Journal.Blank();
            }

            _emit?.Invoke(Convert(raw));
        }
        catch (Exception e)
        {
            if (!_warnedShape)
            {
                _warnedShape = true;
                Journal.Warn($"capture: conversion failed — {e.Message}");
            }
        }
    }

    // ------------------------------------------------------------ conversion

    private static StatEvent Convert(object raw)
    {
        var e = new StatEvent
        {
            Kind          = MapKind(Reflect.Member(raw, "Type")),
            MatchTime     = F(Reflect.Member(raw, "MatchTime")),
            ActorId       = S(Reflect.Member(raw, "ActorUserId")),
            ActorTeam     = I(Reflect.Member(raw, "ActorTeam"), -1),
            SecondaryId   = S(Reflect.Member(raw, "SecondaryUserId")),
            SecondaryTeam = I(Reflect.Member(raw, "SecondaryTeam"), -1),
            BallPosition  = V(Reflect.Member(raw, "BallPosition")),
            BallVelocity  = V(Reflect.Member(raw, "BallVelocity")),
            OnTarget      = B(Reflect.Member(raw, "OnTarget")),
            Value         = F(Reflect.Member(raw, "Value")),
            NearestOpponent = F(Reflect.Member(raw, "NearestOpponent")),
        };

        return e;
    }

    /// <summary>
    /// Map by enum member NAME, never by ordinal. VRFS can renumber MatchEventType in a
    /// patch without renaming its members, and name matching survives that.
    /// </summary>
    private static EventKind MapKind(object v)
    {
        if (v == null) return EventKind.Unknown;
        var name = v.ToString();
        return Enum.TryParse<EventKind>(name, ignoreCase: true, out var k) ? k : EventKind.Unknown;
    }

    private static float F(object v) => v switch
    {
        float f => f, double d => (float)d, int i => i, _ => 0f
    };

    private static int I(object v, int fallback) => v switch
    {
        int i => i, byte b => b, short s => s, float f => (int)f,
        _ => v != null && v.GetType().IsEnum ? System.Convert.ToInt32(v) : fallback
    };

    private static bool B(object v) => v is bool b && b;

    private static string S(object v)
    {
        var s = v as string;
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static Vec3 V(object v)
    {
        if (v == null) return default;
        var x = Reflect.Member(v, "x"); var y = Reflect.Member(v, "y"); var z = Reflect.Member(v, "z");
        return new Vec3(F(x), F(y), F(z));
    }
}
