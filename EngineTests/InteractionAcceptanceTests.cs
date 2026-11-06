// InteractionAcceptanceTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Contracts;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>How likely a stranger is to take up an overture: friendly ones nearly always, heavier ones less.</summary>
    [TestClass]
    public sealed class InteractionAcceptanceTests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"));
        private static readonly HumanId Listener = new(Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"));

        private sealed class RecordingRandom : IRandomSource
        {
            public double LastChance { get; private set; } = -1;

            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p)
            {
                LastChance = p;
                return false;
            }
        }

        private static double AcceptanceOfAStranger(RelationalActKind kind)
        {
            var random = new RecordingRandom();
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                null);
            engine.RestoreState(new InteractionSurface("room", false, 0.1, 0.1, SurfaceKind.Social));

            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3), Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("room", false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            var ctx = new HumanContext
            {
                Id = Listener,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };

            engine.Handle(InteractionProposed.Of(new WDateTime(0), Speaker, Listener, kind), ctx, new EventCollector());
            return random.LastChance;
        }

        [TestMethod]
        public void AStranger_NearlyAlwaysAnswersFriendlySmallTalk()
        {
            foreach (var kind in new[] { RelationalActKind.SmallTalk, RelationalActKind.Question, RelationalActKind.Humor, RelationalActKind.Validation })
                Assert.IsTrue(AcceptanceOfAStranger(kind) >= 0.75, $"{kind}: {AcceptanceOfAStranger(kind):F2}");
        }

        [TestMethod]
        public void HeavierActs_AreAcceptedLessOftenThanSmallTalk()
        {
            var smallTalk = AcceptanceOfAStranger(RelationalActKind.SmallTalk);
            var disclosure = AcceptanceOfAStranger(RelationalActKind.SelfDisclosure);
            var invite = AcceptanceOfAStranger(RelationalActKind.Invite);

            Assert.IsTrue(smallTalk > disclosure + 0.1, $"{smallTalk:F2} vs {disclosure:F2}");
            Assert.IsTrue(disclosure > invite, $"{disclosure:F2} vs {invite:F2}");
            Assert.IsTrue(invite < 0.55, $"A stranger rarely accepts an invitation: {invite:F2}");
        }
    }
}
