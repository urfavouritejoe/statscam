using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace StatsCam;

/// <summary>
/// Everything we know about the game is looked up by string at runtime.
///
/// Il2CppInterop emits ordinary managed classes whose properties marshal to
/// unmanaged memory, so plain System.Reflection reads them correctly. Resolving
/// this way means the mod compiles with no reference to the generated interop
/// assemblies, and keeps working when VRFS re-obfuscates private members — the
/// public DTO surface we depend on is stable across patches.
/// </summary>
internal static class Reflect
{
    private static readonly Dictionary<string, Type> TypeCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Interop assemblies are loaded lazily, so a type can be perfectly resolvable and
    /// still invisible to a scan of AppDomain.GetAssemblies(). These are pulled in
    /// explicitly the first time a lookup misses.
    ///
    /// Note the names carry no Il2Cpp prefix — MelonLoader only prefixes where the name
    /// would collide with a real managed assembly, so the game's own code keeps its
    /// original name.
    /// </summary>
    private static readonly string[] GameAssemblies =
    {
        "Assembly-CSharp",
        "Assembly-CSharp-firstpass",
        "UnityEngine.CoreModule",
        "UnityEngine.IMGUIModule",
        "UnityEngine.InputLegacyModule",
        "Il2CppPhotonRealtime",
        "Il2CppPhotonUnityNetworking",
    };

    private static bool _preloaded;

    /// <summary>Find a type by its full name, loading the game assemblies if needed.</summary>
    public static Type Find(string fullName)
    {
        if (TypeCache.TryGetValue(fullName, out var hit)) return hit;

        var found = Scan(fullName);

        if (found == null && !_preloaded)
        {
            _preloaded = true;
            Preload();
            found = Scan(fullName);
        }

        TypeCache[fullName] = found;
        return found;
    }

    private static Type Scan(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { /* an assembly that refuses reflection is not fatal */ }
        }
        return null;
    }

    private static void Preload()
    {
        foreach (var name in GameAssemblies)
        {
            try
            {
                Assembly.Load(new AssemblyName(name));
                Journal.Line($"reflect: loaded {name}");
            }
            catch (Exception e)
            {
                Journal.Line($"reflect: could not load {name} — {e.GetType().Name}");
            }
        }
    }

    /// <summary>Find the first type whose full name ends with the given suffix.</summary>
    public static Type FindEndingWith(string suffix)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }
            foreach (var t in types)
                if (t.FullName != null && t.FullName.EndsWith(suffix, StringComparison.Ordinal))
                    return t;
        }
        return null;
    }

    private const BindingFlags Any =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>Read a field or property by name. Returns null rather than throwing.</summary>
    public static object Member(object target, string name, Type declaring = null)
    {
        if (target == null && declaring == null) return null;
        var t = declaring ?? target.GetType();

        try
        {
            var p = t.GetProperty(name, Any);
            if (p != null && p.CanRead) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : target);
        }
        catch { }

        try
        {
            var f = t.GetField(name, Any);
            if (f != null) return f.GetValue(f.IsStatic ? null : target);
        }
        catch { }

        return null;
    }

    public static object Static(Type t, string name) => Member(null, name, t);

    /// <summary>Invoke a method by name with no arguments.</summary>
    public static object Call(object target, string name, Type declaring = null)
    {
        var t = declaring ?? target?.GetType();
        if (t == null) return null;
        try
        {
            var m = t.GetMethod(name, Any, null, System.Type.EmptyTypes, null);
            if (m == null) return null;
            return m.Invoke(m.IsStatic ? null : target, null);
        }
        catch { return null; }
    }

    public static MethodInfo Method(Type t, string name)
    {
        if (t == null) return null;
        try { return t.GetMethods(Any).FirstOrDefault(m => m.Name == name); }
        catch { return null; }
    }

    /// <summary>
    /// Human-readable dump of a type's members. Used once per interesting type so the
    /// log records the real runtime shape, which is the ground truth the static
    /// metadata read is checked against.
    /// </summary>
    public static string Describe(Type t, int max = 80)
    {
        if (t == null) return "(null type)";
        var sb = new StringBuilder();
        sb.Append(t.FullName).Append("  [").Append(t.Assembly.GetName().Name).Append(']').AppendLine();

        try
        {
            var props = t.GetProperties(Any).Take(max).ToArray();
            if (props.Length > 0)
            {
                sb.AppendLine("  properties:");
                foreach (var p in props)
                    sb.Append("    ").Append(Short(p.PropertyType)).Append(' ').Append(p.Name).AppendLine();
            }

            var fields = t.GetFields(Any).Take(max).ToArray();
            if (fields.Length > 0)
            {
                sb.AppendLine("  fields:");
                foreach (var f in fields)
                    sb.Append("    ").Append(Short(f.FieldType)).Append(' ').Append(f.Name).AppendLine();
            }
        }
        catch (Exception e)
        {
            sb.Append("  (member enumeration failed: ").Append(e.Message).Append(')').AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string Short(Type t)
    {
        var n = t?.Name ?? "?";
        return n.StartsWith("Il2Cpp", StringComparison.Ordinal) ? n.Substring(6) : n;
    }

    /// <summary>Best-effort readable rendering of any value, including il2cpp structs.</summary>
    public static string Show(object v)
    {
        if (v == null) return "null";

        switch (v)
        {
            case string s: return '"' + s + '"';
            case float f: return f.ToString("0.###");
            case double d: return d.ToString("0.###");
            case bool b: return b ? "true" : "false";
        }

        var t = v.GetType();

        // Vector3 and friends: pull x/y/z rather than relying on ToString.
        var x = Member(v, "x"); var y = Member(v, "y"); var z = Member(v, "z");
        if (x is float fx && y is float fy)
        {
            return z is float fz
                ? $"({fx:0.##}, {fy:0.##}, {fz:0.##})"
                : $"({fx:0.##}, {fy:0.##})";
        }

        if (t.IsEnum) return v.ToString();

        try
        {
            var s = v.ToString();
            // Il2Cpp objects with no override render as the type name; keep it short.
            return string.IsNullOrEmpty(s) ? t.Name : s;
        }
        catch { return t.Name; }
    }

    /// <summary>Enumerate an il2cpp Hashtable / Dictionary into readable pairs.</summary>
    public static IEnumerable<string> Pairs(object table)
    {
        if (table == null) yield break;

        // Il2Cpp collections do not implement managed IEnumerable, so walk Keys and index.
        var keys = Member(table, "Keys");
        var count = Member(table, "Count");
        if (keys == null)
        {
            yield return $"(unenumerable {table.GetType().Name}, Count={Show(count)})";
            yield break;
        }

        var list = new List<object>();
        if (keys is IEnumerable managed && keys is not string)
        {
            foreach (var k in managed) list.Add(k);
        }
        else
        {
            // Il2Cpp key collection: try Count + item indexer.
            var kc = Member(keys, "Count") as int?;
            var idx = keys.GetType().GetProperty("Item", Any);
            if (kc != null && idx != null)
                for (var i = 0; i < kc.Value; i++)
                    try { list.Add(idx.GetValue(keys, new object[] { i })); } catch { }
        }

        if (list.Count == 0)
        {
            yield return $"(no keys read; Count={Show(count)}, keys type={keys.GetType().Name})";
            yield break;
        }

        var itemProp = table.GetType().GetProperty("Item", Any);
        foreach (var k in list)
        {
            object val = null;
            try { val = itemProp?.GetValue(table, new object[] { k }); } catch { }
            yield return $"{Show(k),-28} = {Show(val)}";
        }
    }

    /// <summary>Loader smoke test: report whether the types we depend on actually resolve.</summary>
    public static void Snapshot()
    {
        Journal.Head("Interop");
        Journal.Line($"{AppDomain.CurrentDomain.GetAssemblies().Length} assemblies loaded");

        // Resolving a type is the check that matters — assembly-name matching was
        // misleading, because the game's assembly keeps its original unprefixed name
        // and is not loaded until something asks for a type in it.
        foreach (var probe in new[]
                 {
                     "VRFS.MatchAnalytics.MatchEventSink",
                     "VRFS.MatchAnalytics.MatchEvent",
                     "PlayerView",
                     "Photon.Pun.PhotonNetwork",
                     "Photon.Realtime.Player",
                     "UnityEngine.GUI",
                 })
        {
            var t = Find(probe);
            Journal.Line($"  {(t != null ? "ok  " : "MISS")} {probe}" +
                         (t != null ? $"   [{t.Assembly.GetName().Name}]" : ""));
        }
    }
}
