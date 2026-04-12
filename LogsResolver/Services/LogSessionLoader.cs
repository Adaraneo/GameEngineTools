using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class LogSessionLoader
{
    private readonly JsonLogSessionDiscoveryService _discoveryService;
    private readonly JsonLogFileReader _reader;
    private readonly LogIntegrityAnalyzer _analyzer;

    public LogSessionLoader(JsonLogSessionDiscoveryService discoveryService, JsonLogFileReader reader, LogIntegrityAnalyzer analyzer)
    {
        _discoveryService = discoveryService;
        _reader = reader;
        _analyzer = analyzer;
    }

    public Task<LogSessionLoadResult> LoadAsync(string selectedPath)
        => Task.Run(() => Load(selectedPath));

    private LogSessionLoadResult Load(string selectedPath)
    {
        var diagnostics = new List<LogDiagnosticIssue>();
        var session = _discoveryService.Discover(selectedPath);
        var eventsById = new Dictionary<long, ResolvedLogEvent>();

        foreach (var file in session.AllJsonLinesFiles)
        {
            foreach (var record in _reader.Read(file, diagnostics))
            {
                if (!TryMap(record, diagnostics, out var parsed))
                {
                    continue;
                }

                var source = new ResolvedLogEventSource
                {
                    FilePath = record.File.FilePath,
                    SourceKind = record.File.SourceKind,
                    PersonId = record.File.PersonId,
                    Subsystem = record.File.Subsystem,
                    LineNumber = record.LineNumber
                };

                if (eventsById.TryGetValue(parsed.EventInstanceId, out var existing))
                {
                    if (!CoreFieldsMatch(existing, parsed))
                    {
                        diagnostics.Add(new LogDiagnosticIssue
                        {
                            Kind = LogDiagnosticIssueKind.InconsistentMirrorEvent,
                            Title = "Inconsistent mirrored event",
                            Description = $"Event {parsed.EventInstanceId} differs between mirrored JSONL records. The first record was kept.",
                            AffectedFile = record.File.FilePath,
                            LineNumber = record.LineNumber,
                            EventInstanceIds = new[] { parsed.EventInstanceId }
                        });
                    }

                    existing.Sources.Add(source);
                }
                else
                {
                    parsed.Sources.Add(source);
                    eventsById.Add(parsed.EventInstanceId, parsed);
                }
            }
        }

        var events = eventsById.Values.OrderBy(e => e.RealTimestamp).ThenBy(e => e.EventInstanceId).ToList();
        diagnostics.AddRange(_analyzer.Analyze(session, events));

        if (events.Count == 0 && session.AllTextLogFiles.Any())
        {
            diagnostics.Add(new LogDiagnosticIssue
            {
                Kind = LogDiagnosticIssueKind.StructuredJsonUnavailable,
                Title = "Structured JSONL data unavailable",
                Description = "Text log files were found, but no JSONL events were loaded. Structured browsing requires JSONL; use raw file inspection for text logs."
            });
        }

        return new LogSessionLoadResult
        {
            Session = session,
            Events = events,
            Diagnostics = diagnostics
        };
    }

    private static bool TryMap(JsonLogReadRecord record, IList<LogDiagnosticIssue> diagnostics, out ResolvedLogEvent resolved)
    {
        var dto = record.Entry;
        var missing = new List<string>();
        if (dto.EventInstanceId <= 0) missing.Add(nameof(dto.EventInstanceId));
        if (dto.RealTimestamp == default) missing.Add(nameof(dto.RealTimestamp));
        if (string.IsNullOrWhiteSpace(dto.WorldTimeText)) missing.Add(nameof(dto.WorldTimeText));
        if (string.IsNullOrWhiteSpace(dto.Level)) missing.Add(nameof(dto.Level));
        if (string.IsNullOrWhiteSpace(dto.Category)) missing.Add(nameof(dto.Category));
        if (dto.Message is null) missing.Add(nameof(dto.Message));

        if (missing.Count > 0)
        {
            diagnostics.Add(new LogDiagnosticIssue
            {
                Kind = LogDiagnosticIssueKind.MissingRequiredField,
                Title = "Missing required JSONL field",
                Description = $"Missing or invalid required fields: {string.Join(", ", missing)}.",
                AffectedFile = record.File.FilePath,
                LineNumber = record.LineNumber,
                EventInstanceIds = dto.EventInstanceId > 0 ? new[] { dto.EventInstanceId } : Array.Empty<long>()
            });
            resolved = null!;
            return false;
        }

        resolved = new ResolvedLogEvent
        {
            EventInstanceId = dto.EventInstanceId,
            RealTimestamp = dto.RealTimestamp,
            WorldTimeText = dto.WorldTimeText!,
            Level = dto.Level!,
            Category = dto.Category!,
            EventId = dto.EventId,
            Message = dto.Message!,
            ExceptionType = dto.ExceptionType,
            ExceptionMessage = dto.ExceptionMessage,
            StackTrace = dto.StackTrace,
            PersonId = dto.PersonId,
            Subsystem = dto.Subsystem,
            CorrelationId = dto.CorrelationId,
            InteractionId = dto.InteractionId,
            DecisionId = dto.DecisionId,
            RelatedPersonId = dto.RelatedPersonId,
            LocationId = dto.LocationId,
            TickKey = dto.TickKey
        };

        return true;
    }

    private static bool CoreFieldsMatch(ResolvedLogEvent a, ResolvedLogEvent b)
        => a.RealTimestamp == b.RealTimestamp
           && string.Equals(a.Level, b.Level, StringComparison.Ordinal)
           && string.Equals(a.Category, b.Category, StringComparison.Ordinal)
           && a.EventId == b.EventId
           && string.Equals(a.Message, b.Message, StringComparison.Ordinal)
           && a.PersonId == b.PersonId
           && string.Equals(a.Subsystem, b.Subsystem, StringComparison.Ordinal)
           && string.Equals(a.CorrelationId, b.CorrelationId, StringComparison.Ordinal)
           && string.Equals(a.InteractionId, b.InteractionId, StringComparison.Ordinal)
           && string.Equals(a.DecisionId, b.DecisionId, StringComparison.Ordinal)
           && a.RelatedPersonId == b.RelatedPersonId
           && string.Equals(a.TickKey, b.TickKey, StringComparison.Ordinal);
}
