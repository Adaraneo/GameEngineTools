// Parsing/LogEntry.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogReader.Parsing;

/// <summary>One parsed JSONL log line (the PascalCase fields the engine writes).</summary>
public sealed class LogEntry
{
    public long? EventInstanceId { get; init; }
    public int? EventId { get; init; }
    public string? RealTimestamp { get; init; }
    public string? WorldTimeText { get; init; }
    public string? Level { get; init; }
    public string? Subsystem { get; init; }
    public string? Message { get; init; }
    public string? PersonId { get; init; }
    public string? RelatedPersonId { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }
    public string? InteractionId { get; init; }

    /// <summary>Structured payload of the event (null for older logs).</summary>
    public JsonObject? Payload { get; init; }

    public string PersonLower => (PersonId ?? "").ToLowerInvariant();
    public string RelatedLower => (RelatedPersonId ?? "").ToLowerInvariant();

    public static LogEntry? TryParse(string line)
    {
        var t = line.Trim();
        if (t.Length == 0) return null;
        JsonNode? node;
        try { node = JsonNode.Parse(t); } catch { return null; }
        return node is JsonObject o ? FromJson(o) : null;
    }

    public static LogEntry FromJson(JsonObject o) => new()
    {
        EventInstanceId = o.L("EventInstanceId"),
        EventId = (int?)o.D("EventId"),
        RealTimestamp = o.S("RealTimestamp"),
        WorldTimeText = o.S("WorldTimeText"),
        Level = o.S("Level"),
        Subsystem = o.S("Subsystem"),
        Message = o.S("Message"),
        PersonId = o.S("PersonId"),
        RelatedPersonId = o.S("RelatedPersonId"),
        ExceptionType = o.S("ExceptionType"),
        ExceptionMessage = o.S("ExceptionMessage"),
        InteractionId = o.S("InteractionId"),
        Payload = o.Obj("Payload"),
    };

    /// <summary>Rebuilds an entry from its compact projection (the structured payload is not kept).</summary>
    public static LogEntry FromCompact(JsonObject c) => new()
    {
        EventInstanceId = c.L("i"), RealTimestamp = c.S("rt"), WorldTimeText = c.S("wt"), Level = c.S("lv"),
        EventId = (int?)c.D("ev"), Subsystem = c.S("ss"), Message = c.S("msg"), PersonId = c.S("pid"),
        RelatedPersonId = c.S("rpid"), ExceptionType = c.S("exc"), ExceptionMessage = c.S("excm"),
    };

    /// <summary>Compact projection used by the summary / event-list endpoints.</summary>
    public JsonObject ToCompact() => new()
    {
        ["i"] = EventInstanceId,
        ["rt"] = RealTimestamp,
        ["wt"] = WorldTimeText,
        ["lv"] = Level,
        ["ev"] = EventId,
        ["ss"] = Subsystem,
        ["msg"] = Message,
        ["pid"] = PersonId,
        ["rpid"] = RelatedPersonId,
        ["exc"] = ExceptionType,
        ["excm"] = ExceptionMessage,
    };
}
