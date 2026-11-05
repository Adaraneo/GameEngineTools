// SoakDeterminismTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using EngineTests.Utils;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.Characters.Engines.Status;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Movement;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>A small society run for weeks through the real scene: state must stay bounded, and equal seeds must give equal worlds.</summary>
    [TestClass]
    public sealed class SoakDeterminismTests : TestBase
    {
        private static int Characters => int.TryParse(Environment.GetEnvironmentVariable("SOAK_CHARS"), out var n) ? n : 8;
        private static readonly string[] Occupations = { "scholar", "clerk", "farmer", "artist", "merchant", "healer", "laborer", "craftsperson" };

        private sealed record Outcome(IReadOnlyList<IHuman> Humans, IReadOnlyList<(int Day, string Metrics)> Samples, IReadOnlyDictionary<string, int> Events);

        private Outcome RunSociety(int seed, int days, bool occupations = false, bool homes = false)
        {
            var rngFactory = ServiceProvider.GetRequiredService<IRandomSourceFactory>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var locations = (DefaultLocationService)ServiceProvider.GetRequiredService<ILocationService>();
            var worldClock = ServiceProvider.GetRequiredService<IWorldClock>();
            var spec = ServiceProvider.GetRequiredService<WorldTimeSpec>();

            var square = new LocationDescriptor($"square_{seed}", "Square", 0.05, 0.01, 50, Environment.GetEnvironmentVariable("SOAK_PRIVATE") == "1", LocationType.Social);
            locations.RegisterLocation(square);

            ServiceProvider.GetRequiredService<SqliteWorldDatabase>().InsertLocation(square, "r");
            var buffer = ServiceProvider.GetRequiredService<WorldObjectWriteBuffer>();
            var homeIds = new List<string>();
            if (homes)
            {
                for (var i = 0; i < Characters; i++)
                {
                    var home = new LocationDescriptor($"home_{seed}_{i}", $"Home {i}", 0.02, 0.01, 2, true, LocationType.Rest);
                    locations.RegisterLocation(home);
                    ServiceProvider.GetRequiredService<SqliteWorldDatabase>().InsertLocation(home, "r");
                    homeIds.Add(home.Id);
                }
            }

            SeedEnvironment(buffer, square.Id, homeIds);

            var clock = new SystemClock(worldClock, spec);
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));

            var humans = new List<IHuman>();
            for (var i = 0; i < Characters; i++)
            {
                var human = factory.Create(Blueprint(rngFactory, seed, i, occupations ? Occupations[i % Occupations.Length] : null));
                locations.MoveCharacter(human.Id, square.Id);
                humans.Add(human);
            }

            var reputation = new CommunityReputationLedger();
            var status = new StatusLedger();
            var orchestrator = new DefaultSceneOrchestrator(
                ServiceProvider.GetRequiredService<IAttractionCalculator>(),
                locations,
                ServiceProvider.GetRequiredService<IPerceptionFidelityPolicy>(),
                new GameEngineTools.World.Simulation.CharacterPerceptionOptions(),
                ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>(),
                new WorldMap(
                    new Dictionary<string, LocationDescriptor>(),
                    new Dictionary<string, IReadOnlyList<WorldConnection>>(),
                    new Dictionary<string, IReadOnlyList<string>>()),
                ServiceProvider.GetRequiredService<IMovementSpeedProvider>(),
                new Random(seed),
                ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<DefaultSceneOrchestrator>(),
                ServiceProvider.GetRequiredService<IWorldObjectProvider>(),
                new SceneOrchestratorOptions(),
                reputation,
                status);

            var samples = new List<(int, string)>();
            var acts = new Dictionary<string, int>();
            var events = new Dictionary<string, int>();
            var tickDay = -1;
            var options = new SimulationSceneOptions
            {
                Characters = humans,
                LocationService = locations,
                ObjectSnapshotCache = ServiceProvider.GetRequiredService<WorldObjectSnapshotCache>(),
                WriteBuffer = buffer,
                RespawnScheduler = ServiceProvider.GetRequiredService<ObjectRespawnScheduler>(),
                SimulationDays = days,
                TickStep = WTimeSpan.FromMinutes(30),
                DefaultCharacterLod = CognitiveResolutionLevel.Nearby,
                OnTick = (now, chars) =>
                {
                    orchestrator.OnTick(now, chars);
                    foreach (var c in chars)
                        foreach (var e in c.LastOutbox)
                        {
                            events[e.GetType().Name] = events.GetValueOrDefault(e.GetType().Name) + 1;
                            if (e is GameEngineTools.Characters.Engines.Behavior.ActionCommitted ac)
                                acts[ac.ActionName] = acts.GetValueOrDefault(ac.ActionName) + 1;
                        }
                    var day = (int)(WDateTime.Difference(now, WDateTime.New(WDateOnly.New(100, 1, 1))).TotalDays);
                    if (day != tickDay && day % 10 == 0)
                        samples.Add((day, Describe(chars, reputation)));
                    tickDay = day;
                },
            };

            new SimulationScene(clock, options, ServiceProvider.GetRequiredService<ICognitiveResolutionLevelRuntime>())
                .RunAsync().GetAwaiter().GetResult();

            samples.Add((days, Describe(humans, reputation) + " | actions: " + string.Join(", ", acts.OrderByDescending(a => a.Value).Take(8).Select(a => $"{a.Key}={a.Value}"))));
            return new Outcome(humans, samples, events);
        }

        private static string Describe(IReadOnlyList<IHuman> humans, CommunityReputationLedger reputation)
        {
            var edges = humans.Max(h => h.Snapshot.Relationships.Edges.Count);
            var episodes = humans.Max(h => h.Snapshot.Memory.Episodes.Count);
            var beliefs = humans.Max(h => h.Snapshot.SemanticMemory?.People.Count ?? 0);
            var goals = humans.Max(h => h.Snapshot.Goals?.Goals.Count ?? 0);
            var known = humans.Max(h => h.Snapshot.Memory.Knowledge?.Count ?? 0);
            var drift = InterestDrift(humans);
            var stress = humans.Average(h => h.Snapshot.Psychology.Stress);
            var body = $"energy={humans.Average(h => h.Snapshot.Physiology.Energy):F0} hunger={humans.Average(h => h.Snapshot.Physiology.Hunger):F0} thirst={humans.Average(h => h.Snapshot.Physiology.Thirst):F0} sleepDebt={humans.Average(h => h.Snapshot.Physiology.SleepDebtHours):F1} valence={humans.Average(h => h.Snapshot.Psychology.Valence):F2} allostatic={humans.Average(h => h.Snapshot.Physiology.AllostaticLoad):F0} needSocial={humans.Average(h => h.Snapshot.Psychology.Motivations?.NeedSocial ?? 0):F0}";
            return $"maxEdges={edges} maxEpisodes={episodes} maxBeliefPeople={beliefs} maxGoals={goals} maxKnowledge={known} reputations={reputation.Snapshot().Count} interestDrift={drift:F3} meanStress={stress:F1} {body}";
        }

        private static double InterestDrift(IReadOnlyList<IHuman> humans) => humans
            .Where(h => h.Snapshot.Interests is not null)
            .Select(h => h.Snapshot.Interests!)
            .Max(s => new[]
            {
                s.Current.Realistic - s.Baseline.Realistic, s.Current.Investigative - s.Baseline.Investigative,
                s.Current.Artistic - s.Baseline.Artistic, s.Current.Social - s.Baseline.Social,
                s.Current.Enterprising - s.Baseline.Enterprising, s.Current.Conventional - s.Baseline.Conventional,
            }.Max(Math.Abs));

        private static void SeedEnvironment(WorldObjectWriteBuffer buffer, string locationId, IReadOnlyList<string> homeIds)
        {
            WorldObject Fixture(string id, WorldObjectCategory category, AffordanceType affordance, bool pickable = false, bool respawns = false, string? at = null)
                => new()
                {
                    Id = id,
                    DisplayName = id,
                    Category = category,
                    LocationId = at ?? locationId,
                    IsAvailable = true,
                    IsPickable = pickable,
                    Respawns = respawns,
                    RespawnMinutes = 30,
                    WeightGrams = pickable ? 300 : 500_000,
                    ItemKind = category == WorldObjectCategory.Food ? PickupItemKind.Food : PickupItemKind.None,
                    Affordances = ImmutableArray.Create(new WorldObjectAffordance(affordance, 0.8)),
                };

            for (var i = 0; i < Characters; i++)
            {
                buffer.AddObject(Fixture($"food_{i}", WorldObjectCategory.Food, AffordanceType.Hunger, pickable: true, respawns: true));
                buffer.AddObject(Fixture($"bed_{i}", WorldObjectCategory.Shelter, AffordanceType.Rest, at: homeIds.Count > 0 ? homeIds[i] : null));
            }

            buffer.AddObject(Fixture("well", WorldObjectCategory.Drink, AffordanceType.Thirst));
            buffer.AddObject(Fixture("bench", WorldObjectCategory.Tool, AffordanceType.Work));
            buffer.Flush();
        }

        private static HumanBlueprint Blueprint(IRandomSourceFactory rngFactory, int seed, int index, string? occupation = null)
        {
            var id = new HumanId(new Guid(seed, 0, (short)index, new byte[8]));
            var identity = new Identity(
                new Name { Original = $"N{index}", Familiar = new[] { $"N{index}" } },
                new Surname { Male = "Soak", Female = "Soak" },
                WDateOnly.New(100 - 25 - index, 1, 1));
            var personality = new Personality(
                new BigFive(0.3 + 0.05 * index, 0.5, 0.4 + 0.05 * index, 0.5, 0.6 - 0.04 * index),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);
            var sex = index % 2 == 0 ? SexBiology.Female : SexBiology.Male;
            var genes = new AppearanceGenerator(rngFactory).GenerateBlueprint(sex, seed: seed * 100 + index);
            return new HumanBlueprint(id, identity, sex, personality, genes, Seed: seed * 100 + index, Occupation: occupation);
        }

        /// <summary>On-demand long run: set SOAK_DAYS (e.g. 90) to see how reproduction, interests and stress behave over months.</summary>
        [TestMethod]
        public void Society_WithOccupations_ReportsReproductionInterestsAndStress()
        {
            var days = int.TryParse(Environment.GetEnvironmentVariable("SOAK_DAYS"), out var requested) ? requested : 6;
            var outcome = RunSociety(seed: 31, days: days, occupations: Environment.GetEnvironmentVariable("SOAK_NO_OCC") != "1", homes: Environment.GetEnvironmentVariable("SOAK_HOMES") == "1");

            foreach (var (day, metrics) in outcome.Samples)
                Console.WriteLine($"day {day,3}: {metrics}");
            Console.WriteLine("top events: " + string.Join(", ", outcome.Events.OrderByDescending(e => e.Value).Take(25).Select(e => $"{e.Key}={e.Value}")));
            foreach (var name in new[] { "SexualEncounterOutcome", "PregnancyStarted", "ChildBorn", "CharacterDied", "WorkFitFelt", "ActionCommitted" })
                Console.WriteLine($"{name}: {outcome.Events.GetValueOrDefault(name)}");

            Assert.IsTrue(InterestDrift(outcome.Humans) < 0.25, "Interests stay within a modest distance of their baseline.");
            Assert.AreEqual(0, outcome.Events.GetValueOrDefault("CharacterDied"), "Nobody dies in a fed, sheltered society.");
        }

        [TestMethod]
        public void Society_RunsForWeeks_AndReportsHowItsStateGrows()
        {
            var outcome = RunSociety(seed: 11, days: 8);

            foreach (var (day, metrics) in outcome.Samples)
                Console.WriteLine($"day {day,3}: {metrics}");

            Assert.IsTrue(outcome.Humans.All(h => h.Snapshot.Relationships.Edges.Count <= Characters - 1), "A relationship edge exists at most per other person.");
        }

        [TestMethod]
        public void Society_WithTheSameSeed_EndsInTheSameState()
        {
            var first = RunSociety(seed: 21, days: 3);
            WWorld.Reset();
            Cleanup();
            Init();
            var second = RunSociety(seed: 21, days: 3);

            var diffs = new List<string>();
            for (var i = 0; i < Characters; i++)
                DeepCompare.Compare($"human{i}", first.Humans[i].Snapshot, second.Humans[i].Snapshot, diffs, ignoreGuids: false);

            Console.WriteLine($"{diffs.Count} difference(s):");
            foreach (var d in diffs.Take(40))
                Console.WriteLine("  " + d);

            Assert.AreEqual(0, diffs.Count, "Two runs with the same seed must end in the same world (see the list above).");
        }
    }
}
