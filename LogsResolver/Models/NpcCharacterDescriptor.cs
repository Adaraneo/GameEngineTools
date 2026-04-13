namespace LogsResolver.Models;

public sealed class NpcCharacterDescriptor
{
    public required Guid PersonId { get; init; }

    public required string FilePath { get; init; }

    public string? DisplayName { get; init; }

    public string? BirthDateText { get; init; }

    public string? BiologyText { get; init; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName)
        ? PersonId.ToString()
        : $"{DisplayName} ({PersonId})";
}
