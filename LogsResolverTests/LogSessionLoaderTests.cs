using System.Text.Json;
using LogsResolver.Models;
using LogsResolver.Services;

namespace LogsResolverTests;

[TestClass]
public sealed class LogSessionLoaderTests
{
    [TestMethod]
    public async Task LoadAsync_MirroredEventsWithDifferentLocationId_AddsInconsistencyDiagnostic()
    {
        var root = Path.Combine(Path.GetTempPath(), "LogsResolverTests", Guid.NewGuid().ToString("N"));
        try
        {
            var personId = Guid.NewGuid();
            var scopedRoot = Path.Combine(root, "Person", personId.ToString());
            Directory.CreateDirectory(scopedRoot);

            var globalEvent = CreateEvent(personId, "loc-a");
            var scopedEvent = CreateEvent(personId, "loc-b");
            await File.WriteAllTextAsync(Path.Combine(root, "Characters.jsonl"), JsonSerializer.Serialize(globalEvent) + Environment.NewLine);
            await File.WriteAllTextAsync(Path.Combine(scopedRoot, "Subsystem.jsonl"), JsonSerializer.Serialize(scopedEvent) + Environment.NewLine);

            var loader = new LogSessionLoader(
                new JsonLogSessionDiscoveryService(),
                new JsonLogFileReader(),
                new LogIntegrityAnalyzer());

            var result = await loader.LoadAsync(root);

            Assert.AreEqual(1, result.Events.Count);
            Assert.IsTrue(result.Diagnostics.Any(d => d.Kind == LogDiagnosticIssueKind.InconsistentMirrorEvent));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static object CreateEvent(Guid personId, string locationId)
        => new
        {
            EventInstanceId = 123L,
            RealTimestamp = new DateTimeOffset(2026, 4, 12, 10, 15, 22, TimeSpan.Zero),
            WorldTimeText = "0001-01-01 08:00:00",
            Level = "Information",
            Category = "Test.Category",
            EventId = 1001,
            Message = "Mirrored event",
            ExceptionType = (string?)null,
            ExceptionMessage = (string?)null,
            StackTrace = (string?)null,
            PersonId = personId,
            Subsystem = "Subsystem",
            CorrelationId = "corr-1",
            InteractionId = "interaction-1",
            DecisionId = "decision-1",
            RelatedPersonId = (Guid?)null,
            LocationId = locationId,
            TickKey = "42",
            IsScoped = true
        };
}
