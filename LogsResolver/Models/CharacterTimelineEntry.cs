namespace LogsResolver.Models;

public sealed class CharacterTimelineEntry
{
    public required long EventInstanceId { get; init; }

    public required DateTimeOffset RealTimestamp { get; init; }

    public required string WorldTimeText { get; init; }

    public required string Level { get; init; }

    public required string Category { get; init; }

    public required int EventId { get; init; }

    public required string Message { get; init; }

    public Guid? PersonId { get; init; }

    public Guid? RelatedPersonId { get; init; }

    public string? Subsystem { get; init; }

    public string? CorrelationId { get; init; }

    public string? InteractionId { get; init; }

    public string? DecisionId { get; init; }

    public string? TickKey { get; init; }

    public string Involvement { get; init; } = "Primary";
}
