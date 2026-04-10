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
        public void Handle_RepeatedRejectingPattern_OutweighsOneOffSignal()
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

            engine.Handle(new MemoryEncoded(
                new WDateTime(10),
                self,
                Guid.NewGuid(),
                0.7,
                "Interaction:Validation:accepted",
                "PerceivedWarmth:Interaction:Validation:accepted",
                other,
                new PersonBeliefEvidence(other, PersonBeliefKind.Warm, 0.18, "test-warm")), ctx, outbox);

            var belief = engine.State.GetBeliefs(other)?.Beliefs[PersonBeliefKind.Rejecting];
            Assert.IsNotNull(belief);
            Assert.IsTrue(belief.Strength > 0.18);
            Assert.IsTrue(belief.EvidenceCount >= 4);
        }

        [TestMethod]
        public void Handle_RepeatedContradiction_WeakensEstablishedRejectingBelief()
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

            var baseline = engine.State.GetBeliefs(other);
            Assert.IsNotNull(baseline);
            var rejectingBefore = baseline.StrengthOf(PersonBeliefKind.Rejecting);

            for (var i = 0; i < 3; i++)
            {
                engine.Handle(new MemoryEncoded(
                    new WDateTime(10 + i),
                    self,
                    Guid.NewGuid(),
                    0.7,
                    "Interaction:Validation:accepted",
                    "PerceivedWarmth:Interaction:Validation:accepted",
                    other,
                    new PersonBeliefEvidence(other, PersonBeliefKind.Warm, 0.18, "test-warm")), ctx, new EventCollector());
            }

            var beliefs = engine.State.GetBeliefs(other);
            Assert.IsNotNull(beliefs);
            Assert.IsTrue(beliefs.StrengthOf(PersonBeliefKind.Rejecting) < rejectingBefore);
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
        public void SemanticTargeting_ChangesPreferredTarget()
        {
            var warmTarget = new HumanId(Guid.NewGuid());
            var rejectingTarget = new HumanId(Guid.NewGuid());
            var initiator = BuildHuman(
                new HumanId(Guid.NewGuid()),
                new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
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
                }),
                relationships: new Dictionary<HumanId, RelationshipEdge>
                {
                    [warmTarget] = BuildRelationshipEdge(warmTarget, trust: 68, comfort: 66, closeness: 18, familiarity: 22),
                    [rejectingTarget] = BuildRelationshipEdge(rejectingTarget, trust: 42, comfort: 40, closeness: 8, familiarity: 18)
                });

            var warmHuman = BuildHuman(warmTarget);
            var rejectingHuman = BuildHuman(rejectingTarget);

            var selected = SemanticTargeting.ChooseTarget(initiator, new[] { rejectingHuman, warmHuman }, SocialTargetMode.ReachOut);

            Assert.IsNotNull(selected);
            Assert.AreEqual(warmTarget, selected.Id);
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

        [TestMethod]
        public void ExpectedAcceptance_IsPsychologySensitive()
        {
            var other = new HumanId(Guid.NewGuid());
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [other] = BeliefSet(
                    new Dictionary<PersonBeliefKind, double>
                    {
                        [PersonBeliefKind.Warm] = 0.65,
                        [PersonBeliefKind.EmotionallySafe] = 0.55,
                        [PersonBeliefKind.Rejecting] = 0.20
                    },
                    other)
            });

            var edge = BuildRelationshipEdge(other, trust: 58, comfort: 56, closeness: 14, familiarity: 20);
            var guarded = new PsychologicalProfile(CopingStyle.Avoidant, new SelfNarrative(0.5, 0.95, 0.25), 0.3, 0.7);
            var affiliative = new PsychologicalProfile(CopingStyle.PeoplePleasing, new SelfNarrative(0.5, 0.35, 0.95), 0.3, 0.7);

            var guardedExpected = semantic.ExpectedAcceptance(other, SpeechAct.SelfDisclosure, edge, guarded);
            var affiliativeExpected = semantic.ExpectedAcceptance(other, SpeechAct.SelfDisclosure, edge, affiliative);

            Assert.IsTrue(affiliativeExpected > guardedExpected);
        }

        [TestMethod]
        public void SemanticTargeting_IsDeterministic()
        {
            var a = new HumanId(Guid.NewGuid());
            var b = new HumanId(Guid.NewGuid());
            var initiator = BuildHuman(
                new HumanId(Guid.NewGuid()),
                new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
                {
                    [a] = BeliefSet(new Dictionary<PersonBeliefKind, double> { [PersonBeliefKind.Warm] = 0.72 }, a),
                    [b] = BeliefSet(new Dictionary<PersonBeliefKind, double> { [PersonBeliefKind.Warm] = 0.72 }, b)
                }),
                relationships: new Dictionary<HumanId, RelationshipEdge>
                {
                    [a] = BuildRelationshipEdge(a, trust: 60, comfort: 55, closeness: 10, familiarity: 15),
                    [b] = BuildRelationshipEdge(b, trust: 60, comfort: 55, closeness: 10, familiarity: 15)
                });

            var selected1 = SemanticTargeting.ChooseTarget(initiator, new[] { BuildHuman(b), BuildHuman(a) }, SocialTargetMode.ReachOut);
            var selected2 = SemanticTargeting.ChooseTarget(initiator, new[] { BuildHuman(b), BuildHuman(a) }, SocialTargetMode.ReachOut);

            Assert.IsNotNull(selected1);
            Assert.IsNotNull(selected2);
            Assert.AreEqual(selected1.Id, selected2.Id);
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
                    new MemoryIndex(new List<EpisodicMemory>()),
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

        private static IHuman BuildHuman(
            HumanId id,
            SemanticMemoryState? semantic = null,
            Dictionary<HumanId, RelationshipEdge>? relationships = null,
            PsychologicalProfile? profile = null)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentStyle.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            return new LocalHuman(
                id,
                personality,
                profile ?? PsychologicalProfile.FromPersonality(personality),
                new EnginesSnapshot(
                    new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                    new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                    new RelationshipState(relationships ?? new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>()),
                    semantic ?? SemanticMemoryState.Empty));
        }

        private static RelationshipEdge BuildRelationshipEdge(
            HumanId other,
            double trust,
            double comfort,
            double closeness,
            double familiarity)
            => new(default, other, 55, trust, familiarity, 50, 50, 15, 10, closeness, 50, comfort, new DomainBreakdown(50, 50, 50, 50, 50), 2);

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

        private sealed class LocalHuman : IHuman
        {
            public LocalHuman(HumanId id, Personality personality, PsychologicalProfile profile, EnginesSnapshot snapshot)
            {
                Id = id;
                Personality = personality;
                PsychologyProfile = profile;
                Snapshot = snapshot;
                Identity = new Identity(new Name { Original = "A", Familiar = new[] { "A" } }, new Surname { Male = "B", Female = "B" }, WDateOnly.New(100, 1, 1));
            }

            public HumanId Id { get; }
            public Identity Identity { get; }
            public SexBiology Biology => SexBiology.Female;
            public Personality Personality { get; }
            public PsychologicalProfile PsychologyProfile { get; }
            public PhysicalAppearance PhysicalAppearance => new(
                170,
                BodyFrame.Medium,
                SkinTone.Light,
                EyeColor.Brown,
                HairColorNatural.Brown,
                HairType.Wavy,
                FaceShape.Oval,
                42,
                38,
                0.5,
                0.5);
            public AttractionProfile AttractionProfile => null!;
            public EnginesSnapshot Snapshot { get; private set; }
            public IReadOnlyList<IDomainEvent> LastOutbox => Array.Empty<IDomainEvent>();
            public int Age => 20;
            public void Tick(WDateTime now, WTimeSpan dt) { }
            public void ReceiveEvent(IDomainEvent @event) { }
            public void RestoreSnapshot(EnginesSnapshot snapshot) => Snapshot = snapshot;
        }
    }
}
