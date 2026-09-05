using System;
using System.Globalization;
using System.Text;
using StatsCam.Overlay;

namespace StatsCam.Harness;

/// <summary>
/// An IDraw that writes SVG instead of calling Unity.
///
/// The point is that StatPanel, GoalFlash and StatsBoard run their real layout code
/// through this, so the geometry that ships is the geometry that gets inspected. It
/// approximates IMGUI text metrics closely enough to catch overlaps, overflow and
/// misalignment — it is not a pixel-accurate Unity emulator.
/// </summary>
internal sealed class SvgDraw : IDraw
{
    private readonly StringBuilder _sb = new();
    private int _rects, _texts;

    public bool Ready => true;
    public int ScreenW { get; }
    public int ScreenH { get; }

    public int RectCount => _rects;
    public int TextCount => _texts;

    /// <summary>Bounds of everything drawn, for checking nothing escaped the screen.</summary>
    public float MinX = float.MaxValue, MinY = float.MaxValue;
    public float MaxX = float.MinValue, MaxY = float.MinValue;

    public SvgDraw(int w, int h)
    {
        ScreenW = w;
        ScreenH = h;
    }

    public void Init() { }

    public void Fill(float x, float y, float w, float h, Col c)
    {
        if (w <= 0 || h <= 0) return;
        _rects++;
        Track(x, y, w, h);
        _sb.Append($"<rect x='{N(x)}' y='{N(y)}' width='{N(w)}' height='{N(h)}' ")
           .Append($"fill='{Hex(c)}' fill-opacity='{N(c.A)}'/>\n");
    }

    public void Text(float x, float y, float w, float h, string s, int size, Col c,
                     Align align, bool bold)
    {
        if (string.IsNullOrEmpty(s)) return;
        _texts++;
        Track(x, y, w, h);

        // IMGUI centres text vertically in the rect; mirror that.
        var ty = y + h / 2f;
        var (tx, anchor) = align switch
        {
            Align.Center => (x + w / 2f, "middle"),
            Align.Right => (x + w, "end"),
            _ => (x, "start")
        };

        _sb.Append($"<text x='{N(tx)}' y='{N(ty)}' font-size='{size}' ")
           .Append($"font-family='Segoe UI, DejaVu Sans, sans-serif' ")
           .Append($"font-weight='{(bold ? "700" : "400")}' ")
           .Append($"text-anchor='{anchor}' dominant-baseline='central' ")
           .Append($"fill='{Hex(c)}' fill-opacity='{N(c.A)}'>")
           .Append(Esc(s))
           .Append("</text>\n");
    }

    private void Track(float x, float y, float w, float h)
    {
        MinX = Math.Min(MinX, x); MinY = Math.Min(MinY, y);
        MaxX = Math.Max(MaxX, x + w); MaxY = Math.Max(MaxY, y + h);
    }

    public string ToSvg(string backdrop = "#101713")
        => Wrap(0, 0, ScreenW, ScreenH, backdrop);

    /// <summary>Same drawing, viewBox cropped to a region — for inspecting one panel at size.</summary>
    public string ToSvgCropped(float x, float y, float w, float h, string backdrop = "#101713")
        => Wrap(x, y, w, h, backdrop);

    private string Wrap(float vx, float vy, float vw, float vh, string backdrop)
        => $"<svg xmlns='http://www.w3.org/2000/svg' " +
           $"viewBox='{N(vx)} {N(vy)} {N(vw)} {N(vh)}' width='{N(vw)}' height='{N(vh)}'>\n" +
           $"<rect x='{N(vx)}' y='{N(vy)}' width='{N(vw)}' height='{N(vh)}' fill='{backdrop}'/>\n" +
           _sb + "</svg>\n";

    private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Hex(Col c)
        => $"#{(int)Math.Round(Math.Clamp(c.R, 0, 1) * 255):x2}" +
           $"{(int)Math.Round(Math.Clamp(c.G, 0, 1) * 255):x2}" +
           $"{(int)Math.Round(Math.Clamp(c.B, 0, 1) * 255):x2}";

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
