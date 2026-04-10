// SemanticMemoryEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class SemanticMemoryEngineTests : TestBase
    {
        [TestMethod]
        public void Handle_RepeatedRejectingEvidence_FormsStableBelief()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            for (var i = 0; i < 4; i++)
            {
                engine.Handle(new MemoryEncoded(
                    new WDateTime(i),
                    self,
                    Guid.NewGuid(),
                    0.7,
                    "Interaction:Question:declined",
                    "PerceivedThreat:Interaction:Question:declined",
                    other,
                    new PersonBeliefEvidence(other, PersonBeliefKind.Rejecting, 0.22, "test-rejecting")), ctx, outbox);
            }

            var belief = engine.State.GetBeliefs(other)?.Beliefs[PersonBeliefKind.Rejecting];
            Assert.IsNotNull(belief);
            Assert.IsTrue(belief.Strength > 0.10);
            Assert.AreEqual(4, belief.EvidenceCount);
            Assert.IsTrue(belief.Stability > 0.05);
        }

        [TestMethod]
        public void Handle_SingleContradiction_DoesNotFlipEstablishedBelief()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            for (var i = 0; i < 4; i++)
            {
                engine.Handle(new MemoryEncoded(
                    new WDateTime(i),
                    self,
                    Guid.NewGuid(),
                    0.7,
                    "Interaction:Invite:declined",
                    "PerceivedThreat:Interaction:Invite:declined",
                    other,
                    new PersonBeliefEvidence(other, PersonBeliefKind.Rejecting, 0.24, "test-rejecting")), ctx, new EventCollector());
            }

            engine.Handle(new MemoryEncoded(
                new WDateTime(10),
                self,
                Guid.NewGuid(),
                0.7,
                "Interaction:Validation:accepted",
                "PerceivedWarmth:Interaction:Validation:accepted",
                other,
                new PersonBeliefEvidence(other, PersonBeliefKind.Warm, 0.18, "test-warm")), ctx, new EventCollector());

            var beliefs = engine.State.GetBeliefs(other);
            Assert.IsNotNull(beliefs);
            Assert.IsTrue(beliefs.StrengthOf(PersonBeliefKind.Rejecting) > beliefs.StrengthOf(PersonBeliefKind.Warm));
            Assert.IsTrue(beliefs.StrengthOf(PersonBeliefKind.Rejecting) > 0.10);
        }

        [TestMethod]
        public void MemoryInfluenceEngine_UsesSemanticBeliefs_ToBiasReachOut()
        {
            var socialBoost = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [new HumanId(Guid.NewGuid())] = BeliefSet(
                    new Dictionary<PersonBeliefKind, double>
                    {
                        [PersonBeliefKind.Warm] = 0.8,
                        [PersonBeliefKind.EmotionallySafe] = 0.7,
                        [PersonBeliefKind.Reliable] = 0.6
                    })
            });

            var socialPenalty = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [new HumanId(Guid.NewGuid())] = BeliefSet(
                    new Dictionary<PersonBeliefKind, double>
                    {
                        [PersonBeliefKind.Rejecting] = 0.8,
                        [PersonBeliefKind.Critical] = 0.7
                    })
            });

            var boostContext = BuildBehaviorContext(socialBoost);
            var penaltyContext = BuildBehaviorContext(socialPenalty);

            var boosted = new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) };
            var penalized = new List<BehaviorCandidate> { new(ReachOut, 10, WTimeSpan.FromHours(1), BehaviorDomain.Social) };

            var engine = new MemoryInfluenceEngine();
            engine.Modify(boostContext, boosted);
            engine.Modify(penaltyContext, penalized);

            Assert.IsTrue(boosted[0].Utility > penalized[0].Utility);
        }

        [TestMethod]
        public void ExpectedAcceptanceAndSelector_DifferByTargetBeliefs()
        {
            var warmTarget = new HumanId(Guid.NewGuid());
            var rejectingTarget = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [warmTarget] = BeliefSet(
                    new Dictionary<PersonBeliefKind, double>
                    {
                        [PersonBeliefKind.Warm] = 0.9,
                        [PersonBeliefKind.EmotionallySafe] = 0.8,
                        [PersonBeliefKind.Reliable] = 0.6
                    },
                    warmTarget),
                [rejectingTarget] = BeliefSet(
                    new Dictionary<PersonBeliefKind, double>
                    {
                        [PersonBeliefKind.Rejecting] = 0.9,
                        [PersonBeliefKind.Critical] = 0.7
                    },
                    rejectingTarget)
            });

            var edge = new RelationshipEdge(default, default, 55, 55, 20, 50, 50, 15, 10, 18, 50, 50, new DomainBreakdown(50, 50, 50, 50, 50), 2);
            var surface = new InteractionSurface("Village", false, 0.2, 0.2, SurfaceKind.Social);

            var warmAcceptance = semantic.ExpectedAcceptance(warmTarget, SpeechAct.SelfDisclosure);
            var rejectingAcceptance = semantic.ExpectedAcceptance(rejectingTarget, SpeechAct.SelfDisclosure);

            Assert.IsTrue(warmAcceptance > rejectingAcceptance);

            var warmCounts = SampleActs(edge, surface, semantic, warmTarget, 300);
            var rejectingCounts = SampleActs(edge, surface, semantic, rejectingTarget, 300);

            var warmVulnerable = Count(warmCounts, SpeechAct.Validation) + Count(warmCounts, SpeechAct.SelfDisclosure) + Count(warmCounts, SpeechAct.Meta);
            var rejectingVulnerable = Count(rejectingCounts, SpeechAct.Validation) + Count(rejectingCounts, SpeechAct.SelfDisclosure) + Count(rejectingCounts, SpeechAct.Meta);

            Assert.IsTrue(warmVulnerable > rejectingVulnerable);
        }

        private static DefaultSemanticMemoryEngine BuildEngine()
            => new(Options.Create(new SemanticMemoryConfig()));

        private static IHumanContext BuildContext(HumanId self, SemanticMemoryState? semantic = null)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentStyle.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(new Name { Original = "A", Familiar = new[] { "A" } }, new Surname { Male = "B", Female = "B" }, WDateOnly.New(100, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = new EnginesSnapshot(
                    new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                    new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                    new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>()),
                    semantic ?? SemanticMemoryState.Empty),
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("SemanticTests"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static BehaviorContext BuildBehaviorContext(SemanticMemoryState semantic)
        {
            var human = BuildContext(new HumanId(Guid.NewGuid()), semantic);
            var state = new BehaviorState(10, 5, 5, 20, 50, 30, null);
            return new BehaviorContext(new WDateTime(0), WTimeSpan.FromHours(1), human, new EventCollector(), state, new BehaviorConfig(), new Dictionary<string, double>());
        }

        private static PersonBeliefSet BeliefSet(Dictionary<PersonBeliefKind, double> beliefs, HumanId? other = null)
        {
            var person = other ?? new HumanId(Guid.NewGuid());
            return new PersonBeliefSet(
                person,
                beliefs.ToDictionary(
                    entry => entry.Key,
                    entry => new PersonBelief(person, entry.Key, entry.Value, 0.5, 3, new WDateTime(0), "seed")));
        }

        private static Dictionary<SpeechAct, int> SampleActs(
            RelationshipEdge edge,
            InteractionSurface surface,
            SemanticMemoryState semantic,
            HumanId target,
            int draws)
        {
            var rng = new Random(12345);
            var counts = new Dictionary<SpeechAct, int>();

            for (var i = 0; i < draws; i++)
            {
                var act = ReachOutSpeechActSelector.SelectSpeechAct(edge, surface, rng, semantic, target).Act;
                counts[act] = counts.TryGetValue(act, out var count) ? count + 1 : 1;
            }

            return counts;
        }

        private static int Count(IReadOnlyDictionary<SpeechAct, int> counts, SpeechAct act)
            => counts.TryGetValue(act, out var count) ? count : 0;
    }
}
