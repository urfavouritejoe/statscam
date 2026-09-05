using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace StatsCam;

/// <summary>
/// Reads the Photon layer, which is a third-party assembly and therefore not
/// obfuscated — the most reliable identity source in the build.
///
/// Its second job is to dump CustomProperties. VRFS keeps 25 player keys and 64 room
/// keys in PlayerPropertyNames / RoomPropertyNames, but those are static readonly
/// strings behind obfuscated field names, so the key-to-meaning mapping cannot be
/// recovered from metadata. One live dump settles it.
/// </summary>
internal static class PhotonProbe
{
    private const string NetType = "Photon.Pun.PhotonNetwork";

    private static Type _net;
    private static string _lastSignature = "";
    private static bool _propsDumped;

    public static bool Ready => _net != null;

    public static void Install()
    {
        Journal.Head("Photon layer");

        _net = Reflect.Find(NetType) ?? Reflect.FindEndingWith(".PhotonNetwork");
        if (_net == null)
        {
            Journal.Fail("PhotonNetwork not found — cannot read the roster.");
            return;
        }

        Journal.Line($"found  {_net.FullName}  [{_net.Assembly.GetName().Name}]");

        var player = Reflect.Find("Photon.Realtime.Player");
        Journal.Line(player != null
            ? $"found  {player.FullName}  [{player.Assembly.GetName().Name}]"
            : "[warn] Photon.Realtime.Player not found");
    }

    public static bool InRoom()
    {
        if (_net == null) return false;
        return Reflect.Static(_net, "InRoom") is bool b && b;
    }

    /// <summary>Dump the roster when it changes, or unconditionally on demand.</summary>
    public static void Poll(bool force = false)
    {
        if (_net == null) return;

        var players = Enumerate(Reflect.Static(_net, "PlayerList")).ToList();
        var room = Reflect.Static(_net, "CurrentRoom");

        var sig = string.Join("|", players.Select(p => Reflect.Show(Reflect.Member(p, "ActorNumber"))));
        if (!force && sig == _lastSignature) return;
        _lastSignature = sig;

        Journal.Head($"Roster ({players.Count} in room)");

        if (room != null)
        {
            Journal.Line($"room   name={Reflect.Show(Reflect.Member(room, "Name"))}  " +
                         $"players={Reflect.Show(Reflect.Member(room, "PlayerCount"))}  " +
                         $"max={Reflect.Show(Reflect.Member(room, "MaxPlayers"))}");
        }

        foreach (var p in players)
        {
            Journal.Line(
                $"  actor={Reflect.Show(Reflect.Member(p, "ActorNumber")),-4} " +
                $"nick={Reflect.Show(Reflect.Member(p, "NickName")),-22} " +
                $"userId={Reflect.Show(Reflect.Member(p, "UserId")),-40} " +
                $"local={Reflect.Show(Reflect.Member(p, "IsLocal"))} " +
                $"master={Reflect.Show(Reflect.Member(p, "IsMasterClient"))}");
        }

        // The property key dump only needs to happen once per session.
        if (!_propsDumped && players.Count > 0)
        {
            _propsDumped = true;
            DumpProperties(players, room);
        }
    }

    private static void DumpProperties(List<object> players, object room)
    {
        Journal.Head("CustomProperties — resolves the obfuscated key tables");

        foreach (var p in players)
        {
            var nick = Reflect.Show(Reflect.Member(p, "NickName"));
            var props = Reflect.Member(p, "CustomProperties");
            Journal.Line($"player {nick}:");

            var any = false;
            foreach (var pair in Reflect.Pairs(props)) { Journal.Line("    " + pair); any = true; }
            if (!any) Journal.Line("    (empty)");
        }

        if (room != null)
        {
            Journal.Line("room:");
            var any = false;
            foreach (var pair in Reflect.Pairs(Reflect.Member(room, "CustomProperties")))
            {
                Journal.Line("    " + pair);
                any = true;
            }
            if (!any) Journal.Line("    (empty)");
        }
    }

    /// <summary>Walk an il2cpp array or managed enumerable without knowing its type.</summary>
    private static IEnumerable<object> Enumerate(object seq)
    {
        if (seq == null) yield break;

        if (seq is IEnumerable managed && seq is not string)
        {
            foreach (var o in managed) yield return o;
            yield break;
        }

        var count = Reflect.Member(seq, "Length") as int? ?? Reflect.Member(seq, "Count") as int?;
        if (count == null) yield break;

        var idx = seq.GetType().GetProperty("Item",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (idx == null) yield break;

        for (var i = 0; i < count.Value; i++)
        {
            object v = null;
            try { v = idx.GetValue(seq, new object[] { i }); } catch { }
            if (v != null) yield return v;
        }
    }
}
