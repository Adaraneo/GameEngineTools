using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class LogQueryEngine
{
    private IReadOnlyList<ResolvedLogEvent> _events = Array.Empty<ResolvedLogEvent>();
    private readonly Dictionary<long, ResolvedLogEvent> _byEventId = new();
    private readonly Dictionary<Guid, List<ResolvedLogEvent>> _byPerson = new();
    private readonly Dictionary<string, List<ResolvedLogEvent>> _bySubsystem = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ResolvedLogEvent>> _byCorrelation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ResolvedLogEvent>> _byInteraction = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ResolvedLogEvent>> _byDecision = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ResolvedLogEvent>> _byCategory = new(StringComparer.OrdinalIgnoreCase);

    public void SetEvents(IReadOnlyList<ResolvedLogEvent> events)
    {
        _events = events;
        _byEventId.Clear();
        _byPerson.Clear();
        _bySubsystem.Clear();
        _byCorrelation.Clear();
        _byInteraction.Clear();
        _byDecision.Clear();
        _byCategory.Clear();

        foreach (var ev in events)
        {
            _byEventId[ev.EventInstanceId] = ev;
            AddToIndex(_byCategory, ev.Category, ev);
            if (ev.PersonId.HasValue) AddToIndex(_byPerson, ev.PersonId.Value, ev);
            AddToStringIndex(_bySubsystem, ev.Subsystem, ev);
            AddToStringIndex(_byCorrelation, ev.CorrelationId, ev);
            AddToStringIndex(_byInteraction, ev.InteractionId, ev);
            AddToStringIndex(_byDecision, ev.DecisionId, ev);
        }
    }

    public IReadOnlyList<ResolvedLogEvent> Query(LogQuery query)
    {
        IEnumerable<ResolvedLogEvent> candidates = SelectSmallestCandidateSet(query);

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

        return candidates.OrderBy(e => e.RealTimestamp).ThenBy(e => e.EventInstanceId).ToList();
    }

    public ResolvedLogEvent? GetByEventInstanceId(long eventInstanceId)
        => _byEventId.TryGetValue(eventInstanceId, out var ev) ? ev : null;

    private IEnumerable<ResolvedLogEvent> SelectSmallestCandidateSet(LogQuery query)
    {
        var sets = new List<IReadOnlyList<ResolvedLogEvent>>();
        if (query.PersonId.HasValue && _byPerson.TryGetValue(query.PersonId.Value, out var byPerson)) sets.Add(byPerson);
        if (!string.IsNullOrWhiteSpace(query.Subsystem) && _bySubsystem.TryGetValue(query.Subsystem, out var bySubsystem)) sets.Add(bySubsystem);
        if (!string.IsNullOrWhiteSpace(query.CorrelationId) && _byCorrelation.TryGetValue(query.CorrelationId, out var byCorrelation)) sets.Add(byCorrelation);
        if (!string.IsNullOrWhiteSpace(query.InteractionId) && _byInteraction.TryGetValue(query.InteractionId, out var byInteraction)) sets.Add(byInteraction);
        if (!string.IsNullOrWhiteSpace(query.DecisionId) && _byDecision.TryGetValue(query.DecisionId, out var byDecision)) sets.Add(byDecision);
        if (!string.IsNullOrWhiteSpace(query.Category) && _byCategory.TryGetValue(query.Category, out var byCategory)) sets.Add(byCategory);

        return sets.Count == 0 ? _events : sets.OrderBy(s => s.Count).First();
    }

    private static bool Contains(string? value, string? filter)
        => string.IsNullOrWhiteSpace(filter)
           || (!string.IsNullOrWhiteSpace(value) && value.Contains(filter, StringComparison.OrdinalIgnoreCase));

    private static void AddToIndex<TKey>(Dictionary<TKey, List<ResolvedLogEvent>> dictionary, TKey key, ResolvedLogEvent ev)
        where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = new List<ResolvedLogEvent>();
            dictionary.Add(key, list);
        }

        list.Add(ev);
    }

    private static void AddToStringIndex(Dictionary<string, List<ResolvedLogEvent>> dictionary, string? key, ResolvedLogEvent ev)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        AddToIndex(dictionary, key, ev);
    }
}
