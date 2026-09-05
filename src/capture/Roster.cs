using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using StatsCam.Model;

namespace StatsCam.Capture;

/// <summary>
/// Keeps MatchState.Players in step with the Photon room.
///
/// Photon is a third-party assembly and therefore unobfuscated, which makes it the most
/// reliable identity source in the build: ActorNumber for in-session keying, UserId for
/// persistence, NickName for display.
/// </summary>
public sealed class Roster
{
    private const string NetType = "Photon.Pun.PhotonNetwork";

    /// <summary>
    /// Candidate CustomProperties keys for team. VRFS keeps the real key in
    /// PlayerPropertyNames behind an obfuscated field name, so the value cannot be
    /// recovered statically — Phase 0 dumps the live hashtable and the winner gets
    /// pinned here. Until then, try the plausible ones in order.
    /// </summary>
    public static string[] TeamKeys = { "team", "Team", "teamId", "t" };

    private readonly MatchState _state;
    private Type _net;
    private string _lastRoom = "";

    public Roster(MatchState state) => _state = state;

    public bool Ready => _net != null;
    public string RoomId { get; private set; } = "";

    public void Install()
    {
        _net = Reflect.Find(NetType) ?? Reflect.FindEndingWith(".PhotonNetwork");
        Journal.Line(_net != null
            ? $"roster: using {_net.FullName}"
            : "[warn] roster: PhotonNetwork not found — names will stay unresolved");
    }

    public bool InRoom() => _net != null && Reflect.Static(_net, "InRoom") is bool b && b;

    /// <summary>Returns true when the room changed, which the caller treats as a new match.</summary>
    public bool Sync()
    {
        if (_net == null) return false;

        var room = Reflect.Static(_net, "CurrentRoom");
        RoomId = room == null ? "" : Reflect.Member(room, "Name") as string ?? "";

        var changed = RoomId != _lastRoom;
        _lastRoom = RoomId;

        lock (_state.Gate)
        {
            foreach (var p in _state.Players.Values) p.Present = false;

            foreach (var player in Enumerate(Reflect.Static(_net, "PlayerList")))
            {
                var userId = Reflect.Member(player, "UserId") as string;
                var nick = Reflect.Member(player, "NickName") as string;
                var actor = Reflect.Member(player, "ActorNumber") as int? ?? -1;

                // UserId can be null before authentication completes; actor number is
                // always present, so it stands in as a stable within-room key.
                var key = !string.IsNullOrEmpty(userId) ? userId : $"actor:{actor}";

                var ps = _state.Ensure(key);
                ps.Present = true;
                ps.ActorNumber = actor;

                if (!string.IsNullOrWhiteSpace(nick) && ps.Name != nick) ps.Name = nick;

                var team = ReadTeam(player);
                if (team >= 0) ps.Team = team;
            }
        }

        return changed;
    }

    private int ReadTeam(object player)
    {
        var props = Reflect.Member(player, "CustomProperties");
        if (props == null) return -1;

        var indexer = props.GetType().GetProperty("Item",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (indexer == null) return -1;

        foreach (var key in TeamKeys)
        {
            object v = null;
            try { v = indexer.GetValue(props, new object[] { key }); } catch { }
            if (v == null) continue;

            switch (v)
            {
                case int i: return i;
                case byte b: return b;
                case short s: return s;
                case bool bo: return bo ? 1 : 0;
                case string str when int.TryParse(str, out var parsed): return parsed;
            }
        }
        return -1;
    }

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
