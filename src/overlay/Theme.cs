namespace StatsCam.Overlay;

/// <summary>
/// One place for every colour and metric on screen. Kit colours are StatsCam's own, not
/// read from the game — team 0 is blue, team 1 is red, matching MatchEvent.ActorTeam.
/// </summary>
public static class Theme
{
    // Ground and ink — dark, low-saturation, sits under football footage without fighting it.
    public static Col Panel      = Col.Hex(0x0E1512, 0.88f);
    /// <summary>The Y board sits on black, so its own ground is opaque and near-black.</summary>
    public static Col Board      = Col.Hex(0x080C0A);
    public static Col PanelEdge  = Col.Hex(0x3A4841, 0.55f);
    public static Col Divider    = Col.Hex(0x28332D, 0.85f);
    public static Col Strip      = Col.Hex(0x1A231E, 0.92f);

    public static Col Ink        = Col.Hex(0xF2F6F3);
    public static Col Body       = Col.Hex(0xC6D2CB);
    public static Col Muted      = Col.Hex(0x8B9A92);
    public static Col Accent     = Col.Hex(0xE0A04A);

    // Team kits. Index 0 and 1 match MatchEvent.ActorTeam.
    public static readonly Col[] Kit = { Col.Hex(0x4A8FE0), Col.Hex(0xD9563E) };
    public static readonly string[] KitName = { "BLUE", "RED" };

    // Geometry
    public const float PanelWidth = 336f;
    public const float MarginX = 26f;
    public const float MarginY = 26f;
    public const float RowHeight = 21f;

    public static Col TeamColour(int team) => team is 0 or 1 ? Kit[team] : Muted;
    public static string TeamName(int team) => team is 0 or 1 ? KitName[team] : "—";
}
