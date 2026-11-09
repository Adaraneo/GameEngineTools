// ConfigCoverageTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Every setting of a config type should be visible in the live appsettings files, not only as a default in code.
    /// The keys that are still missing are listed in <c>ConfigCoverageBaseline.txt</c>; a new setting that is not in the JSON fails here.
    /// </summary>
    [TestClass]
    public sealed class ConfigCoverageTests
    {
        // Every bound section and its type. scripts/check-config.py checks that no BindConfiguration/GetSection in the engine is missing from this list.
        private static readonly (string File, string Section, string Type)[] Sections =
        {
            ("Characters", "Characters:Physiology", "PhysiologyConfig"),
            ("Characters", "Characters:MenstrualCycle", "MenstrualCycleConfig"),
            ("Characters", "Characters:Psychology", "PsychologyConfig"),
            ("Characters", "Characters:Behavior", "BehaviorConfig"),
            ("Characters", "Characters:Sleep", "SleepConfig"),
            ("Characters", "Characters:Interactions", "InteractionConfig"),
            ("Characters", "Characters:Relationships", "RelationshipsConfig"),
            ("Characters", "Characters:Memory", "MemoryConfig"),
            ("Characters", "Characters:SemanticMemory", "SemanticMemoryConfig"),
            ("Characters", "Characters:Lod", "CognitiveResolutionLevelConfig"),
            ("Characters", "Characters:DailySchedule", "DailyScheduleConfig"),
            ("Characters", "Characters:Goals", "GoalConfig"),
            ("Characters", "Characters:Values", "ValuesConfig"),
            ("Characters", "Characters:SelfConcept", "SelfConceptConfig"),
            ("Characters", "Characters:Interests", "InterestConfig"),
            ("Characters", "Characters:SocialComparison", "SocialComparisonConfig"),
            ("Characters", "Characters:Status", "StatusConfig"),
            ("Characters", "Characters:Bereavement", "BereavementConfig"),
            ("Characters", "Characters:Fidelity", "CharacterFidelityConfig"),
            ("Economy", "Economy", "EconomyConfig"),
            ("World", "World:Crops", "CropConfig"),
            ("World", "World:Weather", "WeatherConfig"),
            ("World", "World:Movement", "MovementConfig"),
            ("World", "World:Astro", "AstroConfig"),
            ("World", "World:Universe", "UniverseConfig"),
            ("World", "World:Calendar", "CalendarConfig"),
            ("World", "World:Perception", "CharacterPerceptionOptions"),
        };

        private static readonly string[] Projects = { "EngineTests", "GameSandbox", "WorldObserver" };

        [TestMethod]
        public void NoConfigSetting_IsMissingFromTheJson_BeyondTheKnownBaseline()
        {
            var root = FindRepoRoot();
            if (root is null)
                Assert.Inconclusive("The repository folders were not found next to the test binaries.");

            var current = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var project in Projects)
            {
                foreach (var group in Sections.GroupBy(s => s.File))
                {
                    var path = Path.Combine(root, project, $"appsettings.{group.Key}.json");
                    if (!File.Exists(path))
                        continue;

                    var config = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
                    foreach (var (_, section, typeName) in group)
                    {
                        var keys = config.GetSection(section).GetChildren().Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (keys.Count == 0)
                        {
                            current.Add($"{project}|{section}|*");
                            continue;
                        }

                        foreach (var property in Settings(FindType(typeName)))
                        {
                            if (!keys.Contains(property))
                                current.Add($"{project}|{section}|{property}");
                        }
                    }
                }
            }

            var baselinePath = Path.Combine(root, "EngineTests", "ConfigCoverageBaseline.txt");
            if (Environment.GetEnvironmentVariable("UPDATE_CONFIG_BASELINE") == "1")
            {
                File.WriteAllLines(baselinePath, current);
                return;
            }

            var baseline = File.Exists(baselinePath)
                ? File.ReadAllLines(baselinePath).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>();
            var added = current.Where(k => !baseline.Contains(k)).ToList();
            var fixedNow = baseline.Where(k => !current.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.AreEqual(0, added.Count, "Settings missing from the JSON (add the key, or run with UPDATE_CONFIG_BASELINE=1 to accept):\n" + string.Join("\n", added));
            Assert.AreEqual(0, fixedNow.Count, "These are in the JSON now; remove them from ConfigCoverageBaseline.txt (UPDATE_CONFIG_BASELINE=1 does it):\n" + string.Join("\n", fixedNow));
        }

        [TestMethod]
        public void EverySectionInTheTable_HasAType_WithSettings()
        {
            foreach (var (_, section, typeName) in Sections)
                Assert.IsTrue(Settings(FindType(typeName)).Any(), $"{section} ({typeName}) has no settable properties.");
        }

        private static Type FindType(string name)
            => typeof(GameEngineTools.Characters.Engines.Physiology.PhysiologyConfig).Assembly.GetTypes().Single(t => t.Name == name && !t.IsNested);

        private static IEnumerable<string> Settings(Type type)
            => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.SetMethod!.IsPublic).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

        private static string? FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "EngineTests")) && Directory.Exists(Path.Combine(dir.FullName, "GameEngineTools")))
                    return dir.FullName;
            }

            return null;
        }
    }
}
