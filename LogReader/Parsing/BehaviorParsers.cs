// Parsing/BehaviorParsers.cs — Behavior engine event parsers (port of parsers/behavior.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class BehaviorParsers
{
    private static readonly Regex DecisionRe = Js.Re(@"→\s+(\S+)\s+\(utility:\s*([\d.,]+)\)", false);
    private static readonly Regex PlanRe = Js.Re(@"Action:(\S+)\s+Start:(\S+)\s+Duration:(\S+)\s+Utility:([\d.,]+)");
    private static readonly Regex DarkCoreRe = Js.Re(@"Action:(\S+)\s+Kind:(\S+)\s+DarkCore:([\d.,]+)\s+Delta:(-?[\d.,]+)");
    private static readonly Regex Habit1011Re = Js.Re($@"learned\s+(\S+)\s+cue=(\S+)\s+surface=(\S+)\s+time=(\S+)\s+strength\s+([\d.,]+){P.Arr}([\d.,]+).*?reps=(\d+)");
    private static readonly Regex HabitDecayRe = Js.Re($@"decay\s+days=([\d.,]+)\s+retention=([\d.,]+)\s+traces\s+(\d+){P.Arr}(\d+)\s+removed=(\d+)");
    private static readonly Regex HabitPruneRe = Js.Re($@"pruned\s+traces\s+(\d+){P.Arr}(\d+)\s+max=(\d+)");
    private static readonly Regex HabitBiasRe = Js.Re($@"bias\s+(\S+):\s+utility\s+([\d.,]+){P.Arr}([\d.,]+),\s*applicability=([\d.,]+),\s*multiplier=([\d.,]+)");
    private static readonly Regex HabitDomRe = Js.Re(@"Habit\s+dominated\s+decision:\s*(\S+)\s*\(habitBias=([\d.,]+),\s*totalUtility=([\d.,]+),\s*ratio=([\d.,]+)\)");
    private static readonly Regex NeedThreshRe = Js.Re($@"(\w+)\s+crossed\s+threshold\s+([\d.,]+):\s*([\d.,]+){P.Arr}([\d.,]+)");
    private static readonly Regex CandidateRe = Js.Re(@"Action:(\S+)\s+Utility:([\d.,]+)");
    private static readonly Regex RunningRe = Js.Re(@"Akce\s+'([^']+)'\s+stále\s+běží,\s*zbývá\s+(.+?)\.");
    private static readonly Regex Action1003Re = Js.Re(@"Nová\s+akce:\s+'([^']+)'\s+\(utility=([\d.,]+),\s*trvání=([^)]+)\)");
    private static readonly Regex ActionDurRe = Js.Re(@"trvání=([^)]+)");
    private static readonly Regex CooldownRe = Js.Re(@"Cooldown\s+'([^']+)':\s*([\d.,]+)h");

    public static JsonObject? ParseDecision(LogEntry e)
    {
        if (e.EventId != 1001) return null;
        JsonNode? action; double? utility;
        if (e.Payload is { } p) { action = p.Get("Action"); utility = Js.Num(p.Get("Utility")); }
        else
        {
            var m = Js.Match(e.Message, DecisionRe);
            if (m == null) return null;
            action = m[1]; utility = m.D(2);
        }
        var meta = P.Meta(Js.Str(action), "▶️", "secondary");
        return new JsonObject { ["worldTime"] = e.WorldTimeText, ["action"] = action?.DeepClone(), ["utility"] = utility }.Spread(meta);
    }

    public static JsonObject? ParsePlanDetail(LogEntry e)
    {
        if (e.EventId != 5201) return null;
        JsonNode? action, start, duration; double? utility;
        if (e.Payload is { } p)
        {
            action = p.Get("Action"); start = p.Get("Start"); duration = p.Get("Duration"); utility = Js.Num(p.Get("Utility"));
        }
        else
        {
            var m = Js.Match(e.Message, PlanRe);
            if (m == null) return null;
            action = m[1]; start = m[2]; duration = m[3]; utility = m.D(4);
        }
        var meta = P.Meta(Js.Str(action), "📋", "secondary");
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["action"] = action?.DeepClone(), ["start"] = start?.DeepClone(),
            ["duration"] = duration?.DeepClone(), ["utility"] = utility,
        }.Spread(meta);
    }

    public static JsonObject? ParseDarkCoreModifier(LogEntry e)
    {
        if (e.EventId != 1017) return null;
        string? action, kind; double? darkCore, delta;
        if (e.Payload is { } p)
        {
            action = p.S("Action"); kind = p.S("Kind"); darkCore = Js.Num(p.Get("DarkCore")); delta = Js.Num(p.Get("Delta"));
        }
        else
        {
            var m = Js.Match(e.Message, DarkCoreRe);
            if (m == null) return null;
            action = m[1]; kind = m[2]; darkCore = m.D(3); delta = m.D(4);
        }
        var isAntagonism = kind == "Antagonism";
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = 1017, ["action"] = action, ["kind"] = kind,
            ["darkCore"] = darkCore, ["delta"] = delta,
            ["icon"] = isAntagonism ? "😈" : "🧊",
            ["label"] = isAntagonism
                ? $"DarkCore antagonism bonus on {action} (+{Js.Fx(delta, 2)})"
                : $"DarkCore prosocial penalty on {action} ({Js.Fx(delta, 2)})",
            ["level"] = "secondary",
        };
    }

    public static JsonObject? ParseHabit(LogEntry e)
    {
        if (e.EventId != 1011) return null;
        var msg = e.Message ?? "";
        if (e.Payload is { } p)
        {
            var reps = p.Get("RepetitionCount");
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["eventId"] = 1011,
                ["habitName"] = p.Get("Action")?.DeepClone(), ["cue"] = p.Get("Cue")?.DeepClone(),
                ["surface"] = p.Get("Surface")?.DeepClone(), ["timeBand"] = p.Get("TimeBand")?.DeepClone(),
                ["strengthBefore"] = Js.Num(p.Get("BeforeStrength")), ["strengthAfter"] = Js.Num(p.Get("AfterStrength")),
                ["repetitions"] = Js.NumberOf(reps) ?? (double?)Js.ParseInt(Js.Str(reps)),
                ["icon"] = "💪", ["label"] = "Habit reinforced", ["level"] = "success",
            };
        }
        var m = Js.Match(msg, Habit1011Re);
        if (m == null)
            return new JsonObject { ["worldTime"] = e.WorldTimeText, ["eventId"] = 1011, ["message"] = msg, ["icon"] = "💪", ["label"] = "Habit reinforced", ["level"] = "success" };
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = 1011, ["habitName"] = m[1], ["cue"] = m[2], ["surface"] = m[3],
            ["timeBand"] = m[4], ["strengthBefore"] = m.D(5), ["strengthAfter"] = m.D(6), ["repetitions"] = m.I(7),
            ["icon"] = "💪", ["label"] = "Habit reinforced", ["level"] = "success",
        };
    }

    public static JsonObject? ParseHabitDecayed(LogEntry e)
    {
        if (e.EventId != 1012) return null;
        var o = new JsonObject { ["worldTime"] = e.WorldTimeText, ["eventId"] = 1012 };
        if (e.Payload is { } p)
        {
            o["days"] = Js.Num(p.Get("Days")); o["retention"] = Js.Num(p.Get("Retention"));
            o["countBefore"] = Js.NumberOf(p.Get("BeforeCount")); o["countAfter"] = Js.NumberOf(p.Get("AfterCount"));
            o["removed"] = Js.NumberOf(p.Get("RemovedCount"));
        }
        else
        {
            var m = Js.Match(e.Message, HabitDecayRe);
            o["days"] = m?.D(1); o["retention"] = m?.D(2); o["countBefore"] = m?.I(3);
            o["countAfter"] = m?.I(4); o["removed"] = m?.I(5);
        }
        o["icon"] = "📉"; o["label"] = "Habit decay"; o["level"] = "secondary";
        return o;
    }

    public static JsonObject? ParseHabitPruned(LogEntry e)
    {
        if (e.EventId != 1013) return null;
        var o = new JsonObject { ["worldTime"] = e.WorldTimeText, ["eventId"] = 1013 };
        if (e.Payload is { } p)
        {
            o["countBefore"] = Js.NumberOf(p.Get("BeforeCount")); o["countAfter"] = Js.NumberOf(p.Get("AfterCount"));
            o["maxTraces"] = Js.NumberOf(p.Get("MaxTraces"));
        }
        else
        {
            var m = Js.Match(e.Message, HabitPruneRe);
            o["countBefore"] = m?.I(1); o["countAfter"] = m?.I(2); o["maxTraces"] = m?.I(3);
        }
        o["icon"] = "✂️"; o["label"] = "Habit pruned"; o["level"] = "secondary";
        return o;
    }

    public static JsonObject? ParseHabitBias(LogEntry e)
    {
        if (e.EventId != 1014) return null;
        string? action; double? before, after, appl, mult;
        if (e.Payload is { } p)
        {
            action = p.S("Action");
            before = Js.Num(p.Get("BeforeUtility")); after = Js.Num(p.Get("AfterUtility"));
            appl = Js.Num(p.Get("Applicability")); mult = Js.Num(p.Get("Multiplier"));
        }
        else
        {
            var m = Js.Match(e.Message, HabitBiasRe);
            if (m == null)
                return new JsonObject { ["worldTime"] = e.WorldTimeText, ["eventId"] = 1014, ["message"] = e.Message ?? "", ["icon"] = "⚡", ["label"] = "Habit bias", ["level"] = "info" };
            action = m[1]; before = m.D(2); after = m.D(3); appl = m.D(4); mult = m.D(5);
        }
        var meta = P.Meta(action, "⚡", "info");
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = 1014, ["action"] = action,
            ["utilityBefore"] = before, ["utilityAfter"] = after, ["applicability"] = appl, ["multiplier"] = mult,
            ["icon"] = meta.Icon, ["label"] = $"Habit bias: {action}", ["level"] = "info",
        };
    }

    public static JsonObject? ParseHabitDominated(LogEntry e)
    {
        if (e.EventId != 1015) return null;
        string? action; double? bias, total, ratio;
        if (e.Payload is { } p)
        {
            action = p.S("Action"); bias = Js.Num(p.Get("HabitBias")); total = Js.Num(p.Get("TotalUtility")); ratio = Js.Num(p.Get("Ratio"));
        }
        else
        {
            var m = Js.Match(e.Message, HabitDomRe);
            action = m?[1]; bias = m?.D(2); total = m?.D(3); ratio = m?.D(4);
        }
        var meta = P.Meta(action, "💪", "warning", action ?? "action");
        return new JsonObject
        {
            ["eventId"] = 1015, ["worldTime"] = e.WorldTimeText, ["action"] = action,
            ["habitBias"] = bias, ["totalUtility"] = total, ["ratio"] = ratio,
        }.Spread(meta);
    }

    public static JsonObject? ParseNeedThresholdCrossed(LogEntry e)
    {
        if (e.EventId != 1016) return null;
        string? need; double? threshold, oldV, newV;
        if (e.Payload is { } p)
        {
            need = p.S("Need"); threshold = Js.Num(p.Get("Threshold")); oldV = Js.Num(p.Get("OldValue")); newV = Js.Num(p.Get("NewValue"));
        }
        else
        {
            var m = Js.Match(e.Message, NeedThreshRe);
            need = m?[1]; threshold = m?.D(2); oldV = m?.D(3); newV = m?.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 1016, ["worldTime"] = e.WorldTimeText, ["need"] = need, ["threshold"] = threshold,
            ["oldValue"] = oldV, ["newValue"] = newV,
            ["icon"] = "⚠️", ["label"] = $"{need ?? "Need"} threshold", ["level"] = newV > 85 ? "danger" : "warning",
        };
    }

    public static JsonObject? ParseCandidate(LogEntry e)
    {
        if (e.EventId != 1000) return null;
        string? action; double? utility;
        if (e.Payload is { } p) { action = p.S("Action"); utility = Js.Num(p.Get("Utility")); }
        else
        {
            var m = Js.Match(e.Message, CandidateRe);
            if (m == null) return null;
            action = m[1]; utility = m.D(2);
        }
        var meta = P.Meta(action, "🔍", "secondary");
        return new JsonObject { ["eventId"] = 1000, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["utility"] = utility }
            .Spread(meta).Also(o => o["label"] = $"Candidate: {(P.HasMeta(action) ? meta.Label : action)}");
    }

    public static JsonObject? ParseRunning(LogEntry e)
    {
        if (e.EventId != 1002) return null;
        string? action, remaining;
        if (e.Payload is { } p) { action = p.S("Action"); remaining = (p.S("Remaining") ?? "").Trim(); }
        else
        {
            var m = Js.Match(e.Message, RunningRe);
            if (m == null) return null;
            action = m[1]; remaining = m[2]!.Trim();
        }
        var meta = P.Meta(action, "▶️", "secondary");
        return new JsonObject { ["eventId"] = 1002, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["remaining"] = remaining }
            .Spread(meta).Also(o => o["label"] = $"Running: {meta.Label}");
    }

    /// <summary>EventId 1003 — committed action (used for both the physio and behavior views).</summary>
    public static JsonObject? ParseAction1003(LogEntry e)
    {
        if (e.EventId != 1003) return null;
        if (e.Payload is { } p)
        {
            var act = p.S("Action");
            if (string.IsNullOrEmpty(act)) return null;
            var meta0 = P.Meta(act, "▶️", "secondary");
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["eventId"] = 1003, ["action"] = act,
                ["utility"] = p.Get("Utility") != null ? Js.Num(p.Get("Utility")) : null,
                ["duration"] = p.Get("Duration") != null ? Js.Str(p.Get("Duration"))!.Trim() : null,
            }.Spread(meta0);
        }

        var msg = e.Message ?? "";
        var m = Js.Match(msg, Action1003Re);
        var action = m?[1];
        if (action == null)
        {
            foreach (var key in P.ActionKeys)
                if (msg.Contains(key, StringComparison.Ordinal)) { action = key; break; }
        }
        if (action == null) return null;

        var meta = P.Meta(action, "▶️", "secondary");
        var durMatch = m != null ? null : Js.Match(msg, ActionDurRe);
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = 1003, ["action"] = action,
            ["utility"] = m?.D(2), ["duration"] = m != null ? m[3]!.Trim() : durMatch?[1],
        }.Spread(meta);
    }

    public static JsonObject? ParseCooldownSet(LogEntry e)
    {
        if (e.EventId != 1004) return null;
        string? action; double? hours;
        if (e.Payload is { } p) { action = p.S("Action"); hours = Js.Num(p.Get("Hours")); }
        else
        {
            var m = Js.Match(e.Message, CooldownRe);
            if (m == null) return null;
            action = m[1]; hours = m.D(2);
        }
        var meta = P.Meta(action, "⏱️", "secondary");
        return new JsonObject { ["eventId"] = 1004, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["cooldownHours"] = hours }
            .Spread(meta).Also(o => { o["label"] = $"Cooldown: {meta.Label} ({(hours == null ? "NaN" : Js.Fmt(hours.Value))}h)"; o["level"] = "secondary"; });
    }

    private static JsonObject Also(this JsonObject o, Action<JsonObject> f) { f(o); return o; }
}
