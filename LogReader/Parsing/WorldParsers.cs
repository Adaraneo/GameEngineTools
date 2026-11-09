// Parsing/WorldParsers.cs — schedule, scheduler, travel, world-object and dialogue parsers
// (port of parsers/schedule.js, movement.js, objects.js, dialogue.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class ScheduleParsers
{
    private static readonly Regex DayRe = Js.Re(@"Day\s+(\d+)\s+registered[:\s]+(\d+)\s+slot");
    private static readonly Regex SlotRe = Js.Re(@"Slot\s+(\S+)\s+triggered[:\s]+(\S+)\s+@\s+(.+)");
    private static readonly Regex BiasRe = Js.Re(@"Slot\s+(\S+)\s+bias\s+\+([\d.,]+)\s+on\s+(\S+)");
    private static readonly Regex SkipRe = Js.Re(@"Slot\s+(\S+)\s+skipped.*stress=([\d.,]+).*energy=([\d.,]+)");
    private static readonly Regex SeededRe = Js.Re(@"Seeded\s+from\s+(\S+)[:\s]+(\d+)\s+slot");

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> Meta = new()
    {
        [1400] = ("📅", "Day registered", "secondary"), [1401] = ("⏰", "Slot triggered", "info"),
        [1402] = ("📌", "Slot bias", "primary"), [1403] = ("⏭️", "Slot skipped", "warning"),
        [1404] = ("🌱", "Schedule seeded", "success"),
    };

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> SchedulerMeta = new()
    {
        [4100] = ("▶️", "Scheduler started", "info"), [4101] = ("🔄", "Scheduler tick", "secondary"),
        [4102] = ("💤", "Scheduler idle", "secondary"), [4103] = ("❌", "Scheduler failed", "danger"),
    };

    public static JsonObject? ParseScheduleEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !Meta.TryGetValue(eid, out var meta)) return null;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["eventId"] = eid, ["worldTime"] = e.WorldTimeText, ["icon"] = meta.Icon,
            ["label"] = meta.Label, ["level"] = meta.Level, ["message"] = msg,
        };
        if (e.Payload is { } p)
        {
            switch (eid)
            {
                case 1400: o["dayIndex"] = Js.ToInt(p.Get("DayIndex")); o["slotCount"] = Js.ToInt(p.Get("SlotCount")); break;
                case 1401: o["slotId"] = p.Raw("SlotId"); o["action"] = p.Raw("Action"); o["location"] = p.Raw("Location"); break;
                case 1402: o["slotId"] = p.Raw("SlotId"); o["bias"] = Js.Num(p.Get("Bias")); o["action"] = p.Raw("Action"); break;
                case 1403: o["slotId"] = p.Raw("SlotId"); o["stress"] = Js.Num(p.Get("Stress")); o["energy"] = Js.Num(p.Get("Energy")); break;
                case 1404: o["occupation"] = p.Raw("Occupation"); o["slotCount"] = Js.ToInt(p.Get("SlotCount")); break;
            }
            return o;
        }
        switch (eid)
        {
            case 1400 when Js.Match(msg, DayRe) is { } m: o["dayIndex"] = m.I(1); o["slotCount"] = m.I(2); break;
            case 1401 when Js.Match(msg, SlotRe) is { } m: o["slotId"] = m[1]; o["action"] = m[2]; o["location"] = m[3]!.Trim(); break;
            case 1402 when Js.Match(msg, BiasRe) is { } m: o["slotId"] = m[1]; o["bias"] = m.D(2); o["action"] = m[3]; break;
            case 1403 when Js.Match(msg, SkipRe) is { } m: o["slotId"] = m[1]; o["stress"] = m.D(2); o["energy"] = m.D(3); break;
            case 1404 when Js.Match(msg, SeededRe) is { } m: o["occupation"] = m[1]; o["slotCount"] = m.I(2); break;
        }
        return o;
    }

    public static JsonObject? ParseSchedulerEvent(LogEntry e)
    {
        if (e.EventId is not { } eid || !SchedulerMeta.TryGetValue(eid, out var meta)) return null;
        return new JsonObject
        {
            ["eventId"] = eid, ["worldTime"] = e.WorldTimeText, ["message"] = e.Message ?? "",
            ["icon"] = meta.Icon, ["label"] = meta.Label, ["level"] = meta.Level,
        };
    }
}

public static class MovementParsers
{
    private static readonly Regex DepartedRe = Js.Re(@"departed for (\S+), ETA (.+?) \(([\d.,]+)min\)");
    private static readonly Regex ArrivedRe = Js.Re(@"arrived at (\S+)\.");

    public static JsonObject? ParseTravelEvent(LogEntry e)
    {
        if (e.EventId is not (1700 or 1701)) return null;
        var departed = e.EventId == 1700;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["eventId"] = e.EventId, ["worldTime"] = e.WorldTimeText,
            ["icon"] = departed ? "🚶" : "🏁", ["label"] = departed ? "Departed" : "Arrived",
            ["kind"] = departed ? "departed" : "arrived", ["level"] = departed ? "info" : "success", ["message"] = msg,
        };
        var p = e.Payload;
        if (departed)
        {
            if (p != null)
            {
                o["destination"] = p.Raw("Destination"); o["minutes"] = Js.Num(p.Get("Minutes")); o["arriveAt"] = p.Raw("ArriveAt");
            }
            else if (Js.Match(msg, DepartedRe) is { } m) { o["destination"] = m[1]; o["arriveAt"] = m[2]; o["minutes"] = m.D(3); }
        }
        else if (p != null) o["destination"] = p.Raw("Destination");
        else if (Js.Match(msg, ArrivedRe) is { } m) o["destination"] = m[1];
        return o;
    }
}

public static class ObjectParsers
{
    private static readonly Regex UseRe = Js.Re(@"used\s+(\S+)\s+\(([^)]+)\)\s+at\s+(\S+):\s+affordances=(\S+)\s+sat=([\d.,]+)\s+consumed=(true|false)");
    private static readonly Regex RespawnRe = Js.Re(@"(\S+)\s+\(([^)]+)\)\s+at\s+(\S+)\s+respawned\s+after\s+([\d.,]+)min");

    private static readonly Dictionary<string, string> AffordanceIcons = new()
    {
        ["Hunger"] = "🍖", ["Thirst"] = "💧", ["Rest"] = "🛏️", ["Social"] = "🤝", ["Work"] = "⚒️",
        ["Entertainment"] = "🎵", ["Warmth"] = "🔥", ["MoodBoost"] = "✨", ["StressRaise"] = "⚠️",
    };

    private static string AffordanceIcon(string types) =>
        AffordanceIcons.TryGetValue(types.Split('+')[0], out var i) ? i : "📦";

    private static string AffordanceLevel(string types, bool consumed) =>
        types.Contains("Hunger") ? "success" : types.Contains("Thirst") ? "info" : types.Contains("Rest") ? "primary"
        : consumed ? "warning" : "secondary";

    public static JsonObject? ParseObjectUsed(LogEntry e)
    {
        if (e.EventId != 1500) return null;
        string? objectId, displayName, locationId, affordances; double? satisfaction; bool consumed;
        if (e.Payload is { } p)
        {
            objectId = p.S("ObjectId"); displayName = p.S("DisplayName"); locationId = p.S("LocationId");
            affordances = p.S("Affordances"); satisfaction = Js.Num(p.Get("Satisfaction"));
            consumed = Js.IsTrue(p.Get("WasConsumed"));
        }
        else
        {
            var m = Js.Match(e.Message, UseRe);
            if (m == null) return null;
            objectId = m[1]; displayName = m[2]; locationId = m[3]; affordances = m[4]; satisfaction = m.D(5);
            consumed = m[6] == "true";
        }
        var aff = affordances ?? "";
        return new JsonObject
        {
            ["eventId"] = 1500, ["worldTime"] = e.WorldTimeText, ["objectId"] = objectId, ["displayName"] = displayName,
            ["locationId"] = locationId, ["affordances"] = affordances, ["satisfaction"] = satisfaction, ["consumed"] = consumed,
            ["icon"] = AffordanceIcon(aff),
            ["label"] = $"{displayName} ({affordances}){(consumed ? " — consumed" : "")}",
            ["level"] = AffordanceLevel(aff, consumed),
        };
    }

    public static JsonObject? ParseObjectRespawned(LogEntry e)
    {
        if (e.EventId != 1501) return null;
        string? objectId, displayName, locationId; double? elapsed;
        if (e.Payload is { } p)
        {
            objectId = p.S("ObjectId"); displayName = p.S("DisplayName"); locationId = p.S("LocationId");
            elapsed = Js.Num(p.Get("ElapsedMinutes"));
        }
        else
        {
            var m = Js.Match(e.Message, RespawnRe);
            if (m == null) return null;
            objectId = m[1]; displayName = m[2]; locationId = m[3]; elapsed = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 1501, ["worldTime"] = e.WorldTimeText, ["objectId"] = objectId, ["displayName"] = displayName,
            ["locationId"] = locationId, ["elapsedMinutes"] = elapsed,
            ["icon"] = "♻️", ["label"] = $"{displayName} respawned", ["level"] = "secondary",
        };
    }
}

public static class DialogueParsers
{
    private static readonly Regex SpeechActRe =
        Js.Re(@"„(.*)“\s*\(kind=(\w+),\s*predik[áa]t=([^,]*),\s*register=(\w+),\s*directness=(\w+)(?:,\s*odpov[ěe]ď=(\w+),\s*naAkt=(\w+))?\)");

    public static JsonObject? ParseSpeechActUttered(LogEntry e)
    {
        if (e.EventId != 1220) return null;
        JsonNode? speakerName = null, addresseeName = null, relationalKind, predicateLemma, register, directness, utterance;
        string speakerId = "", addresseeId = "";
        bool isResponse; string? respondingTo;
        if (e.Payload is { } p)
        {
            speakerId = (p.S("SpeakerId") ?? "").ToLowerInvariant(); speakerName = p.Raw("SpeakerName");
            addresseeId = (p.S("AddresseeId") ?? "").ToLowerInvariant(); addresseeName = p.Raw("AddresseeName");
            relationalKind = p.Raw("RelationalKind"); predicateLemma = p.Raw("PredicateLemma");
            register = p.Raw("Register"); directness = p.Raw("Directness"); utterance = p.Raw("Utterance");
            isResponse = Js.IsTrue(p.Get("IsResponse"));
            respondingTo = p.S("RespondingToKind") ?? "None";
        }
        else
        {
            var m = Js.Match(e.Message, SpeechActRe);
            if (m == null) return null;
            utterance = m[1]; relationalKind = m[2]; predicateLemma = m[3]!.Trim(); register = m[4]; directness = m[5];
            isResponse = (m[6] ?? "").ToLowerInvariant() == "true";
            respondingTo = m[7] ?? "None";
        }
        var kind = Js.Str(relationalKind); var lemma = Js.Str(predicateLemma);
        return new JsonObject
        {
            ["eventId"] = 1220, ["worldTime"] = e.WorldTimeText,
            ["partnerId"] = (e.RelatedPersonId ?? addresseeId).ToLowerInvariant(),
            ["speakerId"] = speakerId, ["speakerName"] = speakerName, ["addresseeId"] = addresseeId, ["addresseeName"] = addresseeName,
            ["relationalKind"] = relationalKind?.DeepClone(), ["predicateLemma"] = predicateLemma?.DeepClone(),
            ["register"] = register?.DeepClone(), ["directness"] = directness?.DeepClone(), ["utterance"] = utterance?.DeepClone(),
            ["isResponse"] = isResponse, ["respondingToKind"] = respondingTo,
            ["icon"] = isResponse ? "↩️" : "🗨️",
            ["label"] = isResponse ? $"{kind}: {lemma} (odpověď na {respondingTo})" : $"{kind}: {lemma}",
            ["level"] = "info", ["kind"] = "utterance",
        };
    }
}
