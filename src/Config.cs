using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StatsCam;

/// <summary>
/// Plain key=value config, written with defaults on first run so it is discoverable
/// without documentation. Deliberately not JSON: this file gets edited at 11pm between
/// matches, and a missing comma should not cost a stream.
/// </summary>
public sealed class Config
{
    public bool OverlayEnabled = true;
    public bool Diagnostics = true;

    public static string PathOnDisk =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StatsCam", "statscam.cfg");

    public static Config Load()
    {
        var cfg = new Config();
        try
        {
            var path = PathOnDisk;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, Template);
                Journal.Line($"config: wrote defaults to {path}");
                return cfg;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                map[line[..i].Trim()] = line[(i + 1)..].Trim();
            }

            cfg.OverlayEnabled = Bool(map, "overlay.enabled", cfg.OverlayEnabled);
            cfg.Diagnostics = Bool(map, "diagnostics", cfg.Diagnostics);

            Journal.Line($"config: loaded {path}");
        }
        catch (Exception e)
        {
            Journal.Warn($"config: using defaults — {e.Message}");
        }
        return cfg;
    }

    private static bool Bool(IReadOnlyDictionary<string, string> m, string k, bool d)
        => m.TryGetValue(k, out var v)
            ? v is "1" or "true" or "yes" or "on"
            : d;

    private const string Template = @"# StatsCam configuration
# Edit and restart Camera.exe.

# Draw the live panel, goal flash and Y stats board inside the camera window.
overlay.enabled = 1

# Startup type catalogue and verbose event logging to StatsCam\*.log
diagnostics = 1
";
}
