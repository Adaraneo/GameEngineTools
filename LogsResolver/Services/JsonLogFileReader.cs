using LogsResolver.Models;
using System.Text.Json;

namespace LogsResolver.Services;

public sealed class JsonLogFileReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IEnumerable<JsonLogReadRecord> Read(
        JsonLogFileDescriptor file,
        IList<LogDiagnosticIssue> diagnostics,
        IProgress<LogLoadProgress>? progress = null)
    {
        if (!File.Exists(file.FilePath))
        {
            yield break;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(file.FilePath))
        {
            lineNumber++;
            if (lineNumber % 10_000 == 0)
            {
                progress?.Report(new LogLoadProgress
                {
                    Phase = "Reading JSONL",
                    FilePath = file.FilePath,
                    LineNumber = lineNumber
                });
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            CharacterLogEntryDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<CharacterLogEntryDto>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                AddMalformedDiagnostic(diagnostics, file.FilePath, lineNumber, ex.Message);
                continue;
            }

            if (dto is null)
            {
                AddMalformedDiagnostic(diagnostics, file.FilePath, lineNumber, "The line deserialized to null.");
                continue;
            }

            yield return new JsonLogReadRecord(dto, file, lineNumber);
        }
    }

    private static void AddMalformedDiagnostic(IList<LogDiagnosticIssue> diagnostics, string filePath, int lineNumber, string message)
        => diagnostics.Add(new LogDiagnosticIssue
        {
            Kind = LogDiagnosticIssueKind.MalformedJsonLine,
            Title = "Malformed JSONL line",
            Description = message,
            AffectedFile = filePath,
            LineNumber = lineNumber
        });
}

public sealed record JsonLogReadRecord(
    CharacterLogEntryDto Entry,
    JsonLogFileDescriptor File,
    int LineNumber);

public sealed class CharacterLogEntryDto
{
    public long EventInstanceId { get; set; }

    public DateTimeOffset RealTimestamp { get; set; }

    public string? WorldTimeText { get; set; }

    public string? Level { get; set; }

    public string? Category { get; set; }

    public int EventId { get; set; }

    public string? Message { get; set; }

    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    public string? StackTrace { get; set; }

    public Guid? PersonId { get; set; }

    public string? Subsystem { get; set; }

    public string? CorrelationId { get; set; }

    public string? InteractionId { get; set; }

    public string? DecisionId { get; set; }

    public Guid? RelatedPersonId { get; set; }

    public string? LocationId { get; set; }

    public string? TickKey { get; set; }

    public bool IsScoped { get; set; }
}
