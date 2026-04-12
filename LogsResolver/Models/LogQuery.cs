namespace LogsResolver.Models;

public sealed class LogQuery
{
    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    public string? Level { get; set; }

    public string? Category { get; set; }

    public int? EventId { get; set; }

    public Guid? PersonId { get; set; }

    public string? Subsystem { get; set; }

    public string? CorrelationId { get; set; }

    public string? InteractionId { get; set; }

    public string? DecisionId { get; set; }

    public Guid? RelatedPersonId { get; set; }

    public string? TickKey { get; set; }

    public string? FreeText { get; set; }

    public bool ExceptionsOnly { get; set; }

    public bool GlobalOnly { get; set; }

    public bool ScopedOnly { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; } = 2_000;
}
