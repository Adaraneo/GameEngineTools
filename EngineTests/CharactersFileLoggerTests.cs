using System.Text.Json;
using GameEngineTools.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EngineTests;

[TestClass]
public sealed class CharactersFileLoggerTests
{
    [TestMethod]
    public void ScopedEvent_WritesTextAndJsonMirrors_WithSameEventInstanceIdAndMetadata()
    {
        using var fixture = LoggerFixture.Create(opt =>
        {
            opt.WriteTextLogs = true;
            opt.WriteJsonLines = true;
            opt.MirrorMode = CharactersLogMirrorMode.GlobalAndScoped;
        });

        var personId = Guid.NewGuid();
        var relatedId = Guid.NewGuid();
        var logger = fixture.CreateLogger("Test.Category");

        using (logger.BeginCharacterScope(
            personId,
            "Subsystem",
            correlationId: "corr-1",
            interactionId: "interaction-1",
            decisionId: "decision-1",
            relatedPersonId: relatedId,
            locationId: "loc-1",
            tickKey: "42"))
        {
            logger.LogInformation(new EventId(1001), "Selected {Action}", "Walk");
        }

        fixture.FlushAll();

        var globalText = ReadAllTextShared(Path.Combine(fixture.LogsRoot, "Characters.log"));
        var scopedText = ReadAllTextShared(Path.Combine(fixture.LogsRoot, "Person", personId.ToString(), "Subsystem.log"));
        Assert.IsTrue(globalText.Contains("[W:test-world]"));
        Assert.IsTrue(globalText.Contains("[Seq:1]"));
        Assert.IsTrue(globalText.Contains("[INF]"));
        Assert.IsTrue(globalText.Contains($"[P:{personId}]"));
        Assert.IsTrue(globalText.Contains("[S:Subsystem]"));
        Assert.IsTrue(globalText.Contains("[Corr:corr-1]"));
        Assert.IsTrue(globalText.Contains($"[Rel:{relatedId}]"));
        Assert.IsTrue(globalText.Contains("[Tick:42]"));
        Assert.IsTrue(scopedText.Contains("Selected Walk"));

        using var globalJson = JsonDocument.Parse(ReadAllTextShared(Path.Combine(fixture.LogsRoot, "Characters.jsonl")));
        using var scopedJson = JsonDocument.Parse(ReadAllTextShared(Path.Combine(fixture.LogsRoot, "Person", personId.ToString(), "Subsystem.jsonl")));
        Assert.AreEqual(
            globalJson.RootElement.GetProperty("EventInstanceId").GetInt64(),
            scopedJson.RootElement.GetProperty("EventInstanceId").GetInt64());
        Assert.AreEqual(personId, globalJson.RootElement.GetProperty("PersonId").GetGuid());
        Assert.AreEqual("Subsystem", globalJson.RootElement.GetProperty("Subsystem").GetString());
        Assert.AreEqual("corr-1", globalJson.RootElement.GetProperty("CorrelationId").GetString());
        Assert.AreEqual("interaction-1", globalJson.RootElement.GetProperty("InteractionId").GetString());
        Assert.AreEqual("decision-1", globalJson.RootElement.GetProperty("DecisionId").GetString());
        Assert.AreEqual(relatedId, globalJson.RootElement.GetProperty("RelatedPersonId").GetGuid());
        Assert.AreEqual("loc-1", globalJson.RootElement.GetProperty("LocationId").GetString());
        Assert.AreEqual("42", globalJson.RootElement.GetProperty("TickKey").GetString());
    }

    [TestMethod]
    public void NonScopedEvent_WritesOnlyGlobalOutput()
    {
        using var fixture = LoggerFixture.Create();
        var logger = fixture.CreateLogger("Test.Category");

        logger.LogInformation("Global only");
        fixture.FlushAll();

        Assert.IsTrue(File.Exists(Path.Combine(fixture.LogsRoot, "Characters.log")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.LogsRoot, "Characters.jsonl")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.LogsRoot, "Person")));
    }

    [TestMethod]
    public void GlobalOnlyMode_SuppressesScopedMirrorFiles()
    {
        using var fixture = LoggerFixture.Create(opt => opt.MirrorMode = CharactersLogMirrorMode.GlobalOnly);
        var personId = Guid.NewGuid();
        var logger = fixture.CreateLogger("Test.Category");

        using (logger.BeginCharacterScope(personId, "Subsystem"))
        {
            logger.LogInformation("Scoped but global only");
        }

        fixture.FlushAll();

        Assert.IsTrue(File.Exists(Path.Combine(fixture.LogsRoot, "Characters.log")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.LogsRoot, "Person")));
    }

    [TestMethod]
    public void SubsystemFileName_IsSanitized()
    {
        using var fixture = LoggerFixture.Create();
        var personId = Guid.NewGuid();
        var logger = fixture.CreateLogger("Test.Category");

        using (logger.BeginCharacterScope(personId, "Bad:Name"))
        {
            logger.LogInformation("Sanitized");
        }

        fixture.FlushAll();

        Assert.IsTrue(File.Exists(Path.Combine(fixture.LogsRoot, "Person", personId.ToString(), "Bad_Name.log")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.LogsRoot, "Person", personId.ToString(), "Bad_Name.jsonl")));
    }

    [TestMethod]
    public void ExceptionInformation_IsSerializedIntoJsonLines()
    {
        using var fixture = LoggerFixture.Create();
        var logger = fixture.CreateLogger("Test.Category");
        var ex = CreateException();

        logger.LogError(ex, "Failed");
        fixture.FlushAll();

        using var json = JsonDocument.Parse(ReadAllTextShared(Path.Combine(fixture.LogsRoot, "Characters.jsonl")));
        Assert.AreEqual(typeof(InvalidOperationException).FullName, json.RootElement.GetProperty("ExceptionType").GetString());
        Assert.AreEqual("boom", json.RootElement.GetProperty("ExceptionMessage").GetString());
        Assert.IsTrue(json.RootElement.GetProperty("StackTrace").GetString()?.Contains(nameof(CreateException)) == true);
    }

    [TestMethod]
    public void OutputFlags_AreRespected()
    {
        using var jsonOnly = LoggerFixture.Create(opt => opt.WriteTextLogs = false);
        jsonOnly.CreateLogger("Test.Category").LogInformation("Json only");
        jsonOnly.FlushAll();
        Assert.IsFalse(File.Exists(Path.Combine(jsonOnly.LogsRoot, "Characters.log")));
        Assert.IsTrue(File.Exists(Path.Combine(jsonOnly.LogsRoot, "Characters.jsonl")));

        using var textOnly = LoggerFixture.Create(opt => opt.WriteJsonLines = false);
        textOnly.CreateLogger("Test.Category").LogInformation("Text only");
        textOnly.FlushAll();
        Assert.IsTrue(File.Exists(Path.Combine(textOnly.LogsRoot, "Characters.log")));
        Assert.IsFalse(File.Exists(Path.Combine(textOnly.LogsRoot, "Characters.jsonl")));
    }

    [TestMethod]
    public void FlushAllAndDispose_CloseWritersSafely()
    {
        var fixture = LoggerFixture.Create();
        fixture.CreateLogger("Test.Category").LogInformation("Flush");
        fixture.FlushAll();
        var path = Path.Combine(fixture.LogsRoot, "Characters.log");
        Assert.IsTrue(ReadAllTextShared(path).Contains("Flush"));

        fixture.DisposeProvider();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.IsTrue(stream.Length > 0);
        }

        fixture.Dispose();
    }

    private static Exception CreateException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class LoggerFixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private bool _providerDisposed;

        private LoggerFixture(ServiceProvider provider, string logsRoot)
        {
            _provider = provider;
            LogsRoot = logsRoot;
        }

        public string LogsRoot { get; }

        public static LoggerFixture Create(Action<CharactersFileLoggerOptions>? configure = null)
        {
            var logsRoot = Path.Combine(Path.GetTempPath(), "GameEngineToolsLoggerTests", Guid.NewGuid().ToString("N"));
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddCharactersFile(opt =>
                {
                    opt.LogsDirectoryPath = logsRoot;
                    opt.MinLevel = LogLevel.Trace;
                    opt.WorldTimeTextAccessor = () => "test-world";
                    configure?.Invoke(opt);
                });
            });

            return new LoggerFixture(services.BuildServiceProvider(), logsRoot);
        }

        public ILogger CreateLogger(string category)
            => _provider.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        public void FlushAll()
            => _provider.GetRequiredService<ICharactersLogControl>().FlushAll();

        public void DisposeProvider()
        {
            if (_providerDisposed)
            {
                return;
            }

            _provider.Dispose();
            _providerDisposed = true;
        }

        public void Dispose()
        {
            DisposeProvider();

            if (Directory.Exists(LogsRoot))
            {
                Directory.Delete(LogsRoot, recursive: true);
            }
        }
    }
}
