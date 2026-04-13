using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class LogIntegrityAnalyzer
{
    public IReadOnlyList<LogDiagnosticIssue> Analyze(LogSessionDescriptor session, IReadOnlyList<ResolvedLogEvent> events)
    {
        var issues = new List<LogDiagnosticIssue>();

        foreach (var ev in events)
        {
            if (ev.HasScopedSource && !ev.HasGlobalSource)
            {
                issues.Add(new LogDiagnosticIssue
                {
                    Kind = LogDiagnosticIssueKind.OrphanScopedEvent,
                    Title = "Scoped event has no global source",
                    Description = $"Event {ev.EventInstanceId} appears in scoped output but not in the global JSONL file.",
                    EventInstanceIds = new[] { ev.EventInstanceId }
                });
            }

            if (ev.PersonId.HasValue && ev.HasGlobalSource && session.GlobalJsonLinesPath is not null && !ev.HasScopedSource)
            {
                issues.Add(new LogDiagnosticIssue
                {
                    Kind = LogDiagnosticIssueKind.MissingScopedMirror,
                    Title = "Scoped metadata without scoped mirror",
                    Description = $"Event {ev.EventInstanceId} has scoped metadata but no scoped JSONL source. This is expected only when mirror mode was GlobalOnly.",
                    EventInstanceIds = new[] { ev.EventInstanceId }
                });
            }

            var duplicateSources = ev.Sources
                .GroupBy(s => (s.FilePath, s.LineNumber))
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateSources.Count > 0)
            {
                issues.Add(new LogDiagnosticIssue
                {
                    Kind = LogDiagnosticIssueKind.SuspiciousSourceDuplication,
                    Title = "Duplicate source record",
                    Description = $"Event {ev.EventInstanceId} has repeated source records for the same file and line.",
                    EventInstanceIds = new[] { ev.EventInstanceId }
                });
            }
        }

        foreach (var source in events.SelectMany(e => e.Sources).Where(s => s.SourceKind == LogSourceKind.Scoped))
        {
            if (string.IsNullOrWhiteSpace(source.Subsystem))
            {
                issues.Add(new LogDiagnosticIssue
                {
                    Kind = LogDiagnosticIssueKind.EmptySubsystemOnScopedFile,
                    Title = "Empty subsystem on scoped file",
                    Description = "A scoped JSONL source did not resolve a subsystem from its file name.",
                    AffectedFile = source.FilePath,
                    LineNumber = source.LineNumber
                });
            }
        }

        foreach (var ev in events)
        {
            foreach (var source in ev.Sources.Where(s => s.SourceKind == LogSourceKind.Scoped && s.PersonId.HasValue && ev.PersonId.HasValue && s.PersonId != ev.PersonId))
            {
                issues.Add(new LogDiagnosticIssue
                {
                    Kind = LogDiagnosticIssueKind.PersonIdMismatch,
                    Title = "Person id mismatch",
                    Description = $"Event {ev.EventInstanceId} payload person id {ev.PersonId} does not match scoped file person id {source.PersonId}.",
                    AffectedFile = source.FilePath,
                    LineNumber = source.LineNumber,
                    EventInstanceIds = new[] { ev.EventInstanceId }
                });
            }
        }

        return issues;
    }
}
