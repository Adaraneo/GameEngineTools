namespace LogsResolver.Models;

public sealed class LogQueryResult
{
    public IReadOnlyList<ResolvedLogEvent> Items { get; init; }

    public int TotalMatches { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => TotalMatches == 0
        ? 0
        : (int)Math.Ceiling((double)TotalMatches / PageSize);
}
