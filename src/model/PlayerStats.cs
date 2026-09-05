using System;

namespace StatsCam.Model;

/// <summary>
/// Per-player accumulator. Field-for-field a mirror of VRFS.MatchAnalytics.CardStats,
/// plus the identity and spatial data the game keeps elsewhere.
/// </summary>
public sealed class PlayerStats
{
    // ---- identity -------------------------------------------------------
    public string UserId = "";
    public string Name = "";
    public int ActorNumber = -1;
    public int Team = -1;
    public bool Present = true;

    // ---- CardStats mirror -----------------------------------------------
    public int Goals;
    public int OwnGoals;
    public int Assists;
    public int Shots;
    public int ShotsOnTarget;
    public int Touches;
    public int Passes;
    public int PassesCompleted;
    public int Interceptions;
    public int Blocks;
    public int Clearances;
    public int Saves;
    public int Turnovers;

    public float Xg;
    public float Xa;
    public float XtPass;
    public float XtCarry;
    public float Prevented;
    public float SaveValue;
    public float PossessionSeconds;
    public float LossValue;
    public float FailedCatchValue;
    public float RecoveredValue;

    // ---- derived ---------------------------------------------------------
    public float Xt => XtPass + XtCarry;
    public float Defensive => Prevented + SaveValue + RecoveredValue;

    /// <summary>
    /// StatsCam's own ranking score, not the game's CardStats.get_Composite. Derived
    /// entirely from captured events; used to order the player table and pick the
    /// standout performer.
    /// </summary>
    public float Rating => Goals * 3f + Assists * 2f + Xg + Xa + Xt + Defensive * 0.5f - LossValue * 0.3f;

    public float PassAccuracy => Passes == 0 ? 0f : PassesCompleted / (float)Passes;
    public float ShotAccuracy => Shots == 0 ? 0f : ShotsOnTarget / (float)Shots;

    /// <summary>Display name that never renders as an empty box on the overlay.</summary>
    public string Label => string.IsNullOrWhiteSpace(Name) ? (UserId ?? "?") : Name;

    public PlayerStats Clone() => (PlayerStats)MemberwiseClone();
}
