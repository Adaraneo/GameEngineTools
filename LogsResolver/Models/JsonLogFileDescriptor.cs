namespace LogsResolver.Models;

public sealed class JsonLogFileDescriptor
{
    public required string FilePath { get; init; }

    public required LogSourceKind SourceKind { get; init; }

    public Guid? PersonId { get; init; }

    public string? Subsystem { get; init; }
}
