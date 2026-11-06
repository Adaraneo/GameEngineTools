// StressBalanceTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>Whether a character's stress settles at a sane level over repeated days of the same routine.</summary>
    [TestClass]
    public sealed class StressBalanceTests : TestBase
    {
        private double StressAfterDays(int days, Func<int, bool> hasPrivacyAtHour, double neuroticism = 0.5, double extraversion = 0.5)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, extraversion, 0.5, neuroticism),
                AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);

            var engine = new DefaultPsychologyEngine(
                ServiceProvider.GetRequiredService<IOptions<PsychologyConfig>>(),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral, Motivations: new MotivationState()));

            var ctx = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };

            var start = WDateTime.New(WDateOnly.New(100, 1, 1));
            for (var hour = 0; hour < days * 24; hour++)
            {
                var privacy = hasPrivacyAtHour(hour % 24);
                ctx.Snapshot = new EnginesSnapshot(
                    new PhysiologyState(80, 0, 20, 20, 0, 0, 0, null),
                    engine.State,
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("place", privacy, 0.2, 0.2, privacy ? SurfaceKind.Private : SurfaceKind.Social),
                    new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>()));
                engine.Tick(start + WTimeSpan.FromHours(hour), WTimeSpan.FromHours(1), ctx, new EventCollector());
            }

            return engine.State.Stress;
        }

        [TestMethod]
        public void Stress_OfARoutineDay_SettlesLow_ButAWholeDayInAPublicPlaceDoesNot()
        {
            var routine = StressAfterDays(10, hour => hour is < 8 or >= 16);
            var allPublic = StressAfterDays(10, _ => false);
            var allPrivate = StressAfterDays(10, _ => true);

            Console.WriteLine($"routine (16 h private, 8 h public): {routine:F1}; all public: {allPublic:F1}; all private: {allPrivate:F1}");
            Assert.IsTrue(routine < 40.0, $"A normal day with a home settles at a modest stress, got {routine:F1}.");
            Assert.IsTrue(allPublic > routine + 20.0, $"Living in a place without privacy keeps stress high, got {allPublic:F1} vs {routine:F1}.");
        }
    }
}
