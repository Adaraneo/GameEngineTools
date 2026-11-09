// Services/Aggregation.cs — cached data shapes and the aggregation helpers shared by the
// accumulator and the cold-path endpoints (port of the helper functions in server.js).
using System.Text.Json.Nodes;
using LogReader.Parsing;

namespace LogReader.Services;

// ── Cached / served data shapes (property names are the JSON contract with the browser) ──

public sealed class SummaryData
{
    public long TotalEvents { get; set; }
    public Dictionary<string, long> BySubsystem { get; set; } = new();
    public Dictionary<string, long> ByEventId { get; set; } = new();
    public Dictionary<string, long> ByLevel { get; set; } = new();
    public string? From { get; set; }
    public string? To { get; set; }
}

public sealed class FreqData
{
    public List<string> Buckets { get; set; } = new();
    public List<string> Subsystems { get; set; } = new();
    public Dictionary<string, List<long>> Counts { get; set; } = new();
}

public sealed class PhysioData
{
    public List<JsonObject> Snapshots { get; set; } = new();
    public List<JsonObject> SleepEvents { get; set; } = new();
    public List<JsonObject> Cycle { get; set; } = new();
    public List<JsonObject> Actions { get; set; } = new();
    public List<JsonObject> ReproEvents { get; set; } = new();
    public List<JsonObject> ObjectUsed { get; set; } = new();
    public List<JsonObject> ObjectRespawned { get; set; } = new();
    public List<JsonObject> Hormones { get; set; } = new();
}

public sealed class PsychData
{
    public List<JsonObject> Snapshots { get; set; } = new();
    public List<JsonObject> PsychEvents { get; set; } = new();
    public List<JsonObject> SelfConcept { get; set; } = new();
    public List<JsonObject> Interests { get; set; } = new();
    public List<JsonObject> DarkCoreSnapshots { get; set; } = new();
    public List<JsonObject> NeedAppraisal { get; set; } = new();
    public List<JsonObject> ValuesSnapshots { get; set; } = new();
}

public sealed class BehaviorData
{
    public List<JsonObject> Snapshots { get; set; } = new();
    public List<JsonObject> Actions { get; set; } = new();
    public List<JsonObject> Habits { get; set; } = new();
    public List<JsonObject> Decisions { get; set; } = new();
    public List<JsonObject> Plans { get; set; } = new();
    public List<JsonObject> Candidates { get; set; } = new();
    public List<JsonObject> Running { get; set; } = new();
    public List<JsonObject> Cooldowns { get; set; } = new();
    public List<JsonObject> ValuesEvents { get; set; } = new();
    public List<JsonObject> DarkCoreEvents { get; set; } = new();
    public List<JsonObject> TravelEvents { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<JsonObject>? Intents { get; set; }
}

public sealed class MemoryData
{
    public List<JsonObject> Encoded { get; set; } = new();
    public List<JsonObject> Consolidated { get; set; } = new();
    public List<JsonObject> Reconsolidated { get; set; } = new();
    public List<JsonObject> BeliefUpdates { get; set; } = new();
}

public sealed class ActiveGoals
{
    public List<JsonObject> Active { get; set; } = new();
    public List<JsonObject> Resolved { get; set; } = new();
}

public sealed class GoalData
{
    public List<JsonObject> Events { get; set; } = new();
    public JsonObject Timelines { get; set; } = new();
    public ActiveGoals ActiveGoals { get; set; } = new();
}

public sealed class ScheduleData
{
    public List<JsonObject> Events { get; set; } = new();
}

public sealed class LocationData
{
    public List<JsonObject> Sessions { get; set; } = new();
    public JsonObject ByLocation { get; set; } = new();
    public List<JsonObject> ContextEvents { get; set; } = new();
    public List<JsonObject> ObjectInteractions { get; set; } = new();
}

public sealed class RelData
{
    public List<string> Partners { get; set; } = new();
    public Dictionary<string, List<JsonObject>> Timeline { get; set; } = new();
    public Dictionary<string, List<JsonObject>> RelEvents { get; set; } = new();
}

public sealed class InteractionData
{
    public List<JsonObject> Interactions { get; set; } = new();
    public Dictionary<string, List<JsonObject>> ByPartner { get; set; } = new();
    public JsonObject Stats { get; set; } = new();
    public List<JsonObject> IntentEvents { get; set; } = new();
    public List<JsonObject> Utterances { get; set; } = new();
}

public sealed record TickSnapshots(JsonObject? Physio, JsonObject? Psych, JsonObject? Behavior);

public sealed record TickEvent(string WorldTime, string Subsystem, int? EventId, string? Icon, string? Level, string Label, JsonObject Detail);

public sealed record Tick(string WorldTime, TickSnapshots Snapshots, List<TickEvent> Events);

public sealed class TickData
{
    public List<Tick> Ticks { get; set; } = new();
    public int Count { get; set; }
    public int TotalTicks { get; set; }
    public bool Truncated { get; set; }
}

// ── EventId sets shared between the accumulator and the cold-path endpoints ──

public static class EventIds
{
    public static readonly HashSet<int> Goal = new() { 1300, 1301, 1302, 1303, 1304 };
    public static readonly HashSet<int> Schedule = new() { 1400, 1401, 1402, 1403, 1404 };
    public static readonly HashSet<int> Location = new() { 1200, 1205, 1206 };
    public static readonly HashSet<int> Physio = new() { 5000, 5001, 5002, 5003, 5004, 5005, 5006, 5007, 5600, 1100, 1101, 1102, 1103, 1104, 1105, 1106, 1107, 1108, 1109, 1110, 1111, 1112, 1113, 1003 };
    public static readonly HashSet<int> Psych = new() { 5100, 5101, 5102 };
    public static readonly HashSet<int> Behav = new() { 5200, 1000, 1001, 1002, 1003, 1004, 1010, 1011, 1012, 1013, 1014, 1015, 1016, 5201 };
    public static readonly HashSet<int> Sched = new() { 4100, 4101, 4102, 4103 };
    public static readonly HashSet<int> PsychNew = new() { 5103, 5104, 5105 };
    public static readonly HashSet<int> MemNew = new() { 3002, 3003 };
    public static readonly HashSet<int> RelNew = new() { 2008, 2009, 2010, 2011, 2012, 2013, 2014 };
    public static readonly HashSet<int> Interact = new() { 1200, 1201, 1202, 1203, 1204, 1207, 1208, 1209 };
    public static readonly HashSet<int> Values = new() { 1210, 1211, 1212, 1213 };
    public static readonly HashSet<int> Obj = new() { 1500, 1501 };
    public static readonly HashSet<int> Scene = new() { 10001, 10002, 10003 };
    public static readonly HashSet<int> Self = new() { 5300 };
    public static readonly HashSet<int> Life = new() { 1600, 1601 };
    public static readonly HashSet<int> Interest = new() { 5400 };
    public static readonly HashSet<int> MutKnow = new() { 1610 };
    public static readonly HashSet<int> SocialCmp = new() { 2016 };
    public static readonly HashSet<int> RepPrior = new() { 2017 };
    public static readonly HashSet<int> DarkCoreSnap = new() { 5500 };
    public static readonly HashSet<int> DarkCoreMod = new() { 1017 };
    public static readonly HashSet<int> Hormone = new() { 5600 };
    public static readonly HashSet<int> Travel = new() { 1700, 1701 };
    public static readonly HashSet<int> NeedAppraisal = new() { 5210 };
    public static readonly HashSet<int> ValuesSnap = new() { 5700 };
    public static readonly HashSet<int> Dialogue = new() { 1220 };

    /// <summary>Snapshot events live in the global Characters.jsonl and are routed by PersonId.</summary>
    public static readonly HashSet<int> Snapshot = new()
    {
        5000, 5001, 5002, 5003, 5004, 5005, 5006, 5100, 5101, 5102, 5103, 5104, 5105, 5200, 5201, 5210, 5300, 5400, 5500, 5600, 5700,
    };

    public static IEnumerable<(string Label, HashSet<int> Set)> All() => new (string, HashSet<int>)[]
    {
        ("Goal", Goal), ("Schedule", Schedule), ("Location", Location), ("Physio", Physio), ("Psych", Psych), ("Behav", Behav),
        ("Sched", Sched), ("PsychNew", PsychNew), ("MemNew", MemNew), ("RelNew", RelNew), ("Interact", Interact), ("Values", Values),
        ("Obj", Obj), ("Scene", Scene), ("Self", Self), ("Life", Life), ("Interest", Interest), ("MutKnow", MutKnow),
        ("SocialCmp", SocialCmp), ("RepPrior", RepPrior), ("DarkCoreSnap", DarkCoreSnap), ("DarkCoreMod", DarkCoreMod),
        ("Hormone", Hormone), ("Travel", Travel), ("NeedAppraisal", NeedAppraisal), ("ValuesSnap", ValuesSnap), ("Dialogue", Dialogue),
    };
}

public static class Agg
{
    public const int MaxChart = 10_000;
    public const int MaxEvents = 10_000;

    /// <summary>Memory-bounded append for time series: thins uniformly when the list doubles the cap.</summary>
    public static void CapPush(List<JsonObject> list, JsonObject item, int max = MaxChart)
    {
        list.Add(item);
        if (list.Count > max * 2)
        {
            var j = 0;
            for (var i = 0; i < list.Count; i += 2) list[j++] = list[i];
            list.RemoveRange(j, list.Count - j);
        }
    }

    /// <summary>Memory-bounded append that keeps the most recent entries.</summary>
    public static void CapPushRecent(List<JsonObject> list, JsonObject item, int max = MaxEvents)
    {
        list.Add(item);
        if (list.Count > max * 1.25) list.RemoveRange(0, (int)Math.Floor(list.Count * 0.25));
    }

    public static bool IsRelLogEntry(int eid) =>
        (eid >= 2001 && eid <= 2014) || eid is 2016 or 2017 or 1204 or 1207 or 1208 or 1209 or 10001 or 10002 or 10003;

    /// <summary>
    /// Canonical classifier for the relationship event log: the single parsed record an entry contributes, or null.
    /// Partnerless diagnostics are kept; partner-bearing events are dropped when the partner is missing or the character itself.
    /// </summary>
    public static JsonObject? RelLogRecordFor(LogEntry e, string id)
    {
        var eid = e.EventId ?? -1;
        if (eid is 1207 or 1208 or 1209) return InteractionParsers.ParseNormViolation(e);
        if (eid is 10001 or 10002) return InteractionParsers.ParseSceneMoveFailed(e);

        JsonObject? p;
        if (eid == 2005) return null;
        else if (eid >= 2001 && eid <= 2007) p = RelationshipParsers.ParseRelEventForLog(e);
        else if (eid == 2008) p = RelationshipParsers.ParseSexualEncounterOutcome(e);
        else if (eid == 2009) p = RelationshipParsers.ParseDecayDetail(e);
        else if (eid == 2010) p = RelationshipParsers.ParseMilestone(e);
        else if (eid == 2011) p = RelationshipParsers.ParseThirdPartyReputation(e);
        else if (eid == 2012) p = RelationshipParsers.ParseJealousy(e);
        else if (eid == 2013) p = RelationshipParsers.ParseOneWayObservation(e);
        else if (eid == 2014) p = RelationshipParsers.ParseInvestment(e);
        else if (eid == 2016) p = RelationshipParsers.ParseSocialComparison(e);
        else if (eid == 2017) p = RelationshipParsers.ParseReputationPrior(e);
        else if (eid == 1204) p = InteractionParsers.ParseMisattribution(e);
        else if (eid == 10003) p = InteractionParsers.ParseReachOutRouted(e);
        else return null;
        var partner = p?.S("partnerId");
        return p != null && !string.IsNullOrEmpty(partner) && partner != id ? p : null;
    }

    /// <summary>Sorts parsed relationship events, merges each 2001 event with its 2002 apply, groups by partner.</summary>
    public static Dictionary<string, List<JsonObject>> GroupRelEvents(IEnumerable<JsonObject> relLogEvts)
    {
        var parsed = relLogEvts.OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();
        var used = new HashSet<int>();
        var merged = new List<JsonObject>();
        for (var i = 0; i < parsed.Count; i++)
        {
            if (used.Contains(i)) continue;
            var ev = parsed[i];
            var iid = ev.S("interactionId");
            if (ev.S("kind") == "event" && !string.IsNullOrEmpty(iid))
            {
                var applyIdx = -1;
                for (var j = i + 1; j < parsed.Count; j++)
                    if (!used.Contains(j) && parsed[j].S("kind") == "apply" && parsed[j].S("interactionId") == iid) { applyIdx = j; break; }
                if (applyIdx != -1)
                {
                    used.Add(applyIdx);
                    var m = ev.Clone();
                    m["changes"] = parsed[applyIdx].Raw("changes");
                    merged.Add(m);
                    continue;
                }
            }
            merged.Add(ev);
        }
        var map = new Dictionary<string, List<JsonObject>>();
        foreach (var ev in merged)
        {
            var pid = ev.S("partnerId") ?? "undefined";
            if (!map.TryGetValue(pid, out var l)) { l = new List<JsonObject>(); map[pid] = l; }
            l.Add(ev);
        }
        return map;
    }

    public static RelData BuildRelData(IEnumerable<JsonObject> edges, IEnumerable<JsonObject> applies, IEnumerable<JsonObject> relLogEvts)
    {
        var timeline = RelationshipParsers.BuildTimeline(edges, applies);
        var events = GroupRelEvents(relLogEvts);
        var partners = timeline.Keys.Concat(events.Keys).Distinct().ToList();
        return new RelData { Partners = partners, Timeline = timeline, RelEvents = events };
    }

    /// <summary>Derives the currently active / resolved goals from the goal event stream.</summary>
    public static ActiveGoals BuildActiveGoals(IEnumerable<JsonObject> goalEvts)
    {
        var byKind = new Dictionary<string, JsonObject>();
        foreach (var ev in goalEvts)
        {
            var kind = ev.S("kind");
            if (string.IsNullOrEmpty(kind)) continue;
            if (!byKind.TryGetValue(kind, out var g))
            {
                g = new JsonObject { ["kind"] = kind, ["icon"] = ev.Raw("icon"), ["label"] = ev.Raw("label"), ["category"] = ev.Raw("category") };
                byKind[kind] = g;
            }
            switch (ev.S("type"))
            {
                case "activated" or "seeded":
                    g["salience"] = ev.Raw("salience"); g["progress"] = 0; g["frustration"] = null;
                    g["origin"] = ev.Raw("origin") ?? "Personality"; g["activatedAt"] = ev.Raw("worldTime"); g["resolution"] = null;
                    break;
                case "progressed":
                    g["salience"] = ev.Raw("salienceNew"); g["progress"] = ev.Raw("progressNew"); g["lastProgressAt"] = ev.Raw("worldTime");
                    break;
                case "resolved":
                    g["resolution"] = ev.Raw("resolution"); g["resolutionCls"] = ev.Raw("resolutionCls"); g["resolutionIcon"] = ev.Raw("resolutionIcon");
                    g["progress"] = ev.Raw("progress"); g["frustration"] = ev.Raw("frustration"); g["resolvedAt"] = ev.Raw("worldTime");
                    break;
            }
        }
        bool IsResolved(JsonObject g) => g.Get("resolution") != null && !(g.Get("resolution") is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 0);
        return new ActiveGoals
        {
            Active = byKind.Values.Where(g => !IsResolved(g)).OrderByDescending(g => g.D("salience") ?? 0).ToList(),
            Resolved = byKind.Values.Where(IsResolved).OrderByDescending(g => g.S("resolvedAt") ?? "", StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>Links 1201/1202/1203 decisions with 2001/2002 relationship events into interaction records.</summary>
    public static InteractionData BuildInteractionData(
        List<JsonObject> int1201, List<JsonObject> int1202, List<JsonObject> int1203, List<JsonObject> contexts, List<JsonObject> relLogEvts, string selfId)
    {
        var ctxSorted = contexts.OrderBy(c => c.S("worldTime") ?? "", StringComparer.Ordinal).ToList();

        JsonObject? CtxAt(string? worldTime)
        {
            JsonObject? result = null;
            foreach (var c in ctxSorted)
            {
                if (Js.Cmp(c.S("worldTime"), worldTime) <= 0) result = c; else break;
            }
            return result == null ? null : new JsonObject { ["location"] = result.Raw("location"), ["noise"] = result.Raw("noise"), ["crowding"] = result.Raw("crowding") };
        }

        var index = new Dictionary<string, JsonObject>();
        var order = new List<JsonObject>();
        JsonObject GetOrCreate(string? wt, string partner)
        {
            var key = wt + "|" + partner;
            if (!index.TryGetValue(key, out var rec))
            {
                rec = new JsonObject
                {
                    ["worldTime"] = wt, ["partnerId"] = partner, ["type"] = "verbal", ["speechAct"] = null, ["role"] = null,
                    ["probability"] = null, ["outcome"] = null, ["touchLevel"] = null, ["context"] = CtxAt(wt), ["changes"] = null,
                };
                index[key] = rec; order.Add(rec);
            }
            return rec;
        }

        foreach (var ev in int1201)
        {
            var pid = ev.S("partnerId");
            if (string.IsNullOrEmpty(pid)) continue;
            var rec = GetOrCreate(ev.S("worldTime"), pid);
            rec["probability"] = ev.Raw("probability"); rec["outcome"] = ev.Raw("outcome"); rec["type"] = "verbal";
        }
        foreach (var ev in int1202)
        {
            var pid = ev.S("partnerId");
            if (string.IsNullOrEmpty(pid)) continue;
            var rec = GetOrCreate(ev.S("worldTime"), pid);
            rec["probability"] = ev.Raw("probability"); rec["outcome"] = ev.Raw("outcome"); rec["type"] = "touch"; rec["touchLevel"] = ev.Raw("touchLevel");
        }
        foreach (var ev in int1203)
        {
            var pid = ev.S("partnerId");
            if (string.IsNullOrEmpty(pid)) continue;
            GetOrCreate(ev.S("worldTime"), pid)["type"] = "sexual";
        }

        var applyById = new Dictionary<string, JsonObject>();
        foreach (var ev in relLogEvts)
        {
            var iid = ev.S("interactionId");
            if (ev.S("kind") == "apply" && !string.IsNullOrEmpty(iid) && ev.S("partnerId") != selfId) applyById.TryAdd(iid, ev);
        }

        foreach (var ev in relLogEvts)
        {
            var pid = ev.S("partnerId");
            if (string.IsNullOrEmpty(pid) || pid == selfId) continue;
            var key = ev.S("worldTime") + "|" + pid;
            var kind = ev.S("kind");
            if (kind == "event")
            {
                var rec = GetOrCreate(ev.S("worldTime"), pid);
                rec["speechAct"] ??= ev.Raw("act");
                rec["role"] ??= ev.Raw("role");
                rec["outcome"] ??= P.NormalizeOutcome(ev.S("outcome"));
                var iid = ev.S("interactionId");
                if (!string.IsNullOrEmpty(iid) && rec.Get("changes") == null && applyById.TryGetValue(iid, out var apply))
                    rec["changes"] = apply.Raw("changes");
            }
            else if (kind == "apply")
            {
                if (index.TryGetValue(key, out var rec)) rec["changes"] ??= ev.Raw("changes");
                else GetOrCreate(ev.S("worldTime"), pid)["changes"] = ev.Raw("changes");
            }
        }

        var interactions = order.OrderBy(r => r.S("worldTime") ?? "", StringComparer.Ordinal).ToList();

        var bySpeechAct = new JsonObject();
        long accepted = 0;
        foreach (var rec in interactions)
        {
            var isAccepted = rec.S("outcome") == "accepted";
            if (isAccepted) accepted++;
            var act = rec.S("speechAct") ?? (rec.S("type") == "touch" ? $"Touch:{rec.S("touchLevel") ?? ""}" : rec.S("type"));
            if (string.IsNullOrEmpty(act)) continue;
            if (bySpeechAct[act] is not JsonObject b) { b = new JsonObject { ["total"] = 0, ["accepted"] = 0 }; bySpeechAct[act] = b; }
            b["total"] = b.D("total") + 1;
            if (isAccepted) b["accepted"] = b.D("accepted") + 1;
        }

        var byPartner = new Dictionary<string, List<JsonObject>>();
        foreach (var rec in interactions)
        {
            var pid = rec.S("partnerId")!;
            if (!byPartner.TryGetValue(pid, out var l)) { l = new List<JsonObject>(); byPartner[pid] = l; }
            l.Add(rec);
        }

        return new InteractionData
        {
            Interactions = interactions,
            ByPartner = byPartner,
            Stats = new JsonObject { ["total"] = interactions.Count, ["accepted"] = accepted, ["bySpeechAct"] = bySpeechAct },
        };
    }

    public static FreqData BuildFreq(Dictionary<string, Dictionary<string, long>> m)
    {
        var buckets = m.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var subsystems = m.Values.SelectMany(s => s.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        var counts = new Dictionary<string, List<long>>();
        foreach (var ss in subsystems)
            counts[ss] = buckets.Select(b => m[b].TryGetValue(ss, out var c) ? c : 0).ToList();
        return new FreqData { Buckets = buckets, Subsystems = subsystems, Counts = counts };
    }

    /// <summary>Event frequency from compact events (keys wt / ss), bucketed by hour or day.</summary>
    public static FreqData BuildEventFrequency(IEnumerable<JsonObject> compact, string bucket)
    {
        var len = bucket == "hour" ? 13 : 10;
        var m = new Dictionary<string, Dictionary<string, long>>();
        foreach (var e in compact)
        {
            var wt = e.S("wt");
            if (string.IsNullOrEmpty(wt)) continue;
            var key = wt.Length > len ? wt[..len] : wt;
            if (!m.TryGetValue(key, out var sub)) { sub = new Dictionary<string, long>(); m[key] = sub; }
            var ss = e.S("ss") ?? "Unknown";
            sub[ss] = sub.GetValueOrDefault(ss) + 1;
        }
        return BuildFreq(m);
    }

    public static SummaryData BuildSummaryFromCompact(IReadOnlyCollection<JsonObject> entries)
    {
        var s = new SummaryData { TotalEvents = entries.Count };
        foreach (var e in entries)
        {
            if (e.S("ss") is { Length: > 0 } ss) s.BySubsystem[ss] = s.BySubsystem.GetValueOrDefault(ss) + 1;
            if (e.S("lv") is { Length: > 0 } lv) s.ByLevel[lv] = s.ByLevel.GetValueOrDefault(lv) + 1;
            if (e.Get("ev") != null) { var k = e.S("ev")!; s.ByEventId[k] = s.ByEventId.GetValueOrDefault(k) + 1; }
            if (e.S("rt") is { Length: > 0 } rt)
            {
                if (s.From == null || Js.Cmp(rt, s.From) < 0) s.From = rt;
                if (s.To == null || Js.Cmp(rt, s.To) > 0) s.To = rt;
            }
        }
        return s;
    }
}
