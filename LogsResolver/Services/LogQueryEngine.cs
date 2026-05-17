using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class LogQueryEngine
{
    private IReadOnlyList<ResolvedLogEvent> _events = Array.Empty<ResolvedLogEvent>();
    private readonly Dictionary<long, ResolvedLogEvent> _byEventInstanceId = new();

    public void SetEvents(IReadOnlyList<ResolvedLogEvent> events)
    {
        _events = events;
        _byEventInstanceId.Clear();

        foreach (var ev in events)
        {
            _byEventInstanceId[ev.EventInstanceId] = ev;
        }
    }

    public LogQueryResult Query(LogQuery query)
    {
        IEnumerable<ResolvedLogEvent> candidates = _events;

        if (query.From.HasValue) candidates = candidates.Where(e => e.RealTimestamp >= query.From.Value);
        if (query.To.HasValue) candidates = candidates.Where(e => e.RealTimestamp <= query.To.Value);
        if (!string.IsNullOrWhiteSpace(query.Level)) candidates = candidates.Where(e => Contains(e.Level, query.Level));
        if (!string.IsNullOrWhiteSpace(query.Category)) candidates = candidates.Where(e => Contains(e.Category, query.Category));
        if (query.EventId.HasValue) candidates = candidates.Where(e => e.EventId == query.EventId.Value);
        if (query.PersonId.HasValue) candidates = candidates.Where(e => e.PersonId == query.PersonId.Value);
        if (!string.IsNullOrWhiteSpace(query.Subsystem)) candidates = candidates.Where(e => Contains(e.Subsystem, query.Subsystem));
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) candidates = candidates.Where(e => Contains(e.CorrelationId, query.CorrelationId));
        if (!string.IsNullOrWhiteSpace(query.InteractionId)) candidates = candidates.Where(e => Contains(e.InteractionId, query.InteractionId));
        if (!string.IsNullOrWhiteSpace(query.DecisionId)) candidates = candidates.Where(e => Contains(e.DecisionId, query.DecisionId));
        if (query.RelatedPersonId.HasValue) candidates = candidates.Where(e => e.RelatedPersonId == query.RelatedPersonId.Value);
        if (!string.IsNullOrWhiteSpace(query.TickKey)) candidates = candidates.Where(e => Contains(e.TickKey, query.TickKey));
        if (query.ExceptionsOnly) candidates = candidates.Where(e => e.HasException);
        if (query.GlobalOnly) candidates = candidates.Where(e => e.HasGlobalSource && !e.HasScopedSource);
        if (query.ScopedOnly) candidates = candidates.Where(e => e.HasScopedSource);
        if (!string.IsNullOrWhiteSpace(query.FreeText))
        {
            candidates = candidates.Where(e =>
                Contains(e.Message, query.FreeText)
                || Contains(e.Category, query.FreeText)
                || Contains(e.ExceptionMessage, query.FreeText)
                || Contains(e.StackTrace, query.FreeText));
        }

        var pageSize = Math.Clamp(query.PageSize, 100, 100_000);
        var pageIndex = Math.Max(0, query.PageIndex);
        var skip = pageIndex * pageSize;
        var totalMatches = 0;
        var pageItems = new List<ResolvedLogEvent>(Math.Min(pageSize, 4096));

        foreach (var candidate in candidates)
        {
            if (totalMatches >= skip && pageItems.Count < pageSize)
            {
                pageItems.Add(candidate);
            }

            totalMatches++;
        }

        if (skip >= totalMatches && totalMatches > 0)
        {
            pageIndex = Math.Max(0, (int)Math.Ceiling((double)totalMatches / pageSize) - 1);
            skip = pageIndex * pageSize;
            pageItems.Clear();

            var index = 0;
            foreach (var candidate in candidates)
            {
                if (index >= skip && pageItems.Count < pageSize)
                {
                    pageItems.Add(candidate);
                }

                index++;
            }
        }

        return new LogQueryResult
        {
            Items = pageItems,
            TotalMatches = totalMatches,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }

    public ResolvedLogEvent? GetByEventInstanceId(long eventInstanceId)
        => _byEventInstanceId.TryGetValue(eventInstanceId, out var ev) ? ev : null;

    private static bool Contains(string? value, string? filter)
        => string.IsNullOrWhiteSpace(filter)
           || (!string.IsNullOrWhiteSpace(value) && value.Contains(filter, StringComparison.OrdinalIgnoreCase));
}
