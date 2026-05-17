namespace LogsResolver.Models;

public sealed class LogLoadProgress
{
    public string Phase { get; init; }

    public string? FilePath { get; init; }

    public int? FileIndex { get; init; }

    public int? FileCount { get; init; }

    public int? LineNumber { get; init; }

    public int? EventCount { get; init; }

    public string DisplayText
    {
        get
        {
            var parts = new List<string> { Phase };

            if (FileIndex.HasValue && FileCount.HasValue)
            {
                parts.Add($"file {FileIndex.Value}/{FileCount.Value}");
            }

            if (LineNumber.HasValue)
            {
                parts.Add($"line {LineNumber.Value:N0}");
            }

            if (EventCount.HasValue)
            {
                parts.Add($"events {EventCount.Value:N0}");
            }

            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                parts.Add(Path.GetFileName(FilePath));
            }

            return string.Join(" | ", parts);
        }
    }
}
