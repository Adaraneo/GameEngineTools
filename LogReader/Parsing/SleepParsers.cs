// Parsing/SleepParsers.cs — sleep engine and physiology-sleep parsers (port of parsers/sleep.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class SleepParsers
{
    private static readonly Dictionary<int, (string Label, string Icon, string Level)> Meta = new()
    {
        [1100] = ("Sleep Prompt", "😴", "info"), [1101] = ("Sleep Started", "💤", "info"),
        [1102] = ("Shared Sleep", "🛌", "info"), [1103] = ("Sleep Declined", "⚠️", "warning"),
        [1104] = ("Session Ended", "⏰", "secondary"), [1105] = ("Woke Up", "🌅", "success"),
        [1106] = ("Interrupted", "🚨", "danger"), [1107] = ("Phase Changed", "🔄", "secondary"),
        [1108] = ("Nightmare", "👻", "danger"), [1109] = ("Dream", "💭", "info"),
        [1110] = ("Ambush!", "⚔️", "danger"), [1111] = ("Grace Penalty", "⚠️", "warning"),
        [1112] = ("Emergency Sleep", "🆘", "danger"), [1113] = ("Sleep Blocked", "🚫", "warning"),
    };

    private static readonly Regex WokeUpRe = Js.Re(@"Délka:\s*([\d.,]+)h,\s*Kvalita:\s*([\d.,]+),\s*Přerušen:\s*(\w+)");
    private static readonly Regex InterruptedRe = Js.Re(@"přerušen\s*\(([^)]+)\)\s*ve\s*fázi\s+(\S+)");
    private static readonly Regex NightmareRe = Js.Re(@"stres\s*při\s*usnutí:\s*([\d.,]+)");
    private static readonly Regex BlockedRe = Js.Re(@"Hunger:([\d.,]+)\s+Thirst:([\d.,]+)");
    private static readonly Regex EmergencyRe = Js.Re(@"NeedRest:([\d.,]+)\s+Energy:([\d.,]+)");
    private static readonly Regex StartRe = Js.Re(@"Délka:\s*([\d.,]+)h");
    private static readonly Regex CycleRe = Js.Re(@"Phase:(\S+)\s+Day:(\d+)", false);
    private static readonly Regex SleepEndedRe = Js.Re(@"délka:\s*([\d.,]+)h,\s*kvalita:\s*([\d.,]+),\s*dluh[^:]*:\s*([\d.,]+)h");

    public static JsonObject? ParseSleepEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !Meta.TryGetValue(eid, out var meta)) return null;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["label"] = meta.Label,
            ["icon"] = meta.Icon, ["level"] = meta.Level, ["message"] = msg,
        };
        var p = e.Payload;
        switch (eid)
        {
            case 1105:
                if (p != null)
                {
                    o["hours"] = Js.Num(p.Get("Hours")); o["quality"] = Js.Num(p.Get("Quality"));
                    o["interrupted"] = Js.IsTrue(p.Get("WasInterrupted"));
                }
                else if (Js.Match(msg, WokeUpRe) is { } m)
                {
                    o["hours"] = m.D(1); o["quality"] = m.D(2); o["interrupted"] = m[3]!.ToLowerInvariant() == "true";
                }
                break;
            case 1106:
                if (p != null) { o["cause"] = p.Raw("Cause"); o["phase"] = p.Raw("Phase"); }
                else if (Js.Match(msg, InterruptedRe) is { } m) { o["cause"] = m[1]; o["phase"] = m[2]; }
                break;
            case 1108:
                if (p != null) o["stressAtSleep"] = Js.Num(p.Get("StressAtSleep"));
                else if (Js.Match(msg, NightmareRe) is { } m) o["stressAtSleep"] = m.D(1);
                break;
            case 1113:
                if (p != null) { o["hunger"] = Js.Num(p.Get("Hunger")); o["thirst"] = Js.Num(p.Get("Thirst")); }
                else if (Js.Match(msg, BlockedRe) is { } m) { o["hunger"] = m.D(1); o["thirst"] = m.D(2); }
                break;
            case 1112:
                if (p != null) { o["needRest"] = Js.Num(p.Get("NeedRest")); o["energy"] = Js.Num(p.Get("Energy")); }
                else if (Js.Match(msg, EmergencyRe) is { } m) { o["needRest"] = m.D(1); o["energy"] = m.D(2); }
                break;
            case 1101:
                if (p != null) o["plannedHours"] = Js.Num(p.Get("Hours"));
                else if (Js.Match(msg, StartRe) is { } m) o["plannedHours"] = m.D(1);
                break;
        }
        return o;
    }

    public static JsonObject? ParsePhysioCycle(LogEntry e)
    {
        if (e.Payload is { } p)
        {
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["phase"] = p.Raw("Phase"), ["dayInCycle"] = Js.ToInt(p.Get("DayInCycle")),
                ["libidoMod"] = p.Get("Libido") != null ? Js.Num(p.Get("Libido")) : null,
                ["ovulationWindow"] = p.Get("Ovulation") != null ? Js.IsTrue(p.Get("Ovulation")) : null,
                ["symptomPain"] = p.Get("Pain") != null ? Js.Num(p.Get("Pain")) : null,
                ["symptomBloat"] = p.Get("Bloat") != null ? Js.Num(p.Get("Bloat")) : null,
                ["symptomBreastTender"] = p.Get("Tender") != null ? Js.Num(p.Get("Tender")) : null,
                ["pmddActive"] = p.Get("Pmdd") != null ? Js.IsTrue(p.Get("Pmdd")) : null,
            };
        }
        var m = Js.Match(e.Message, CycleRe);
        if (m == null) return null;
        return new JsonObject { ["worldTime"] = e.WorldTimeText, ["phase"] = m[1], ["dayInCycle"] = m.I(2) };
    }

    public static JsonObject? ParsePhysioSleepEnded(LogEntry e)
    {
        if (e.Payload is { } p)
            return new JsonObject
            {
                ["worldTime"] = e.WorldTimeText, ["hours"] = Js.Num(p.Get("Hours")),
                ["quality"] = Js.Num(p.Get("Quality")), ["debtAfter"] = Js.Num(p.Get("Debt")),
            };
        var m = Js.Match(e.Message, SleepEndedRe);
        if (m == null) return null;
        return new JsonObject { ["worldTime"] = e.WorldTimeText, ["hours"] = m.D(1), ["quality"] = m.D(2), ["debtAfter"] = m.D(3) };
    }
}
