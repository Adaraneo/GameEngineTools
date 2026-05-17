namespace LogsResolver.Models;

public sealed class LogDiagnosticIssue
{
    public LogDiagnosticIssueKind Kind { get; init; }

    public string Title { get; init; }

    public string Description { get; init; }

    public string? AffectedFile { get; init; }

    public int? LineNumber { get; init; }

    public IReadOnlyList<long> EventInstanceIds { get; init; } = Array.Empty<long>();
}
