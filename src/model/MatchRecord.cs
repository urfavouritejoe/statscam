using System;
using System.IO;
using System.Linq;

namespace StatsCam.Model;

/// <summary>
/// Writes a finished match to disk as JSON. No server, no upload — a file appears in
/// Camera\StatsCam\matches\ when the match ends, and anything that wants the numbers
/// later reads it from there.
/// </summary>
public static class MatchRecord
{
    public static string Serialize(MatchState s)
    {
        var j = new Json();

        lock (s.Gate)
        {
            var a = s.Team(0);
            var b = s.Team(1);

            j.Obj()
                .P("matchId", s.MatchId.ToString())
                .P("startedUtc", s.StartedUtc.ToString("o"))
                .P("room", s.RoomId)
                .P("phase", s.Phase.ToString())
                .P("clock", s.ClockSeconds)
                .P("possession0", s.PossessionShare0())
                .P("eventCount", s.Log.Count);

            j.Key("score").Arr().Val(s.Score[0]).Val(s.Score[1]).EndArr();

            j.Key("teams").Arr();
            foreach (var t in new[] { a, b })
            {
                j.Obj()
                    .P("team", t.Team).P("goals", t.Goals)
                    .P("shots", t.Shots).P("onTarget", t.OnTarget)
                    .P("xg", t.Xg).P("passes", t.Passes)
                    .P("passAcc", t.PassAccuracy).P("saves", t.Saves)
                    .P("touches", t.Touches)
                 .EndObj();
            }
            j.EndArr();

            j.Key("players").Arr();
            foreach (var p in s.Ranked())
            {
                j.Obj()
                    .P("id", p.UserId).P("name", p.Label).P("team", p.Team)
                    .P("goals", p.Goals).P("ownGoals", p.OwnGoals).P("assists", p.Assists)
                    .P("shots", p.Shots).P("onTarget", p.ShotsOnTarget)
                    .P("touches", p.Touches).P("passes", p.Passes)
                    .P("passAcc", p.PassAccuracy)
                    .P("xg", p.Xg).P("xa", p.Xa).P("xt", p.Xt)
                    .P("saves", p.Saves).P("saveValue", p.SaveValue)
                    .P("possession", p.PossessionSeconds)
                    .P("rating", p.Rating)
                 .EndObj();
            }
            j.EndArr();

            j.Key("shots").Arr();
            foreach (var e in s.Shots)
            {
                var who = s.Find(e.ActorId);
                j.Obj()
                    .P("t", e.MatchTime).P("team", e.ActorTeam)
                    .P("name", who?.Label ?? "?")
                    .P("x", e.BallPosition.X).P("z", e.BallPosition.Z)
                    .P("xg", e.Value).P("onTarget", e.OnTarget)
                    .P("goal", e.Kind == EventKind.Goal)
                 .EndObj();
            }
            j.EndArr();

            j.Key("events").Arr();
            foreach (var e in s.Log)
            {
                var who = s.Find(e.ActorId);
                j.Obj()
                    .P("t", e.MatchTime).P("kind", e.Kind.ToString())
                    .P("actor", e.ActorId ?? "").P("actorName", who?.Label ?? "")
                    .P("team", e.ActorTeam)
                    .P("secondary", e.SecondaryId ?? "")
                    .P("value", e.Value).P("onTarget", e.OnTarget)
                 .EndObj();
            }
            j.EndArr();

            j.EndObj();
        }

        return j.ToString();
    }

    /// <summary>Returns the path written, or null when there was nothing worth saving.</summary>
    public static string Save(MatchState s, string reason)
    {
        try
        {
            int events;
            lock (s.Gate) events = s.Log.Count;
            if (events == 0) return null;

            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StatsCam", "matches");
            Directory.CreateDirectory(dir);

            var name = $"{s.StartedUtc:yyyyMMdd-HHmmss}-{s.MatchId.ToString()[..8]}.json";
            var file = Path.Combine(dir, name);
            File.WriteAllText(file, Serialize(s));

            Journal.Line($"match saved ({reason}): {file}");
            return file;
        }
        catch (Exception e)
        {
            Journal.Warn($"could not save match: {e.Message}");
            return null;
        }
    }
}
