// Parsing/ValuesGoalsMemoryParsers.cs — values, goal/intent and memory parsers
// (port of parsers/values.js, goals.js, memory.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class ValuesParsers
{
    private static readonly Regex ProfileRe = Js.Re(@"benevolence=([\d.,]+),\s*universalism=([\d.,]+),\s*achievement=([\d.,]+),\s*power=([\d.,]+)");
    private static readonly Regex CongRe = Js.Re(@"Congruence violation:\s*action=(\S+),\s*congruence=([\d.,]+),\s*utilityDelta=([+\-]?[\d.,]+)");
    private static readonly Regex GuiltRe = Js.Re(@"Guilt spike:\s*action=(\S+),\s*violatedValue=(\w+),\s*congruence=([\d.,]+),\s*deltaValence=([+\-]?[\d.,]+)");
    private static readonly Regex DriftRe = Js.Re(@"value drift:\s*eroded=(\w+),\s*strengthened=(\w+),\s*delta=([+\-]?[\d.,]+)");

    public static JsonObject? ParseProfile(LogEntry e)
    {
        if (e.EventId != 1210) return null;
        double? b, u, a, pw;
        if (e.Payload is { } p)
        {
            b = Js.Num(p.Get("Benevolence")); u = Js.Num(p.Get("Universalism"));
            a = Js.Num(p.Get("Achievement")); pw = Js.Num(p.Get("Power"));
        }
        else
        {
            var m = Js.Match(e.Message, ProfileRe);
            if (m == null) return null;
            b = m.D(1); u = m.D(2); a = m.D(3); pw = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 1210, ["worldTime"] = e.WorldTimeText, ["benevolence"] = b, ["universalism"] = u,
            ["achievement"] = a, ["power"] = pw, ["icon"] = "🧭", ["label"] = "Values profile", ["level"] = "info",
        };
    }

    public static JsonObject? ParseCongruenceViolation(LogEntry e)
    {
        if (e.EventId != 1211) return null;
        string? action; double? cong, delta;
        if (e.Payload is { } p) { action = p.S("ActionName"); cong = Js.Num(p.Get("Congruence")); delta = Js.Num(p.Get("UtilityDelta")); }
        else
        {
            var m = Js.Match(e.Message, CongRe);
            if (m == null) return null;
            action = m[1]; cong = m.D(2); delta = m.D(3);
        }
        return new JsonObject
        {
            ["eventId"] = 1211, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["congruence"] = cong, ["utilityDelta"] = delta,
            ["icon"] = "⚖️", ["label"] = "Values conflict: " + action, ["level"] = delta < -0.1 ? "warning" : "secondary",
        };
    }

    public static JsonObject? ParseGuiltSpike(LogEntry e)
    {
        if (e.EventId != 1212) return null;
        string? action, violated; double? cong, dv;
        if (e.Payload is { } p)
        {
            action = p.S("ActionName"); violated = p.S("ViolatedValue"); cong = Js.Num(p.Get("Congruence")); dv = Js.Num(p.Get("DeltaValence"));
        }
        else
        {
            var m = Js.Match(e.Message, GuiltRe);
            if (m == null) return null;
            action = m[1]; violated = m[2]; cong = m.D(3); dv = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 1212, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["violatedValue"] = violated,
            ["congruence"] = cong, ["deltaValence"] = dv,
            ["icon"] = "😔", ["label"] = "Guilt: " + violated + " violated by " + action, ["level"] = dv < -0.2 ? "danger" : "warning",
        };
    }

    public static JsonObject? ParseDrifted(LogEntry e)
    {
        if (e.EventId != 1213) return null;
        string? eroded, strengthened; double? delta;
        if (e.Payload is { } p) { eroded = p.S("ErodedValue"); strengthened = p.S("StrengthenedValue"); delta = Js.Num(p.Get("Delta")); }
        else
        {
            var m = Js.Match(e.Message, DriftRe);
            if (m == null) return null;
            eroded = m[1]; strengthened = m[2]; delta = m.D(3);
        }
        return new JsonObject
        {
            ["eventId"] = 1213, ["worldTime"] = e.WorldTimeText, ["eroded"] = eroded, ["strengthened"] = strengthened, ["delta"] = delta,
            ["icon"] = "🔀", ["label"] = $"Value drift: {strengthened} ↑ / {eroded} ↓", ["level"] = "secondary",
        };
    }
}

public static class GoalParsers
{
    private static readonly Regex ActivatedRe = Js.Re(@"Goal activated:\s*(\w+)\s*\(origin=(\w+),\s*salience=([\d.,]+)\)");
    private static readonly Regex ProgressedRe = Js.Re($@"(\w+)\s+salience\s+([\d.,]+){P.Arr}([\d.,]+)\s+progress\s+([\d.,]+){P.Arr}([\d.,]+)");
    private static readonly Regex ResolvedRe = Js.Re(@"Goal resolved:\s*(\w+)\s*[→>]\s*(\w+)\s*\(progress=([\d.,]+),\s*frustration=([\d.,]+)\)");
    private static readonly Regex BiasRe = Js.Re(@"Bias applied to\s+(\S+):\s*\+([\d.,]+)\s+from goal\s+(\w+)\s*\(salience=([\d.,]+)\)");
    private static readonly Regex SeededRe = Js.Re(@"Seeded\s+(\w+)\s+from personality\s*\(salience=([\d.,]+)\)");
    private static readonly Regex IntentRe = Js.Re(@"intent\s+\w+\s+(\w+)(?:\s*->\s*(\S+)\s*\(score=([\d.,]+)\))?");
    private static readonly Regex IntentEmrgRe = Js.Re(@"emergency\s+override\s+(\w+)\s*->\s*(\S+)\s*\(score=([\d.,]+)\)");
    private static readonly Regex IntentBiasRe = Js.Re(@"bias applied to (\S+):\s*\+?([\d.,]+)");

    private static readonly Dictionary<string, (string Icon, string Label, string Category)> KindIcons = new()
    {
        ["FindMeaning"] = ("🔍", "Find Meaning", "existential"), ["OvercomeTrauma"] = ("💪", "Overcome Trauma", "existential"),
        ["BuildIdentity"] = ("🪞", "Build Identity", "existential"), ["ProtectFamily"] = ("🛡️", "Protect Family", "survival"),
        ["EscapeDanger"] = ("🚨", "Escape Danger", "survival"), ["MasterCraft"] = ("⚒️", "Master Craft", "career"),
        ["BuildReputation"] = ("⭐", "Build Reputation", "career"), ["FindPartner"] = ("💕", "Find Partner", "relational"),
        ["RepairRelationship"] = ("🤝", "Repair Relationship", "relational"), ["SeekRevenge"] = ("⚔️", "Seek Revenge", "relational"),
    };

    private static readonly Dictionary<string, (string Cls, string Icon)> Resolutions = new()
    {
        ["Completed"] = ("success", "✅"), ["Abandoned"] = ("secondary", "🚫"), ["Faded"] = ("warning", "🌫️"), ["Displaced"] = ("info", "🔀"),
    };

    private static JsonObject WithKind(JsonObject o, string? kind)
    {
        var (icon, label, cat) = kind != null && KindIcons.TryGetValue(kind, out var k) ? k : ("🎯", kind ?? "", "other");
        o["icon"] = icon; o["label"] = label; o["category"] = cat;
        return o;
    }

    public static JsonObject? ParseGoalEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || eid < 1300 || eid > 1304) return null;
        var msg = e.Message ?? "";
        var wt = e.WorldTimeText;
        var p = e.Payload;
        JsonObject Base(string type) => new() { ["eventId"] = eid, ["worldTime"] = wt, ["type"] = type };

        switch (eid)
        {
            case 1300:
                if (p != null)
                    return WithKind(Base("activated").Put("kind", p.S("Kind")).Put("origin", p.S("Origin")).Put("salience", Js.Num(p.Get("Salience"))), p.S("Kind"));
                if (Js.Match(msg, ActivatedRe) is { } m1)
                    return WithKind(Base("activated").Put("kind", m1[1]).Put("origin", m1[2]).Put("salience", m1.D(3)), m1[1]);
                return null;
            case 1301:
                if (p != null)
                    return WithKind(Base("progressed").Put("kind", p.S("Kind")).Put("salienceOld", Js.Num(p.Get("OldSalience")))
                        .Put("salienceNew", Js.Num(p.Get("NewSalience"))).Put("progressOld", Js.Num(p.Get("OldProgress")))
                        .Put("progressNew", Js.Num(p.Get("NewProgress"))), p.S("Kind"));
                if (Js.Match(msg, ProgressedRe) is { } m2)
                    return WithKind(Base("progressed").Put("kind", m2[1]).Put("salienceOld", m2.D(2)).Put("salienceNew", m2.D(3))
                        .Put("progressOld", m2.D(4)).Put("progressNew", m2.D(5)), m2[1]);
                return null;
            case 1302:
                if (p != null)
                {
                    var resName = p.S("Resolution");
                    var res = resName != null && Resolutions.TryGetValue(resName, out var r) ? r : ("secondary", "?");
                    return WithKind(Base("resolved").Put("kind", p.S("Kind")).Put("resolution", resName)
                        .Put("progress", Js.Num(p.Get("Progress"))).Put("frustration", Js.Num(p.Get("Frustration")))
                        .Put("resolutionCls", res.Item1).Put("resolutionIcon", res.Item2), p.S("Kind"));
                }
                if (Js.Match(msg, ResolvedRe) is { } m3)
                {
                    var res = Resolutions.TryGetValue(m3[2]!, out var r) ? r : ("secondary", "?");
                    return WithKind(Base("resolved").Put("kind", m3[1]).Put("resolution", m3[2]).Put("progress", m3.D(3))
                        .Put("frustration", m3.D(4)).Put("resolutionCls", res.Item1).Put("resolutionIcon", res.Item2), m3[1]);
                }
                return null;
            case 1303:
                if (p != null)
                    return WithKind(Base("bias").Put("action", p.S("Action")).Put("bias", Js.Num(p.Get("Bias")))
                        .Put("kind", p.S("Kind")).Put("salience", Js.Num(p.Get("Salience"))), p.S("Kind"));
                if (Js.Match(msg, BiasRe) is { } m4)
                    return WithKind(Base("bias").Put("action", m4[1]).Put("bias", m4.D(2)).Put("kind", m4[3]).Put("salience", m4.D(4)), m4[3]);
                return null;
            default:
                if (p != null)
                    return WithKind(Base("seeded").Put("kind", p.S("Kind")).Put("salience", Js.Num(p.Get("Salience"))), p.S("Kind"));
                if (Js.Match(msg, SeededRe) is { } m5)
                    return WithKind(Base("seeded").Put("kind", m5[1]).Put("salience", m5.D(2)), m5[1]);
                return null;
        }
    }

    /// <summary>Per-goal timeline series from 1300/1301/1302 events (for the chart).</summary>
    public static JsonObject BuildTimelines(IEnumerable<JsonObject> goalEvents)
    {
        var series = new JsonObject();
        foreach (var ev in goalEvents)
        {
            var kind = ev.S("kind");
            if (kind == null) continue;
            if (series[kind] is not JsonArray arr) { arr = new JsonArray(); series[kind] = arr; }
            var type = ev.S("type");
            if (type is "activated" or "seeded")
                arr.Add(new JsonObject { ["worldTime"] = ev.Raw("worldTime"), ["salience"] = ev.Raw("salience"), ["progress"] = 0 });
            else if (type == "progressed")
                arr.Add(new JsonObject { ["worldTime"] = ev.Raw("worldTime"), ["salience"] = ev.Raw("salienceNew"), ["progress"] = ev.Raw("progressNew") });
            else if (type == "resolved")
                arr.Add(new JsonObject { ["worldTime"] = ev.Raw("worldTime"), ["salience"] = 0, ["progress"] = ev.Raw("progress") });
        }
        return series;
    }

    private static readonly Dictionary<int, string> IntentTypes = new()
    { [1005] = "selected", [1006] = "retained", [1007] = "switched", [1008] = "expired", [1009] = "emergency" };

    private static readonly Dictionary<string, (string Icon, string Level)> IntentMeta = new()
    {
        ["selected"] = ("🎯", "info"), ["retained"] = ("📌", "secondary"), ["switched"] = ("🔄", "warning"),
        ["expired"] = ("⏰", "secondary"), ["emergency"] = ("🆘", "danger"),
    };

    public static JsonObject? ParseIntentEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !IntentTypes.TryGetValue(eid, out var type)) return null;
        var msg = e.Message ?? "";
        var meta = IntentMeta[type];

        if (e.Payload is { } p)
        {
            var intentKind = p.S("IntentKind");
            var action = p.S("Action");
            var arrow = !string.IsNullOrEmpty(action) ? " → " + action : "";
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["type"] = type, ["intentKind"] = intentKind,
                ["action"] = action, ["score"] = p.Get("Score") != null ? Js.Num(p.Get("Score")) : null,
                ["icon"] = meta.Icon, ["level"] = meta.Level,
                ["label"] = eid == 1009 ? $"emergency: {intentKind}{arrow}" : $"{type}: {intentKind}{arrow}",
            };
        }

        if (eid == 1009)
        {
            var me = Js.Match(msg, IntentEmrgRe);
            if (me == null) return null;
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["eventId"] = 1009, ["type"] = type, ["intentKind"] = me[1], ["action"] = me[2],
                ["score"] = me.D(3), ["icon"] = meta.Icon, ["level"] = meta.Level, ["label"] = $"emergency: {me[1]} → {me[2]}",
            };
        }

        var m = Js.Match(msg, IntentRe);
        if (m == null) return null;
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["type"] = type, ["intentKind"] = m[1], ["action"] = m[2],
            ["score"] = !string.IsNullOrEmpty(m[3]) ? m.D(3) : null, ["icon"] = meta.Icon, ["level"] = meta.Level,
            ["label"] = $"{type}: {m[1]}{(!string.IsNullOrEmpty(m[2]) ? " → " + m[2] : "")}",
        };
    }

    public static JsonObject? ParseIntentBias(LogEntry e)
    {
        if (e.EventId != 1010) return null;
        string? action; double? bias;
        if (e.Payload is { } p) { action = p.S("Action"); bias = Js.Num(p.Get("Bias")); }
        else
        {
            var m = Js.Match(e.Message, IntentBiasRe);
            if (m == null) return null;
            action = m[1]; bias = m.D(2);
        }
        return new JsonObject
        {
            ["eventId"] = 1010, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["bias"] = bias,
            ["icon"] = "⚡", ["label"] = $"Bias: {action} +{(bias == null ? "NaN" : Js.Fmt(bias.Value))}", ["level"] = "info",
        };
    }

    private static JsonObject Put(this JsonObject o, string key, JsonNode? value) { o[key] = value; return o; }
}

public static class MemoryParsers
{
    private static readonly Regex EncodeRe = Js.Re(@"Zakódována\s+epizod[ua]:\s*'([^']+)'\s*\(salience=([\d.,]+),\s*emotion=(\w+)(?:,\s*perceived='([^']*)',\s*distortion=([\d.,]+))?\)");
    private static readonly Regex ConsolRe = Js.Re(@"posíleno\s+(\d+)");
    private static readonly Regex PerceivedRe = Js.Re(@"^Perceived(\w+):(.*)$", false);
    private static readonly Regex ReconsolRe = Js.Re($@"Episode\s+'([^']+)'\s+drifted:\s*emotion\s+(\w+){P.Arr}(\w+),\s*drift=([\d.,]+)");
    private static readonly Regex BeliefRe = Js.Re($@"Belief\s+about\s+\S+:\s*(\w+)\s+([\d.,]+){P.Arr}([\d.,]+)\s*\(evidence=(\d+)\)");
    private static readonly Regex MutualRe = Js.Re(@"subject=(\S+)\s+sharedWith=(\S+)\s+actionKind=(\S+)");

    private static readonly Dictionary<string, string> EmotionIcon = new()
    {
        ["Positive"] = "😊", ["Negative"] = "😞", ["Neutral"] = "😐", ["Joy"] = "😄", ["Fear"] = "😨", ["Anger"] = "😤",
        ["Sadness"] = "😢", ["Disgust"] = "🤢", ["Surprise"] = "😲", ["Tenderness"] = "🥰",
    };

    private static readonly Dictionary<string, string> EmotionLevel = new()
    {
        ["Positive"] = "success", ["Negative"] = "danger", ["Neutral"] = "secondary", ["Joy"] = "success", ["Fear"] = "warning",
        ["Anger"] = "danger", ["Sadness"] = "info", ["Disgust"] = "warning", ["Surprise"] = "primary", ["Tenderness"] = "danger",
    };

    private static readonly HashSet<string> PositiveBeliefs = new() { "EmotionallySafe", "Warm", "Reliable" };

    public static JsonObject? ParseEncoded(LogEntry e)
    {
        if (e.EventId != 3000) return null;
        string? tag, emotion, perceived; double? salience, distortion;
        if (e.Payload is { } p)
        {
            tag = p.S("Tag"); salience = Js.Num(p.Get("Salience")); emotion = p.S("Emotion");
            perceived = p.S("Perceived"); distortion = Js.Num(p.Get("Distortion"));
        }
        else
        {
            var m = Js.Match(e.Message, EncodeRe);
            if (m == null) return null;
            tag = m[1]; salience = m.D(2); emotion = m[3]; perceived = m[4]; distortion = m[5] != null ? m.D(5) : null;
        }
        string? kind = null, what = tag;
        if (!string.IsNullOrEmpty(perceived))
        {
            var pm = Js.Match(perceived, PerceivedRe);
            if (pm != null) { kind = pm[1]; what = pm[2]; } else what = perceived;
        }
        var distorted = (kind != null && what != tag) || (distortion != null && distortion >= 0.1 && kind != null);
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["tag"] = tag, ["salience"] = salience, ["emotion"] = emotion,
            ["perceivedWhat"] = what, ["distortionKind"] = kind, ["distortion"] = distortion, ["distorted"] = distorted,
            ["icon"] = emotion != null && EmotionIcon.TryGetValue(emotion, out var ic) ? ic : "💭",
            ["level"] = emotion != null && EmotionLevel.TryGetValue(emotion, out var lv) ? lv : "secondary",
        };
    }

    public static JsonObject? ParseConsolidated(LogEntry e)
    {
        if (e.EventId != 3001) return null;
        double? count;
        if (e.Payload is { } p) count = Js.ToInt(p.Get("Count"));
        else count = Js.Match(e.Message, ConsolRe) is { } m ? m.I(1) : null;
        return new JsonObject { ["worldTime"] = e.WorldTimeText, ["count"] = count, ["icon"] = "🔄", ["label"] = "Consolidated", ["level"] = "info" };
    }

    public static JsonObject? ParseReconsolidated(LogEntry e)
    {
        if (e.EventId != 3002) return null;
        string? tag, from, to; double? drift;
        if (e.Payload is { } p) { tag = p.S("Tag"); from = p.S("OldEmotion"); to = p.S("NewEmotion"); drift = Js.Num(p.Get("DriftFraction")); }
        else
        {
            var m = Js.Match(e.Message, ReconsolRe);
            if (m == null) return null;
            tag = m[1]; from = m[2]; to = m[3]; drift = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 3002, ["worldTime"] = e.WorldTimeText, ["tag"] = tag, ["fromEmotion"] = from, ["toEmotion"] = to,
            ["drift"] = drift, ["icon"] = "🔄", ["label"] = "Memory reconsolidated", ["level"] = "secondary",
        };
    }

    public static JsonObject? ParseBeliefUpdated(LogEntry e)
    {
        if (e.EventId != 3003) return null;
        string? kind; double? oldS, newS, evidence;
        if (e.Payload is { } p)
        {
            kind = p.S("Kind"); oldS = Js.Num(p.Get("OldStrength")); newS = Js.Num(p.Get("NewStrength")); evidence = Js.ToInt(p.Get("EvidenceCount"));
        }
        else
        {
            var m = Js.Match(e.Message, BeliefRe);
            if (m == null) return null;
            kind = m[1]; oldS = m.D(2); newS = m.D(3); evidence = m.I(4);
        }
        var positive = kind != null && PositiveBeliefs.Contains(kind);
        return new JsonObject
        {
            ["eventId"] = 3003, ["worldTime"] = e.WorldTimeText, ["partnerId"] = (e.RelatedPersonId ?? "").ToLowerInvariant(),
            ["beliefKind"] = kind, ["oldStrength"] = oldS, ["newStrength"] = newS, ["evidenceCount"] = evidence, ["delta"] = newS - oldS,
            ["icon"] = positive ? "💚" : "🔴", ["label"] = $"Belief: {kind}", ["level"] = positive ? "success" : "danger",
        };
    }

    public static JsonObject? ParseMutualKnowledge(LogEntry e)
    {
        if (e.EventId != 1610) return null;
        string? subject, shared, kind;
        if (e.Payload is { } p) { subject = p.S("Subject"); shared = p.S("SharedWith"); kind = p.S("ActionKind"); }
        else
        {
            var m = Js.Match(e.Message, MutualRe);
            if (m == null) return null;
            subject = m[1]; shared = m[2]; kind = m[3];
        }
        return new JsonObject
        {
            ["eventId"] = 1610, ["worldTime"] = e.WorldTimeText, ["subject"] = subject, ["sharedWith"] = shared, ["actionKind"] = kind,
            ["icon"] = "\U0001F9E0", ["label"] = $"Mutual knowledge: {kind ?? "?"}", ["level"] = "info",
        };
    }
}
