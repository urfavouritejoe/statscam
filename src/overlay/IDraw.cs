namespace StatsCam.Overlay;

/// <summary>Simple RGBA colour, so nothing outside the overlay touches UnityEngine.Color.</summary>
public readonly struct Col
{
    public readonly float R, G, B, A;
    public Col(float r, float g, float b, float a = 1f) { R = r; G = g; B = b; A = a; }

    /// <summary>From 0xRRGGBB with a separate alpha — how the theme is written.</summary>
    public static Col Hex(uint rgb, float a = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public Col WithAlpha(float a) => new(R, G, B, a);
}

public enum Align { Left, Center, Right }

/// <summary>
/// The drawing surface the overlay renders through.
///
/// Exists so the layout code has exactly one implementation but two backends: the real
/// Unity IMGUI binding in the game, and an SVG writer in the test harness. That means the
/// panel and stats board can be laid out, rendered and inspected without VRFS running —
/// the geometry is the same code either way.
/// </summary>
public interface IDraw
{
    bool Ready { get; }
    int ScreenW { get; }
    int ScreenH { get; }

    void Init();
    void Fill(float x, float y, float w, float h, Col c);
    void Text(float x, float y, float w, float h, string s, int size, Col c, Align align, bool bold);
}

/// <summary>Static facade over the active backend, so call sites stay terse.</summary>
internal static class Gui
{
    public static IDraw Backend = new UnityDraw();

    public static bool Ready => Backend.Ready;
    public static int ScreenW => Backend.ScreenW;
    public static int ScreenH => Backend.ScreenH;

    public static void Init() => Backend.Init();

    public static void Fill(float x, float y, float w, float h, Col c)
        => Backend.Fill(x, y, w, h, c);

    public static void Text(float x, float y, float w, float h, string s, int size, Col c,
                            Align align = Align.Left, bool bold = false)
        => Backend.Text(x, y, w, h, s, size, c, align, bold);

    /// <summary>Rectangle outline. Used sparingly — panel edges and shot markers.</summary>
    public static void Frame(float x, float y, float w, float h, Col c, float t = 1f)
    {
        Fill(x, y, w, t, c);
        Fill(x, y + h - t, w, t, c);
        Fill(x, y, t, h, c);
        Fill(x + w - t, y, t, h, c);
    }
}
