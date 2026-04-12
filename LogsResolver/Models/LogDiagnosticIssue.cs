namespace LogsResolver.Models;

public sealed class LogDiagnosticIssue
{
    public required LogDiagnosticIssueKind Kind { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public string? AffectedFile { get; init; }

    public int? LineNumber { get; init; }

    public IReadOnlyList<long> EventInstanceIds { get; init; } = Array.Empty<long>();
}
