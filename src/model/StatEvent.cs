using System;

namespace StatsCam.Model;

/// <summary>
/// Our own event taxonomy, mirroring VRFS.MatchAnalytics.MatchEventType.
///
/// Deliberately a separate enum rather than the game's: it decouples every consumer
/// from the game assembly, survives a renumbering upstream, and lets the fallback
/// transform-derived source emit the same shapes.
/// </summary>
public enum EventKind
{
    Unknown = 0,
    OwnershipChange,
    Shot,
    Goal,
    Deflection,
    Pass,
    Interception,
    Turnover,
    Save,
    ControlEpisode,
    InferredShot,
    Block,
    Clearance,
}

public readonly struct Vec3
{
    public readonly float X, Y, Z;
    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public float Magnitude => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
    public float FlatDistanceTo(Vec3 o)
    {
        float dx = X - o.X, dz = Z - o.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
    public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
}

/// <summary>
/// One normalized match event. This is the only thing the stat model, the overlay
/// and the dashboard ever see — nothing downstream touches an il2cpp object.
/// </summary>
public sealed class StatEvent
{
    public EventKind Kind;
    public float MatchTime;

    public string ActorId;
    public int ActorTeam = -1;
    public string SecondaryId;
    public int SecondaryTeam = -1;

    public Vec3 BallPosition;
    public Vec3 BallVelocity;

    public bool OnTarget;
    public float Value;            // xG for shots, xT for passes and carries
    public float NearestOpponent;

    /// <summary>Wall-clock arrival, used for overlay timing rather than match logic.</summary>
    public DateTime Received = DateTime.UtcNow;

    /// <summary>Ball speed in km/h — what a broadcast graphic actually wants to show.</summary>
    public float BallSpeedKph => BallVelocity.Magnitude * 3.6f;

    public override string ToString()
        => $"{Kind} t={MatchTime:0.0} actor={ActorId} team={ActorTeam} val={Value:0.###}";
}
