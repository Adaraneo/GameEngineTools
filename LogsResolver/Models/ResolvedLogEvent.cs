namespace LogsResolver.Models;

public sealed class ResolvedLogEvent
{
    public long EventInstanceId { get; init; }

    public DateTimeOffset RealTimestamp { get; init; }

    public string WorldTimeText { get; init; }

    public string Level { get; init; }

    public string Category { get; init; }

    public int EventId { get; init; }

    public string Message { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? StackTrace { get; init; }

    public Guid? PersonId { get; init; }

    public string? Subsystem { get; init; }

    public string? CorrelationId { get; init; }

    public string? InteractionId { get; init; }

    public string? DecisionId { get; init; }

    public Guid? RelatedPersonId { get; init; }

    public string? LocationId { get; init; }

    public string? TickKey { get; init; }

    public List<ResolvedLogEventSource> Sources { get; } = new(capacity: 2);

    public bool HasException => !string.IsNullOrWhiteSpace(ExceptionType);

    public int SourceCount => Sources.Count;

    public string SourceKinds => string.Join(", ", Sources.Select(s => s.SourceKind).Distinct());

    public bool HasGlobalSource => Sources.Any(s => s.SourceKind == LogSourceKind.Global);

    public bool HasScopedSource => Sources.Any(s => s.SourceKind == LogSourceKind.Scoped);
}
