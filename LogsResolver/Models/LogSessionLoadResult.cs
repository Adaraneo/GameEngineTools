namespace LogsResolver.Models;

public sealed class LogSessionLoadResult
{
    public required LogSessionDescriptor Session { get; init; }

    public required IReadOnlyList<ResolvedLogEvent> Events { get; init; }

    public required IReadOnlyList<LogDiagnosticIssue> Diagnostics { get; init; }

    public int JsonFileCount => Session.AllJsonLinesFiles.Count();

    public int TextFileCount => Session.AllTextLogFiles.Count();
}
