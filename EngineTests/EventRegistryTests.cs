// EventRegistryTests.cs
// Generates AND guards the canonical event registry consumed by LogReader.
//
// CoreLog.cs is the single hand-edited source of truth for events. This test reflects
// every [LoggerMessage] method and projects it into LogReader/src/eventRegistry.json:
//
//   • Default run  → DRIFT GUARD: asserts the committed json matches reflection.
//                    Fails if anyone adds/changes/removes a CoreLog event without
//                    regenerating the registry.
//   • GENERATE_REGISTRY=1 → REGENERATE: rewrites the json from reflection, then
//                    reports Inconclusive so the change is reviewed & committed.
//
// To regenerate after changing CoreLog:
//   GENERATE_REGISTRY=1 vstest ... --filter:EventRegistry_MatchesCoreLog

namespace EngineTests
{
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    [TestClass]
    public sealed class EventRegistryTests
    {
        /// <summary>One catalog entry per event type — mirrors eventRegistry.json shape.</summary>
        private sealed record EventDef(
            int Id,
            string Kind,
            string Engine,
            string Scope,
            string Level,
            IReadOnlyList<PayloadField> Payload);

        private sealed record PayloadField(string Name, string Type);

        // World-scoped events carry no PersonId (no BeginCharacterScope around them).
        private static readonly HashSet<int> WorldScopedIds = new() { 1501, 4100, 4101, 4102, 4103 };

        [TestMethod]
        public void EventRegistry_MatchesCoreLog()
        {
            var defs = ReflectCoreLog();
            var json = Serialize(defs);

            var path = ResolveRegistryPath();

            if (Environment.GetEnvironmentVariable("GENERATE_REGISTRY") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, json);
                Assert.Inconclusive($"Regenerated {path} ({defs.Count} events). Review & commit.");
                return;
            }

            Assert.IsTrue(File.Exists(path),
                $"eventRegistry.json not found at {path}. Run with GENERATE_REGISTRY=1 to create it.");

            var committed = File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
            var expected = json.Replace("\r\n", "\n").TrimEnd();

            Assert.AreEqual(expected, committed,
                "eventRegistry.json has drifted from CoreLog.cs. " +
                "Regenerate with GENERATE_REGISTRY=1 and commit the result.");
        }

        /// <summary>Reflects every [LoggerMessage] method in CoreLog into a sorted catalog.</summary>
        private static List<EventDef> ReflectCoreLog()
        {
            var defs = new List<EventDef>();
            foreach (var m in typeof(CoreLog).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var attr = m.GetCustomAttribute<LoggerMessageAttribute>();
                if (attr is null)
                    continue;

                var payload = m.GetParameters()
                    // Skip the `this ILogger logger` receiver — it is plumbing, not payload.
                    .Where(p => p.ParameterType != typeof(ILogger) &&
                                !typeof(ILogger).IsAssignableFrom(p.ParameterType))
                    .Select(p => new PayloadField(p.Name ?? "?", FriendlyType(p.ParameterType)))
                    .ToList();

                defs.Add(new EventDef(
                    Id: attr.EventId,
                    Kind: m.Name,
                    Engine: EngineFor(attr.EventId),
                    Scope: WorldScopedIds.Contains(attr.EventId) ? "world" : "character",
                    Level: attr.Level.ToString(),
                    Payload: payload));
            }
            return defs.OrderBy(d => d.Id).ToList();
        }

        /// <summary>Maps an EventId to its owning engine using the documented range convention.</summary>
        private static string EngineFor(int id) => id switch
        {
            >= 1000 and <= 1099 => "Behavior",
            >= 1100 and <= 1199 => "Sleep",
            >= 1200 and <= 1209 => "Interactions",
            >= 1210 and <= 1219 => "Values",
            >= 1220 and <= 1229 => "Dialogue",
            >= 1230 and <= 1299 => "Values",
            >= 1300 and <= 1399 => "Goals",
            >= 1400 and <= 1499 => "Schedule",
            >= 1500 and <= 1599 => "World",
            >= 2000 and <= 2999 => "Relationships",
            >= 3000 and <= 3999 => "Memory",
            >= 4000 and <= 4999 => "Scheduler",
            >= 5000 and <= 5099 => "Physiology",
            >= 5100 and <= 5199 => "Psychology",
            >= 5200 and <= 5299 => "Behavior",
            _ => "Unknown",
        };

        /// <summary>JS-friendly type names for the payload schema.</summary>
        private static string FriendlyType(Type t) => t switch
        {
            _ when t == typeof(double) => "number",
            _ when t == typeof(float) => "number",
            _ when t == typeof(int) => "int",
            _ when t == typeof(long) => "long",
            _ when t == typeof(bool) => "bool",
            _ when t == typeof(string) => "string",
            _ => t.Name,
        };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static string Serialize(List<EventDef> defs)
        {
            // camelCase keys for JS consumption.
            var shaped = defs.Select(d => new
            {
                id = d.Id,
                kind = d.Kind,
                engine = d.Engine,
                scope = d.Scope,
                level = d.Level,
                payload = d.Payload.Select(p => new { name = p.Name, type = p.Type }),
            });
            return JsonSerializer.Serialize(shaped, JsonOpts);
        }

        /// <summary>Walks up from the test output dir to locate LogReader/src/eventRegistry.json.</summary>
        private static string ResolveRegistryPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "LogReader", "src", "eventRegistry.json");
                if (Directory.Exists(Path.Combine(dir.FullName, "LogReader")))
                    return candidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate the LogReader directory above the test output path.");
        }
    }
}
