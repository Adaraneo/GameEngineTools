namespace LogsResolver.Models;

public sealed class LogSessionLoadResult
{
    public LogSessionDescriptor Session { get; init; }

    public IReadOnlyList<ResolvedLogEvent> Events { get; init; }

    public IReadOnlyList<LogDiagnosticIssue> Diagnostics { get; init; }

    public int JsonFileCount => Session.AllJsonLinesFiles.Count();

    public int TextFileCount => Session.AllTextLogFiles.Count();
}
