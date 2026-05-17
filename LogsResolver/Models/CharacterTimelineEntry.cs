namespace LogsResolver.Models;

public sealed class CharacterTimelineEntry
{
    public long EventInstanceId { get; init; }

    public DateTimeOffset RealTimestamp { get; init; }

    public string WorldTimeText { get; init; }

    public string Level { get; init; }

    public string Category { get; init; }

    public int EventId { get; init; }

    public string Message { get; init; }

    public Guid? PersonId { get; init; }

    public Guid? RelatedPersonId { get; init; }

    public string? Subsystem { get; init; }

    public string? CorrelationId { get; init; }

    public string? InteractionId { get; init; }

    public string? DecisionId { get; init; }

    public string? TickKey { get; init; }

    public string Involvement { get; init; } = "Primary";
}
