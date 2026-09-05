using System;
using System.Linq;
using System.Reflection;
using StatsCam.Model;

namespace StatsCam.Capture;

/// <summary>
/// Startup diagnostics: confirm the types the design depends on exist at runtime, and
/// read the pitch dimensions out of the game rather than guessing them.
///
/// Cheap to run, and it validates the entire static metadata read in one pass.
/// </summary>
internal static class Catalogue
{
    private static readonly string[] Wanted =
    {
        "VRFS.MatchAnalytics.MatchEvent",
        "VRFS.MatchAnalytics.MatchEventType",
        "VRFS.MatchAnalytics.MatchEventSink",
        "VRFS.MatchAnalytics.MatchEventStreamRecorder",
        "VRFS.MatchAnalytics.MatchContext",
        "VRFS.MatchAnalytics.CardStats",
        "VRFS.MatchAnalytics.CardEntryKind",
        "VRFS.MatchAnalytics.PlayerReportDto",
        "VRFS.MatchAnalytics.ShotReportDto",
        "VRFS.MatchAnalytics.SkillsDto",
        "VRFS.MatchAnalytics.StyleDto",
        "VRFS.MatchAnalytics.PitchGeometry",
        "VRFS.MatchAnalytics.PossessionClassifier",
        "VRFS.MatchAnalytics.ShotDetector",
        "VRFS.MatchAnalytics.DefensiveMetrics",
        "VRFS.MatchAnalytics.XGModel",
        "VRFS.MatchAnalytics.SkillCardAnalyticsRunner",
        "VRFS.MatchAnalytics.Overlay.OverlayVisualStyle",
        "VRFS.MatchAnalytics.Endpoint.MatchMetricsRow",
        "BallTouchBodyPart",
        "PlayerView",
        "PlayersManager",
    };

    public static void Run()
    {
        Journal.Head("Analytics type catalogue");

        var hit = 0;
        foreach (var name in Wanted)
        {
            var t = Reflect.Find(name);
            if (t != null) hit++;
            Journal.Line($"  {(t != null ? "ok  " : "MISS")} {name}");
        }
        Journal.Line($"{hit}/{Wanted.Length} resolved");

        DumpEnum("VRFS.MatchAnalytics.MatchEventType");
        DumpEnum("VRFS.MatchAnalytics.CardEntryKind");
        DumpEnum("BallTouchBodyPart");

        ReadPitch();
    }

    private static void DumpEnum(string fullName)
    {
        var t = Reflect.Find(fullName);
        if (t == null) return;

        try
        {
            var names = t.IsEnum
                ? Enum.GetNames(t)
                : t.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name).ToArray();
            Journal.Line($"  {t.Name}: {string.Join(", ", names)}");
        }
        catch (Exception e)
        {
            Journal.Warn($"  could not read {fullName}: {e.Message}");
        }
    }

    /// <summary>
    /// PitchGeometry exposes GoalA / GoalB / PlayArea as public fields. Reading them
    /// makes the shot map and the goal-distance figure correct per arena instead of
    /// assuming one pitch size across 54 scenes.
    /// </summary>
    private static void ReadPitch()
    {
        var t = Reflect.Find("VRFS.MatchAnalytics.PitchGeometry");
        if (t == null) return;

        var a = Reflect.Static(t, "GoalA");
        var b = Reflect.Static(t, "GoalB");
        if (a == null || b == null)
        {
            Journal.Line("  PitchGeometry present but GoalA/GoalB are instance data — " +
                         "using default pitch until a live instance is found");
            return;
        }

        var az = Num(Reflect.Member(a, "z"));
        var bz = Num(Reflect.Member(b, "z"));
        var half = Math.Abs(az - bz) / 2f;
        if (half > 5f && half < 200f)
        {
            Pitch.HalfLength = half;
            Journal.Line($"  pitch half-length read from PitchGeometry: {half:0.0} m");
        }
    }

    private static float Num(object v) => v switch
    {
        float f => f, double d => (float)d, int i => i, _ => 0f
    };
}
