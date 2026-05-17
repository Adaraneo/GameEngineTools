namespace LogsResolver.Models;

public sealed class JsonLogFileDescriptor
{
    public string FilePath { get; init; }

    public LogSourceKind SourceKind { get; init; }

    public Guid? PersonId { get; init; }

    public string? Subsystem { get; init; }
}
