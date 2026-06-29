// FFFSTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for Subsystem D (FFFS): the proximal-threat-gated <see cref="FFFSEscapeModifier"/> (escape
    /// boost / cautious-approach suppression, fired only by a present hazard object and NOT by chronic
    /// stress) and the gender-shifted <see cref="FFFSGenerator"/>.
    /// </summary>
    [TestClass]
    public class FFFSTests : TestBase
    {
        #region Modifier — gating

        [TestMethod]
        public void FFFS_Disabled_IsNoOp()
        {
            // Default config has FFFSEnabled = false — no change even with a hazard and an escape candidate.
            var candidates = EscapeAndApproach();
            var ctx = BuildContext(
                fffs: new FFFSProfile(0.8),
                availableObjects: new[] { HazardObject(0.8) });

            new FFFSEscapeModifier().Modify(ctx, candidates);

            Assert.AreEqual(20.0, candidates.First(c => c.Name == Flee).Utility, 0.001);
            Assert.AreEqual(40.0, candidates.First(c => c.Name == ReachOut).Utility, 0.001);
        }

        [TestMethod]
        public void FFFS_NoThreatDetected_IsNoOp()
        {
            // Enabled, profile present, but no hazard object in the location → fast system stays dormant.
            var cfg = new BehaviorConfig(FFFSEnabled: true);
            var candidates = EscapeAndApproach();
            var ctx = BuildContext(fffs: new FFFSProfile(0.8), availableObjects: null, config: cfg);

            new FFFSEscapeModifier().Modify(ctx, candidates);

            Assert.AreEqual(20.0, candidates.First(c => c.Name == Flee).Utility, 0.001);
            Assert.AreEqual(40.0, candidates.First(c => c.Name == ReachOut).Utility, 0.001);
        }

        [TestMethod]
        public void FFFS_ProximalThreat_BoostsEscapeCandidates()
        {
            var cfg = new BehaviorConfig(FFFSEnabled: true);
            var candidates = EscapeAndApproach();
            var ctx = BuildContext(fffs: new FFFSProfile(0.5), availableObjects: new[] { HazardObject(0.8) }, config: cfg);

            new FFFSEscapeModifier().Modify(ctx, candidates);

            // urgency = threat(0.8) × sensitivity(0.5) × magnitude(15) = 6.0
            Assert.AreEqual(26.0, candidates.First(c => c.Name == Flee).Utility, 0.001,
                "Escape (Flee) is boosted by the escape urgency.");
        }

        [TestMethod]
        public void FFFS_ProximalThreat_SuppressesCautiousApproach()
        {
            var cfg = new BehaviorConfig(FFFSEnabled: true);
            var candidates = EscapeAndApproach();
            var ctx = BuildContext(fffs: new FFFSProfile(0.5), availableObjects: new[] { HazardObject(0.8) }, config: cfg);

            new FFFSEscapeModifier().Modify(ctx, candidates);

            // suppression = urgency(6.0) × 0.5 = 3.0 → 40 - 3 = 37
            Assert.AreEqual(37.0, candidates.First(c => c.Name == ReachOut).Utility, 0.001,
                "Cautious-approach (ReachOut) is suppressed under active FFFS.");
        }

        [TestMethod]
        public void FFFS_HighSensitivity_StrongerResponse_ThanLowSensitivity()
        {
            var cfg = new BehaviorConfig(FFFSEnabled: true);

            var high = EscapeAndApproach();
            new FFFSEscapeModifier().Modify(
                BuildContext(new FFFSProfile(0.9), new[] { HazardObject(0.8) }, cfg), high);

            var low = EscapeAndApproach();
            new FFFSEscapeModifier().Modify(
                BuildContext(new FFFSProfile(0.1), new[] { HazardObject(0.8) }, cfg), low);

            Assert.IsTrue(
                high.First(c => c.Name == Flee).Utility > low.First(c => c.Name == Flee).Utility,
                "Higher FFFS sensitivity produces a stronger escape boost on the same threat.");
        }

        [TestMethod]
        public void FFFS_DoesNotFireOnChronicStress_OnlyOnProximalThreat()
        {
            // KEY redundancy-audit guard: high chronic Stress but NO hazard object → FFFS must NOT fire.
            // (Chronic stress is AffectiveStateEngine's territory; FFFS is proximal-threat only.)
            var cfg = new BehaviorConfig(FFFSEnabled: true);
            var candidates = EscapeAndApproach();
            var stressedNoHazard = BuildContext(
                fffs: new FFFSProfile(0.9),
                availableObjects: null,   // no proximal hazard present
                config: cfg,
                stress: 95.0);            // chronic stress is high...

            new FFFSEscapeModifier().Modify(stressedNoHazard, candidates);

            Assert.AreEqual(20.0, candidates.First(c => c.Name == Flee).Utility, 0.001,
                "FFFS must NOT fire on chronic stress alone — only on a proximal hazard signal.");

            // Sanity: the SAME character WITH a present hazard does fire.
            var withHazard = EscapeAndApproach();
            new FFFSEscapeModifier().Modify(
                BuildContext(new FFFSProfile(0.9), new[] { HazardObject(0.8) }, cfg, stress: 95.0), withHazard);
            Assert.IsTrue(withHazard.First(c => c.Name == Flee).Utility > 20.0,
                "With a proximal hazard present, FFFS fires.");
        }

        #endregion

        #region Generator

        [TestMethod]
        public void FFFS_Female_HasHigherDefaultSensitivity_ThanMale()
        {
            // Deterministic (ZeroRandom → zero noise): female receives the +0.10 gender shift.
            var rng = new ZeroRandom();
            var bigFive = new BigFive(0.5, 0.5, 0.5, 0.5, 0.5);

            var female = FFFSGenerator.Generate(rng, bigFive, SexBiology.Female);
            var male = FFFSGenerator.Generate(rng, bigFive, SexBiology.Male);

            Assert.IsTrue(female.Sensitivity > male.Sensitivity,
                $"Females score higher on FFFS (Corr & Cooper 2016). Female={female.Sensitivity:F3}, Male={male.Sensitivity:F3}");
        }

        [TestMethod]
        public void FFFS_Generator_HigherNeuroticism_RaisesSensitivity()
        {
            var rng = new ZeroRandom();
            var lowN = FFFSGenerator.Generate(rng, new BigFive(0.5, 0.5, 0.5, 0.5, 0.1), SexBiology.Male);
            var highN = FFFSGenerator.Generate(rng, new BigFive(0.5, 0.5, 0.5, 0.5, 0.9), SexBiology.Male);

            Assert.IsTrue(highN.Sensitivity > lowN.Sensitivity,
                "FFFS bridges to the Neuroticism fear-facet: higher N → higher sensitivity.");
        }

        #endregion

        #region Helpers

        private static List<BehaviorCandidate> EscapeAndApproach() => new()
        {
            new(Flee, 20.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological),
            new(ReachOut, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social)
        };

        private static WorldObject HazardObject(double stressRaise) => new WorldObject
        {
            Id = "hazard_01",
            DisplayName = "blade",
            Category = WorldObjectCategory.Furniture,
            LocationId = "test",
            IsAvailable = true,
            Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.StressRaise, stressRaise))
        };

        private static BehaviorContext BuildContext(
            FFFSProfile? fffs,
            IReadOnlyList<WorldObject>? availableObjects = null,
            BehaviorConfig? config = null,
            double stress = 10.0)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                FFFS: fffs);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0, 0.5, 0.5, stress, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            var human = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("FFFS"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            var state = new BehaviorState(10, 5, 5, 20, 50, 30,
                new PlannedAction(Idle, new WDateTime(0), WTimeSpan.FromHours(1), 50.0));

            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                config ?? new BehaviorConfig(),
                new Dictionary<string, double>(),
                AvailableObjects: availableObjects);
        }

        #endregion
    }
}
