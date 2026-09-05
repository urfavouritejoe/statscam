using System;
using System.IO;
using System.Text;
using MelonLoader;

namespace StatsCam;

/// <summary>
/// Dual logger: everything goes to the MelonLoader console and to a plain text file
/// next to the game exe. The file is the Phase 0 deliverable — it is what gets read
/// back to answer the open questions.
/// </summary>
internal static class Journal
{
    private static StreamWriter _w;
    private static readonly object Gate = new();
    public static string Path { get; private set; }

    public static void Open()
    {
        try
        {
            var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StatsCam");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, $"phase0-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _w = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
            {
                AutoFlush = true
            };
            MelonLogger.Msg($"Journal -> {Path}");
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"Could not open journal file: {e.Message}. Console only.");
        }
    }

    public static void Line(string s = "")
    {
        MelonLogger.Msg(s);
        Write(s);
    }

    public static void Warn(string s)
    {
        MelonLogger.Warning(s);
        Write("[warn] " + s);
    }

    public static void Fail(string s)
    {
        MelonLogger.Error(s);
        Write("[FAIL] " + s);
    }

    public static void Blank() => Line();

    public static void Head(string s)
    {
        Line();
        Line("=== " + s + " " + new string('=', Math.Max(0, 68 - s.Length)));
    }

    /// <summary>Timestamped event line — the format the log is grepped on later.</summary>
    public static void Ev(string tag, string body)
        => Line($"[{DateTime.Now:HH:mm:ss.fff}] {tag,-14} {body}");

    private static void Write(string s)
    {
        if (_w == null) return;
        lock (Gate)
        {
            try { _w.WriteLine(s); } catch { /* a broken log must never take the game down */ }
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            try { _w?.Flush(); _w?.Dispose(); } catch { }
            _w = null;
        }
    }
}
