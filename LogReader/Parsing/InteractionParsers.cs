// Parsing/InteractionParsers.cs — interaction engine parsers (port of parsers/interaction.js).
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LogReader.Parsing;

public static class InteractionParsers
{
    private static readonly Regex CtxRe = Js.Re(@"lokace='([^']+)',\s*hluk=([\d.,]+),\s*přeplněnost=([\d.,]+)");
    private static readonly Regex DecRe = Js.Re(@"p\([^)]*\)=([\d.,]+)[,\s]+v[ýy]sledek=(\w+)");
    private static readonly Regex TouchRe = Js.Re(@"level=(\w+)[,\s]+p\([^)]*\)=([\d.,]+)[,\s]+v[ýy]sledek=(\w+)");
    private static readonly Regex SexualRe = Js.Re(@"[→>]\s*\S+\s*:\s*([^,]+),\s*([^,]+),\s*(.+)$", false);
    private static readonly Regex ResultRe = Js.Re(@"v[ýy]sledek=(\w+)");
    private static readonly Regex ProbRe = Js.Re(@"p\([^)]*\)=([\d.,]+)");
    private static readonly Regex LevelRe = Js.Re(@"level=(\w+)");
    private static readonly Regex MisattribRe = Js.Re(@"stress=([\d.,]+),\s*noise=([\d.,]+),\s*penalty=([\d.,]+)");
    private static readonly Regex ObjInteractRe = Js.Re(@"Object interaction:\s*(\S+)\s+kind=(\S+)\s+at=(\S+)\s+\(utility=([\d.,]+)\)");
    private static readonly Regex MoveActionRe = Js.Re(@"Move:\s*(\S+)\s+\(from='([^']*)',\s*utility=([\d.,]+)\)");
    private static readonly Regex NormViolationRe = Js.Re(@"Norm violation detected:\s*kind=(\w+),\s*score=([\d.,]+),\s*acceptancePenalty=([\d.,]+)");
    private static readonly Regex NormShameRe = Js.Re(@"Shame spike:\s*kind=(\w+),\s*score=([\d.,]+),\s*audience=(true|false)");
    private static readonly Regex NormObserverRe = Js.Re(@"Observer\s+(\S+):\s*reaction=(\w+),\s*score=([\d.,]+)");
    private static readonly Regex ReachOutRe = Js.Re(@"->\s*([0-9a-f-]+):\s*act=(\w+),\s*familiarity=([\d.,]+),\s*trust=([\d.,]+),\s*comfort=([\d.,]+),\s*closeness=([\d.,]+),\s*romantic=([\d.,]+),\s*privacy=(\w+)");
    private static readonly Regex MoveNeedRe = Js.Re(@"needs (\w+) but found no location");
    private static readonly Regex MoveReqRe = Js.Re(@"requested (\w+) but no suitable location.*?Current location:\s*([^.]+)\.");

    private static string Lower(string? s) => (s ?? "").ToLowerInvariant();

    public static JsonObject? ParseContext(LogEntry e)
    {
        if (e.EventId != 1200) return null;
        if (e.Payload is { } p)
            return new JsonObject { ["worldTime"] = e.WorldTimeText, ["location"] = p.Raw("Location"), ["noise"] = Js.Num(p.Get("Noise")), ["crowding"] = Js.Num(p.Get("Crowding")) };
        var m = Js.Match(e.Message, CtxRe);
        return m == null ? null : new JsonObject { ["worldTime"] = e.WorldTimeText, ["location"] = m[1], ["noise"] = m.D(2), ["crowding"] = m.D(3) };
    }

    public static JsonObject? ParseDecision(LogEntry e)
    {
        if (e.EventId != 1201) return null;
        double? prob; string? outcome;
        if (e.Payload is { } p) { prob = Js.Num(p.Get("P")); outcome = P.NormalizeOutcome(p.S("Result")); }
        else
        {
            var m = Js.Match(e.Message, DecRe);
            if (m == null) return null;
            prob = m.D(1); outcome = P.NormalizeOutcome(m[2]);
        }
        return new JsonObject { ["worldTime"] = e.WorldTimeText, ["probability"] = prob, ["outcome"] = outcome, ["partnerId"] = Lower(e.RelatedPersonId), ["type"] = "verbal" };
    }

    public static JsonObject? ParseTouchDecision(LogEntry e)
    {
        if (e.EventId != 1202) return null;
        JsonNode? level; double? prob; string? outcome;
        if (e.Payload is { } p) { level = p.Raw("Level"); prob = Js.Num(p.Get("P")); outcome = P.NormalizeOutcome(p.S("Result")); }
        else
        {
            var m = Js.Match(e.Message, TouchRe);
            if (m == null) return null;
            level = m[1]; prob = m.D(2); outcome = P.NormalizeOutcome(m[3]);
        }
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["touchLevel"] = level, ["probability"] = prob, ["outcome"] = outcome,
            ["partnerId"] = Lower(e.RelatedPersonId), ["type"] = "touch",
        };
    }

    public static JsonObject? ParseSexualProposal(LogEntry e)
    {
        if (e.EventId != 1203) return null;
        var msg = e.Message ?? "";
        JsonNode? intent, repr, contra;
        if (e.Payload is { } p) { intent = p.Raw("Intent"); repr = p.Raw("ReprPotential"); contra = p.Raw("Contraception"); }
        else
        {
            var m = Js.Match(msg, SexualRe);
            intent = m?[1]?.Trim(); repr = m?[2]?.Trim(); contra = m?[3]?.Trim();
        }
        return new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId), ["type"] = "sexual",
            ["intent"] = intent, ["reprPotential"] = repr, ["contraception"] = contra, ["message"] = msg,
        };
    }

    private static readonly Dictionary<int, (string Icon, string Label, string Level)> OutcomeMeta = new()
    {
        [1201] = ("💬", "Interaction outcome", "info"), [1202] = ("🤝", "Touch outcome", "warning"), [1203] = ("💕", "Sexual encounter", "danger"),
    };

    public static JsonObject? ParseOutcome(LogEntry e)
    {
        if (e.EventId is not { } eid || !OutcomeMeta.TryGetValue(eid, out var meta)) return null;
        var msg = e.Message ?? "";
        var o = new JsonObject
        {
            ["worldTime"] = e.WorldTimeText, ["eventId"] = eid, ["icon"] = meta.Icon, ["label"] = meta.Label, ["level"] = meta.Level,
            ["message"] = msg, ["partnerId"] = Lower(e.RelatedPersonId), ["interactionId"] = e.InteractionId, ["kind"] = "interaction_outcome",
        };
        if (e.Payload is { } p)
        {
            o["outcome"] = p.Raw("Result"); o["probability"] = p.Get("P") != null ? Js.Num(p.Get("P")) : null; o["touchLevel"] = p.Raw("Level");
        }
        else
        {
            o["outcome"] = Js.Match(msg, ResultRe)?[1]; o["probability"] = Js.Match(msg, ProbRe)?.D(1); o["touchLevel"] = Js.Match(msg, LevelRe)?[1];
        }
        return o;
    }

    public static JsonObject? ParseMisattribution(LogEntry e)
    {
        if (e.EventId != 1204) return null;
        double? stress, noise, penalty;
        if (e.Payload is { } p) { stress = Js.Num(p.Get("Stress")); noise = Js.Num(p.Get("Noise")); penalty = Js.Num(p.Get("Penalty")); }
        else
        {
            var m = Js.Match(e.Message, MisattribRe);
            stress = m?.D(1); noise = m?.D(2); penalty = m?.D(3);
        }
        return new JsonObject
        {
            ["eventId"] = 1204, ["worldTime"] = e.WorldTimeText, ["partnerId"] = Lower(e.RelatedPersonId),
            ["stress"] = stress, ["noise"] = noise, ["penalty"] = penalty,
            ["icon"] = "🌀", ["label"] = "Misattribution penalty", ["level"] = "warning", ["kind"] = "misattribution",
            ["interactionId"] = e.InteractionId,
        };
    }

    public static JsonObject? ParseObjectInteraction(LogEntry e)
    {
        if (e.EventId != 1205) return null;
        string? objectId, kind, locationId; double? utility;
        if (e.Payload is { } p) { objectId = p.S("ObjectId"); kind = p.S("Kind"); locationId = p.S("LocationId"); utility = Js.Num(p.Get("Utility")); }
        else
        {
            var m = Js.Match(e.Message, ObjInteractRe);
            if (m == null) return null;
            objectId = m[1]; kind = m[2]; locationId = m[3]; utility = m.D(4);
        }
        return new JsonObject
        {
            ["eventId"] = 1205, ["worldTime"] = e.WorldTimeText, ["objectId"] = objectId, ["kind"] = kind,
            ["locationId"] = locationId, ["utility"] = utility,
            ["icon"] = kind == "Take" ? "🤲" : kind == "Drop" ? "📤" : "🔧", ["label"] = $"{kind} {objectId}", ["level"] = "info",
        };
    }

    private static readonly Dictionary<string, string> MoveIcons = new()
    { ["Social"] = "🤝", ["Private"] = "🏠", ["Work"] = "⚒️", ["Rest"] = "🛏️", ["Public"] = "🌳" };

    public static JsonObject? ParseMoveAction(LogEntry e)
    {
        if (e.EventId != 1206) return null;
        string? action, from; double? utility;
        if (e.Payload is { } p) { action = p.S("Action"); from = p.S("From"); utility = Js.Num(p.Get("Utility")); }
        else
        {
            var m = Js.Match(e.Message, MoveActionRe);
            if (m == null) return null;
            action = m[1]; from = m[2]; utility = m.D(3);
        }
        var parts = (action ?? "").Split(':');
        var target = parts.Length > 1 ? parts[1] : action;
        return new JsonObject
        {
            ["eventId"] = 1206, ["worldTime"] = e.WorldTimeText, ["action"] = action, ["targetArea"] = target,
            ["fromLocation"] = from, ["utility"] = utility,
            ["icon"] = target != null && MoveIcons.TryGetValue(target, out var ic) ? ic : "🚶",
            ["label"] = $"Move → {target}", ["level"] = "secondary",
        };
    }

    public static JsonObject? ParseNormViolation(LogEntry e)
    {
        if (e.EventId is not (1207 or 1208 or 1209)) return null;
        var msg = e.Message ?? "";
        var wt = e.WorldTimeText;
        var p = e.Payload;
        switch (e.EventId)
        {
            case 1207:
            {
                string? kind; double? score, penalty;
                if (p != null) { kind = p.S("NormKind"); score = Js.Num(p.Get("Score")); penalty = Js.Num(p.Get("Penalty")); }
                else
                {
                    var m = Js.Match(msg, NormViolationRe);
                    if (m == null) return null;
                    kind = m[1]; score = m.D(2); penalty = m.D(3);
                }
                return new JsonObject
                {
                    ["eventId"] = 1207, ["worldTime"] = wt, ["normKind"] = kind, ["score"] = score, ["acceptancePenalty"] = penalty,
                    ["icon"] = "⚖️", ["label"] = $"Norm violated: {kind} (score={(score == null ? "NaN" : Js.Fmt(score.Value))})", ["level"] = "warning",
                };
            }
            case 1208:
            {
                string? kind; double? score; bool audience;
                if (p != null) { kind = p.S("NormKind"); score = Js.Num(p.Get("Score")); audience = Js.IsTrue(p.Get("HasAudience")); }
                else
                {
                    var m = Js.Match(msg, NormShameRe);
                    if (m == null) return null;
                    kind = m[1]; score = m.D(2); audience = m[3] == "true";
                }
                return new JsonObject
                {
                    ["eventId"] = 1208, ["worldTime"] = wt, ["normKind"] = kind, ["score"] = score, ["hasAudience"] = audience,
                    ["icon"] = "😳", ["label"] = $"Shame spike: {kind}", ["level"] = "danger",
                };
            }
            default:
            {
                string observer; string? reaction; double? score;
                if (p != null) { observer = Lower(p.S("ObserverId")); reaction = p.S("Reaction"); score = Js.Num(p.Get("Score")); }
                else
                {
                    var m = Js.Match(msg, NormObserverRe);
                    if (m == null) return null;
                    observer = m[1]!.ToLowerInvariant(); reaction = m[2]; score = m.D(3);
                }
                return new JsonObject
                {
                    ["eventId"] = 1209, ["worldTime"] = wt, ["observerId"] = observer, ["reaction"] = reaction, ["score"] = score,
                    ["icon"] = "👁️", ["label"] = $"Observer reaction: {reaction}", ["level"] = "info",
                };
            }
        }
    }

    public static JsonObject? ParseReachOutRouted(LogEntry e)
    {
        if (e.EventId != 10003) return null;
        string target, act, hasPrivacyText = ""; double? fam, trust, comfort, close, romantic; bool privacy;
        if (e.Payload is { } p)
        {
            target = Lower(p.S("TargetId")); act = p.S("Act")!;
            fam = Js.Num(p.Get("Familiarity")); trust = Js.Num(p.Get("Trust")); comfort = Js.Num(p.Get("Comfort"));
            close = Js.Num(p.Get("Closeness")); romantic = Js.Num(p.Get("Romantic")); privacy = Js.IsTrue(p.Get("HasPrivacy"));
        }
        else
        {
            var m = Js.Match(e.Message, ReachOutRe);
            if (m == null) return null;
            target = m[1]!.ToLowerInvariant(); act = m[2]!;
            fam = m.D(3); trust = m.D(4); comfort = m.D(5); close = m.D(6); romantic = m.D(7);
            privacy = m[8]!.ToLowerInvariant() == "true";
        }
        _ = hasPrivacyText;
        return new JsonObject
        {
            ["eventId"] = 10003, ["worldTime"] = e.WorldTimeText, ["partnerId"] = target, ["act"] = act,
            ["familiarity"] = fam, ["trust"] = trust, ["comfort"] = comfort, ["closeness"] = close, ["romantic"] = romantic,
            ["hasPrivacy"] = privacy, ["icon"] = "🗣️", ["label"] = $"Reached out: {act}", ["level"] = "info",
        };
    }

    public static JsonObject? ParseSceneMoveFailed(LogEntry e)
    {
        if (e.EventId is not (10001 or 10002)) return null;
        var p = e.Payload;
        if (e.EventId == 10001)
        {
            string? category;
            if (p != null) category = p.S("Category");
            else
            {
                var m = Js.Match(e.Message, MoveNeedRe);
                if (m == null) return null;
                category = m[1];
            }
            return new JsonObject
            {
                ["eventId"] = 10001, ["worldTime"] = e.WorldTimeText, ["category"] = category,
                ["icon"] = "⚠️", ["label"] = $"No location found for {category}", ["level"] = "warning", ["kind"] = "move_failed",
            };
        }
        string? type, from;
        if (p != null) { type = p.S("Type"); from = p.S("CurrentLocation"); }
        else
        {
            var m = Js.Match(e.Message, MoveReqRe);
            if (m == null) return null;
            type = m[1]; from = m[2]?.Trim();
        }
        return new JsonObject
        {
            ["eventId"] = 10002, ["worldTime"] = e.WorldTimeText, ["type"] = type, ["fromLocation"] = from,
            ["icon"] = "⚠️", ["label"] = $"No {type} location available", ["level"] = "warning", ["kind"] = "move_failed",
        };
    }

    private static readonly HashSet<string> MoveActions = new() { "MoveTo:Social", "MoveTo:Private", "MoveTo:Work", "MoveTo:Rest", "MoveTo:Public" };

    /// <summary>Groups context/move events into per-location visits with the actions that happened in them.</summary>
    public static (List<JsonObject> Sessions, JsonObject ByLocation) BuildLocationSessions(
        IEnumerable<JsonObject> contextEvents, IEnumerable<JsonObject> moveEvents, IEnumerable<JsonObject> actionEvents)
    {
        var all = contextEvents.Concat(moveEvents).OrderBy(x => x.S("worldTime") ?? "", StringComparer.Ordinal).ToList();

        var sessions = new List<JsonObject>();
        JsonObject? current = null;
        foreach (var ev in all)
        {
            var loc = ev.S("location") ?? ev.S("targetArea");
            if (loc == null) continue;
            var wt = ev.S("worldTime");
            if (current != null && current.S("location") == loc) current["lastSeen"] = wt;
            else
            {
                if (current != null) { current["end"] = wt; sessions.Add(current); }
                current = new JsonObject
                {
                    ["location"] = loc, ["start"] = wt, ["lastSeen"] = wt, ["end"] = null,
                    ["noise"] = ev.Raw("noise"), ["crowding"] = ev.Raw("crowding"),
                    ["actions"] = new JsonArray(), ["objectInteractions"] = new JsonArray(),
                };
            }
        }
        if (current != null) sessions.Add(current);

        foreach (var act in actionEvents)
        {
            var awt = act.S("worldTime");
            if (string.IsNullOrEmpty(awt)) continue;
            var sess = sessions.FirstOrDefault(s => Js.Cmp(awt, s.S("start")) >= 0 && (s.Get("end") == null || Js.Cmp(awt, s.S("end")) < 0));
            if (sess == null) continue;
            if (act.D("eventId") == 1205) sess.Arr("objectInteractions")!.Add(act.DeepClone());
            else if (!MoveActions.Contains(act.S("action") ?? "\0")) sess.Arr("actions")!.Add(act.DeepClone());
        }

        var byLocation = new JsonObject();
        foreach (var s in sessions)
        {
            var key = s.S("location")!;
            if (byLocation[key] is not JsonObject b) { b = new JsonObject { ["visits"] = 0, ["actions"] = 0, ["objInteractions"] = 0 }; byLocation[key] = b; }
            b["visits"] = b.D("visits") + 1;
            b["actions"] = b.D("actions") + s.Arr("actions")!.Count;
            b["objInteractions"] = b.D("objInteractions") + s.Arr("objectInteractions")!.Count;
        }
        return (sessions, byLocation);
    }
}
