// AuditOneLineFixTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using static EngineTests.MemoryScientificTestData;

    /// <summary>Regression tests for the one-line fixes from the 2026-10 audit (docs/plans/audit-2026-10-remediation.md, Phase 1).</summary>
    [TestClass]
    public class AuditOneLineFixTests : TestBase
    {
        [TestMethod]
        public void SleepEnded_Consolidation_KeepsKnownObjects()
        {
            var now = WDateTime.New(100, 1, 10, 12);
            var self = new HumanId(Guid.NewGuid());
            var facts = new[] { new ObjectLocationFact("bread_01", "bakery", now, 0.9, PickupItemKind.Food) };
            var episode = Episode(now, 2, "Interaction:SmallTalk:Accepted|from=a|to=b", EmotionalTag.Positive, 0.80, new HumanId(Guid.NewGuid()), salience: 0.90);

            var engine = BuildMemoryEngine();
            engine.RestoreState(new MemoryIndex(new List<EpisodicMemory> { episode }) { KnownObjects = facts });

            engine.Handle(new SleepEnded(now, self, TotalHoursSlept: 8, Quality: 90, WasInterrupted: false), BuildMemoryContext(self), new EventCollector());

            Assert.AreEqual(1, engine.State.KnownObjects.Count, "Spánek nesmí smazat prostorovou paměť.");
            Assert.AreEqual("bakery", engine.State.KnownObjects[0].LocationId);
        }

        [TestMethod]
        public void IntentMapper_FoodActions_MapToFoodSeeking()
        {
            Assert.AreEqual(BehaviorIntentKind.FoodSeeking, BehaviorIntentMapper.Resolve(ActionNames.Eat));
            Assert.AreEqual(BehaviorIntentKind.FoodSeeking, BehaviorIntentMapper.Resolve(ActionNames.Drink));
            Assert.AreEqual(BehaviorIntentKind.FoodSeeking, BehaviorIntentMapper.Resolve(ActionNames.EatStored));
        }

        [TestMethod]
        public void Factory_AdultCharacter_SeedsGoalsAtClockNow_NotAtBirthDate()
        {
            var generator = ServiceProvider.GetRequiredService<IHumanBlueprintGenerator>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var now = ServiceProvider.GetRequiredService<IClock>().Now;
            var birth = now.Date.AddYears(-30);

            var seeded = 0;
            for (var seed = 1; seed <= 25; seed++)
            {
                var human = factory.Create(generator.Generate(new HumanBlueprintRequest(
                    Sex: SexBiology.Female, MinBirthDate: birth, MaxBirthDate: birth, Seed: seed)));
                human.Tick(now, WTimeSpan.FromMinutes(1));

                foreach (var goal in human.Snapshot.Goals?.Goals ?? Array.Empty<PersistentGoal>())
                {
                    if (goal.Origin != GoalOrigin.Personality) continue;
                    seeded++;
                    Assert.IsTrue(goal.CreatedAt >= now - WTimeSpan.FromDays(1),
                        $"Personality goal {goal.Kind} (seed {seed}) was created at {goal.CreatedAt}, i.e. at the birth date, not at the clock time {now}.");
                    Assert.IsTrue(goal.LastProgressAt >= now - WTimeSpan.FromDays(1),
                        $"Personality goal {goal.Kind} (seed {seed}) starts with LastProgressAt={goal.LastProgressAt}, decades in the past.");
                }
            }

            Assert.IsTrue(seeded > 0, "None of 25 generated adults received a personality goal; the test would prove nothing.");
        }

        private static DefaultMemoryEngine BuildMemoryEngine()
        {
            var sp = new ServiceCollection()
                .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
                .AddSingleton(Options.Create(new MemoryConfig()))
                .BuildServiceProvider();
            return new DefaultMemoryEngine(sp.GetRequiredService<IOptions<MemoryConfig>>(), sp.GetRequiredService<ILoggerFactory>());
        }

        private static IHumanContext BuildMemoryContext(HumanId selfId)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = selfId,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }
    }
}
