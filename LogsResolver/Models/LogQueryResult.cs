namespace LogsResolver.Models;

public sealed class LogQueryResult
{
    public required IReadOnlyList<ResolvedLogEvent> Items { get; init; }

    public required int TotalMatches { get; init; }

    public required int PageIndex { get; init; }

    public required int PageSize { get; init; }

    public int TotalPages => TotalMatches == 0
        ? 0
        : (int)Math.Ceiling((double)TotalMatches / PageSize);
}
