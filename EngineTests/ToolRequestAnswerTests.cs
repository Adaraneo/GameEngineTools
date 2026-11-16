// ToolRequestAnswerTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Contracts;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>The owner's interaction engine answers a Request for a tool from the owner's feeling for the asker and from whether the owner is at work.</summary>
    [TestClass]
    public sealed class ToolRequestAnswerTests : TestBase
    {
        private static readonly HumanId Asker = new(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"));
        private static readonly HumanId Owner = new(Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"));

        private sealed class RecordingRandom : IRandomSource
        {
            public double LastChance { get; private set; } = -1;

            public int Next(int min, int max)
            {
                return min;
            }

            public double NextUnit()
            {
                return 0.0;
            }

            public bool Chance(double p)
            {
                LastChance = p;
                return false;
            }
        }

        private static RelationshipEdge Edge(double trust, double like, double closeness)
        {
            return new RelationshipEdge(
                Owner, Asker, Like: like, Trust: trust, Familiarity: 50, AestheticAttraction: 0, PhysicalAttraction: 0, IntimateAffinity: 0, SexualInterest: 0,
                Closeness: closeness, Respect: 50, Comfort: 50, Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));
        }

        private static (double Chance, InteractionOutcome Outcome) Answer(RelationshipEdge? edge, bool ownerAtWork, bool forATool)
        {
            var random = new RecordingRandom();
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                null);
            engine.RestoreState(new InteractionSurface("farm", false, 0.1, 0.1, SurfaceKind.Social));

            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3), Sociosexuality.Intermediate, Chronotype.Neutral);
            var edges = edge is null ? new Dictionary<HumanId, RelationshipEdge>() : new Dictionary<HumanId, RelationshipEdge> { [Asker] = edge };
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("farm", false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(edges),
                new MemoryIndex(new List<EpisodicMemory>()));
            if (ownerAtWork)
            {
                var slot = new ScheduleSlot("work", 5, Work);
                snapshot = snapshot with { Schedule = new DailyScheduleState(new[] { slot }, slot, 0, "farmer") };
            }

            var ctx = new HumanContext
            {
                Id = Owner,
                Biology = SexBiology.Male,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };

            var act = SpeechAct.Relational(RelationalActKind.Request, Asker, Owner, new WDateTime(0));
            if (forATool)
            {
                act = act with { Roles = act.Roles.SetItem(FgdFunctor.EFF, new EntityRef(EntityId.ForObject("spade_1"), "rýč")) };
            }

            var outbox = new EventCollector();
            engine.Handle(new InteractionProposed(new WDateTime(0), Asker, Owner, new InteractionContent(act)), ctx, outbox);
            return (random.LastChance, outbox.Drain().OfType<InteractionOutcome>().Single());
        }

        [TestMethod]
        public void AFriend_IsLikelyToBeLentTheTool_AStrangerAlmostNever()
        {
            var friend = Answer(Edge(80, 70, 50), ownerAtWork: false, forATool: true);
            var stranger = Answer(null, ownerAtWork: false, forATool: true);

            Assert.IsTrue(friend.Chance > 0.75, $"A friend: {friend.Chance:F2}");
            Assert.IsTrue(stranger.Chance <= 0.06, $"A stranger: {stranger.Chance:F2}");
        }

        [TestMethod]
        public void AnOwnerAtWork_RarelyLends_EvenToAFriend()
        {
            var atWork = Answer(Edge(80, 70, 50), ownerAtWork: true, forATool: true);

            Assert.IsTrue(atWork.Chance < 0.15, $"The owner needs the tool: {atWork.Chance:F2}");
        }

        [TestMethod]
        public void TheOutcome_NamesTheToolThatWasAskedFor_AndOnlyForAToolRequest()
        {
            var tool = Answer(Edge(80, 70, 50), ownerAtWork: false, forATool: true).Outcome;
            var plain = Answer(Edge(80, 70, 50), ownerAtWork: false, forATool: false).Outcome;

            Assert.AreEqual("spade_1", tool.SubjectObjectId);
            Assert.AreEqual(RelationalActKind.Request, tool.Act);
            Assert.IsNull(plain.SubjectObjectId, "A request for something else is answered as before.");
        }

        [TestMethod]
        public void ARequestForNothingInParticular_IsNotJudgedByTheLendingPolicy()
        {
            var plain = Answer(Edge(80, 70, 50), ownerAtWork: true, forATool: false);

            Assert.IsTrue(plain.Chance > 0.3, $"The generic acceptance does not care whether the owner is at work: {plain.Chance:F2}");
        }
    }
}
