// SexualEncounterReproductionTests.cs
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
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    [TestClass]
    public class SexualEncounterReproductionTests : TestBase
    {
        [TestMethod]
        public void AcceptedInvite_InPrivateAdultContext_DoesNotEscalateWhenReadinessIsInsufficient()
        {
            var from = new HumanId(Guid.NewGuid());
            var to = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = Edge(to, from)
            });
            var ctx = AdultContext(to, SexBiology.Female, relationships, new AlwaysAcceptRandom());
            var engine = InteractionEngine();
            var outbox = new EventCollector();

            engine.Handle(
                new InteractionProposed(WDateTime.New(100, 1, 1), from, to, SpeechAct.Invite, null, SexBiology.Male),
                ctx,
                outbox);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<InteractionOutcome>().Single().Accepted);
            Assert.AreEqual(0, events.OfType<SexualEncounterProposed>().Count());
            Assert.AreEqual(0, events.OfType<SexualEncounterOutcome>().Count());
        }

        [TestMethod]
        public void AcceptedInvite_InPrivateAdultContext_EscalatesOnlyWhenReadinessIsClearlyHigh()
        {
            var from = new HumanId(Guid.NewGuid());
            var to = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = HighReadinessEdge(to, from)
            });
            var ctx = AdultContext(to, SexBiology.Female, relationships, new AlwaysAcceptRandom(), semanticMemory: PositiveSemanticMemory(from));
            var engine = InteractionEngine();
            var outbox = new EventCollector();

            engine.Handle(
                new InteractionProposed(WDateTime.New(100, 1, 1), from, to, SpeechAct.Invite, null, SexBiology.Male),
                ctx,
                outbox);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<InteractionOutcome>().Single().Accepted);
            Assert.AreEqual(1, events.OfType<SexualEncounterProposed>().Count());
            Assert.AreEqual(0, events.OfType<SexualEncounterOutcome>().Count());
        }

        [TestMethod]
        public void InteractionProposed_StressPenaltyIsStrongerAtLowTrustThanHighTrust()
        {
            var from = new HumanId(Guid.NewGuid());
            var lowTrustRecipient = new HumanId(Guid.NewGuid());
            var highTrustRecipient = new HumanId(Guid.NewGuid());
            var lowRelationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = LowTrustEdge(lowTrustRecipient, from)
            });
            var highRelationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = HighReadinessEdge(highTrustRecipient, from)
            });
            var engine = InteractionEngine();
            var lowOutbox = new EventCollector();
            var highOutbox = new EventCollector();

            engine.Handle(
                new InteractionProposed(WDateTime.New(100, 1, 1), from, lowTrustRecipient, SpeechAct.Invite, null, SexBiology.Male),
                AdultContext(lowTrustRecipient, SexBiology.Female, lowRelationships, new ThresholdRandom(0.45), stress: 85),
                lowOutbox);

            engine.Handle(
                new InteractionProposed(WDateTime.New(100, 1, 1), from, highTrustRecipient, SpeechAct.Invite, null, SexBiology.Male),
                AdultContext(highTrustRecipient, SexBiology.Female, highRelationships, new ThresholdRandom(0.45), stress: 85),
                highOutbox);

            Assert.IsFalse(lowOutbox.Drain().OfType<InteractionOutcome>().Single().Accepted);
            Assert.IsTrue(highOutbox.Drain().OfType<InteractionOutcome>().Single().Accepted);
        }

        [TestMethod]
        public void SexualEncounterProposal_WhenAccepted_EmitsSingleOutcome()
        {
            var from = new HumanId(Guid.NewGuid());
            var to = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = Edge(to, from)
            });
            var ctx = AdultContext(to, SexBiology.Female, relationships, new AlwaysAcceptRandom());
            var engine = InteractionEngine();
            var outbox = new EventCollector();

            engine.Handle(
                new SexualEncounterProposed(
                    WDateTime.New(100, 1, 1),
                    from,
                    to,
                    ReproductiveIntent.Indifferent,
                    ContraceptionLevel.Unspecified,
                    ReproductivePotential: true),
                ctx,
                outbox);

            var outcome = outbox.Drain().OfType<SexualEncounterOutcome>().Single();
            Assert.IsTrue(outcome.Accepted);
            Assert.IsTrue(outcome.ReproductivePotential);
        }

        [TestMethod]
        public void SexualEncounterOutcome_CanStartPregnancy_WhenFemaleRecipient()
        {
            var male = new HumanId(Guid.NewGuid());
            var female = new HumanId(Guid.NewGuid());
            var engine = PhysiologyEngine(female, SexBiology.Female);
            var ctx = AdultContext(female, SexBiology.Female, EmptyRelationships(), new AlwaysAcceptRandom());
            var outbox = new EventCollector();

            engine.Handle(
                new SexualEncounterOutcome(
                    WDateTime.New(100, 1, 1),
                    male,
                    female,
                    Accepted: true,
                    "accepted",
                    ReproductiveIntent.TryingForChild,
                    ContraceptionLevel.None,
                    ReproductivePotential: true),
                ctx,
                outbox);

            var started = outbox.Drain().OfType<PregnancyStarted>().Single();
            Assert.AreEqual(female, started.Human);
            Assert.AreEqual(male, started.OtherParent);
            Assert.IsNotNull(engine.State.Pregnancy);
        }

        [TestMethod]
        public void SexualEncounterOutcome_CanStartPregnancy_WhenFemaleInitiator()
        {
            var female = new HumanId(Guid.NewGuid());
            var male = new HumanId(Guid.NewGuid());
            var engine = PhysiologyEngine(female, SexBiology.Female);
            var ctx = AdultContext(female, SexBiology.Female, EmptyRelationships(), new AlwaysAcceptRandom());
            var outbox = new EventCollector();

            engine.Handle(
                new SexualEncounterOutcome(
                    WDateTime.New(100, 1, 1),
                    female,
                    male,
                    Accepted: true,
                    "accepted",
                    ReproductiveIntent.TryingForChild,
                    ContraceptionLevel.None,
                    ReproductivePotential: true),
                ctx,
                outbox);

            var started = outbox.Drain().OfType<PregnancyStarted>().Single();
            Assert.AreEqual(female, started.Human);
            Assert.AreEqual(male, started.OtherParent);
            Assert.IsNotNull(engine.State.Pregnancy);
        }

        [TestMethod]
        public void Pregnancy_Tick_DiscoversThenEmitsBirthEventAtTerm()
        {
            var female = new HumanId(Guid.NewGuid());
            var male = new HumanId(Guid.NewGuid());
            var engine = PhysiologyEngine(female, SexBiology.Female);
            var ctx = AdultContext(female, SexBiology.Female, EmptyRelationships(), new AlwaysAcceptRandom());
            var start = WDateTime.New(100, 1, 1);
            var startOutbox = new EventCollector();

            engine.Handle(
                new SexualEncounterOutcome(start, male, female, true, "accepted", ReproductiveIntent.TryingForChild, ContraceptionLevel.None, true),
                ctx,
                startOutbox);
            startOutbox.Drain();

            var discoveryOutbox = new EventCollector();
            engine.Tick(WDateTime.New(100, 1, 22), WTimeSpan.FromDays(21), ctx, discoveryOutbox);
            Assert.AreEqual(1, discoveryOutbox.Drain().OfType<PregnancyDiscovered>().Count());
            Assert.IsTrue(engine.State.Pregnancy?.Discovered);

            var birthOutbox = new EventCollector();
            engine.Tick(WDateTime.New(100, 10, 11), WTimeSpan.FromDays(280), ctx, birthOutbox);
            var born = birthOutbox.Drain().OfType<ChildBorn>().Single();
            Assert.AreEqual(female, born.ParentA);
            Assert.AreEqual(male, born.ParentB);
            Assert.IsNull(engine.State.Pregnancy);
        }

        private static DefaultInteractionEngine InteractionEngine()
        {
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            engine.RestoreState(new InteractionSurface("private", true, 0.1, 0.1, SurfaceKind.Private));
            return engine;
        }

        private static DefaultPhysiologyEngine PhysiologyEngine(HumanId self, SexBiology biology)
        {
            var engine = new DefaultPhysiologyEngine(
                Options.Create(new PhysiologyConfig(
                    BaseConceptionChancePerEncounter: 1.0,
                    OvulationConceptionMultiplier: 1.0,
                    PregnancyTermDays: 280)),
                Options.Create(new MenstrualCycleConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new AlwaysAcceptRandom(),
                biology,
                WDateOnly.New(70, 1, 1),
                WDateOnly.New(100, 1, 1));

            engine.RestoreState(new PhysiologyState(
                Energy: 95,
                SleepDebtHours: 0,
                Hunger: 5,
                Thirst: 5,
                Pain: 0,
                ImmuneLoad: 0,
                BodyTempDelta: 0,
                Cycle: new MenstrualCycleState(CyclePhase.Ovulation, 14, true, 0, 0, 0, 1.15, WDateOnly.New(100, 1, 1))));

            return engine;
        }

        private static IHumanContext AdultContext(HumanId self, SexBiology biology, RelationshipState relationships, IRandomSource random, double stress = 5, SemanticMemoryState? semanticMemory = null)
        {
            var personality = new Personality(
                new BigFive(0.55, 0.55, 0.55, 0.6, 0.2),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.7, 0.5, 0.3, 0.4, 0.5, 0.5, 0.4, 0.6, 0.85),
                Sociosexuality.Unrestricted,
                Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0.2, 0.5, 0.5, stress, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("private", true, 0.1, 0.1, SurfaceKind.Private),
                relationships,
                new MemoryIndex(new List<EpisodicMemory>()),
                semanticMemory ?? SemanticMemoryState.Empty);

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "Test", Familiar = new[] { "Test" } },
                    new Surname { Male = "Test", Female = "Test" },
                    WDateOnly.New(70, 1, 1)),
                Biology = biology,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new LocalNullEventBus(),
                Scheduler = new LocalNullScheduler()
            };
        }

        private static RelationshipState EmptyRelationships()
            => new(new Dictionary<HumanId, RelationshipEdge>());

        private static RelationshipEdge Edge(HumanId self, HumanId other)
            => new(
                self,
                other,
                Like: 75,
                Trust: 72,
                Familiarity: 75,
                AestheticAttraction: 78,
                PhysicalAttraction: 82,
                IntimateAffinity: 72,
                SexualInterest: 82,
                Closeness: 78,
                Respect: 70,
                Comfort: 76,
                Breakdown: new DomainBreakdown(60, 60, 60, 60, 80));

        private static SemanticMemoryState PositiveSemanticMemory(HumanId other)
            => new(new Dictionary<HumanId, PersonBeliefSet>
            {
                [other] = new PersonBeliefSet(
                    other,
                    new Dictionary<PersonBeliefKind, PersonBelief>
                    {
                        [PersonBeliefKind.Warm] = new PersonBelief(other, PersonBeliefKind.Warm, 0.80, 0.70, 5, WDateTime.New(100, 1, 1)),
                        [PersonBeliefKind.EmotionallySafe] = new PersonBelief(other, PersonBeliefKind.EmotionallySafe, 0.80, 0.70, 5, WDateTime.New(100, 1, 1)),
                        [PersonBeliefKind.Reliable] = new PersonBelief(other, PersonBeliefKind.Reliable, 0.65, 0.60, 4, WDateTime.New(100, 1, 1))
                    })
            });

        private static RelationshipEdge HighReadinessEdge(HumanId self, HumanId other)
            => new(
                self,
                other,
                Like: 90,
                Trust: 92,
                Familiarity: 90,
                AestheticAttraction: 88,
                PhysicalAttraction: 90,
                IntimateAffinity: 88,
                SexualInterest: 90,
                Closeness: 92,
                Respect: 85,
                Comfort: 94,
                Breakdown: new DomainBreakdown(70, 70, 70, 70, 88),
                PositiveInteractionCount: 10);

        private static RelationshipEdge LowTrustEdge(HumanId self, HumanId other)
            => new(
                self,
                other,
                Like: 35,
                Trust: 20,
                Familiarity: 25,
                AestheticAttraction: 45,
                PhysicalAttraction: 45,
                IntimateAffinity: 20,
                SexualInterest: 20,
                Closeness: 20,
                Respect: 35,
                Comfort: 20,
                Breakdown: new DomainBreakdown(40, 40, 40, 40, 45));

        private sealed class AlwaysAcceptRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0;

            public bool Chance(double p) => p > 0;
        }

        private sealed class ThresholdRandom(double threshold) : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => threshold;

            public bool Chance(double p) => p >= threshold;
        }
    }
}
