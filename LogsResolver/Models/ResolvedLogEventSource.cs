namespace LogsResolver.Models;

public sealed class ResolvedLogEventSource
{
    public required string FilePath { get; init; }

    public required LogSourceKind SourceKind { get; init; }

    public Guid? PersonId { get; init; }

    public string? Subsystem { get; init; }

    public int? LineNumber { get; init; }

    public string DisplayName
        => SourceKind == LogSourceKind.Global
            ? $"Global: {Path.GetFileName(FilePath)}"
            : $"Scoped: {PersonId} / {Subsystem} ({Path.GetFileName(FilePath)})";
}
