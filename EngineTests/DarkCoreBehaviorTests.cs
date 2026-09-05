// DarkCoreBehaviorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Social;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for <see cref="DarkCoreModifier"/> and the dark-core cross-link with
    /// <see cref="SocialComparisonMath"/> — ensuring high DarkCore boosts antagonistic
    /// actions, penalises prosocial ones, and amplifies malicious envy.
    /// </summary>
    [TestClass]
    public class DarkCoreBehaviorTests : TestBase
    {
        #region Test 1 — High DarkCore → higher Fight utility than low DarkCore

        [TestMethod]
        public void HighDarkCore_RaisesFightUtility_AboveLowDarkCore()
        {
            const double baseUtility = 40.0;

            var highDCCandidates = new List<BehaviorCandidate>
            {
                new(Fight, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
            };
            var lowDCCandidates = new List<BehaviorCandidate>
            {
                new(Fight, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
            };

            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.9), highDCCandidates);
            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.1), lowDCCandidates);

            Assert.IsTrue(highDCCandidates[0].Utility > lowDCCandidates[0].Utility,
                $"High DarkCore Fight utility ({highDCCandidates[0].Utility:F2}) must exceed " +
                $"low DarkCore ({lowDCCandidates[0].Utility:F2}).");
        }

        #endregion Test 1 — High DarkCore → higher Fight utility than low DarkCore

        #region Test 2 — High DarkCore → lower ReachOut utility than low DarkCore

        [TestMethod]
        public void HighDarkCore_ReducesReachOutUtility_BelowLowDarkCore()
        {
            const double baseUtility = 50.0;

            var highDCCandidates = new List<BehaviorCandidate>
            {
                new(ReachOut, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };
            var lowDCCandidates = new List<BehaviorCandidate>
            {
                new(ReachOut, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.9), highDCCandidates);
            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.1), lowDCCandidates);

            Assert.IsTrue(highDCCandidates[0].Utility < lowDCCandidates[0].Utility,
                $"High DarkCore ReachOut utility ({highDCCandidates[0].Utility:F2}) must be lower " +
                $"than low DarkCore ({lowDCCandidates[0].Utility:F2}).");
        }

        #endregion Test 2 — High DarkCore → lower ReachOut utility than low DarkCore

        #region Test 3 — High DarkCore → lower InviteIntimacy utility than low DarkCore

        [TestMethod]
        public void HighDarkCore_ReducesInviteIntimacyUtility_BelowLowDarkCore()
        {
            const double baseUtility = 50.0;

            var highDCCandidates = new List<BehaviorCandidate>
            {
                new(InviteIntimacy, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };
            var lowDCCandidates = new List<BehaviorCandidate>
            {
                new(InviteIntimacy, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.9), highDCCandidates);
            new DarkCoreModifier().Modify(BuildContext(darkCore: 0.1), lowDCCandidates);

            Assert.IsTrue(highDCCandidates[0].Utility < lowDCCandidates[0].Utility,
                $"High DarkCore InviteIntimacy utility ({highDCCandidates[0].Utility:F2}) must be lower " +
                $"than low DarkCore ({lowDCCandidates[0].Utility:F2}).");
        }

        #endregion Test 3 — High DarkCore → lower InviteIntimacy utility than low DarkCore

        #region Test 4 — Monotonicity: antagonism boost strictly increases with DarkCore

        [TestMethod]
        public void FightUtility_StrictlyIncreases_AcrossDarkCoreValues()
        {
            const double baseUtility = 40.0;
            var levels = new[] { 0.1, 0.5, 0.9 };

            double[] utilities = levels.Select(dc =>
            {
                var candidates = new List<BehaviorCandidate>
                {
                    new(Fight, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
                };
                new DarkCoreModifier().Modify(BuildContext(darkCore: dc), candidates);
                return candidates[0].Utility;
            }).ToArray();

            for (var i = 1; i < utilities.Length; i++)
            {
                Assert.IsTrue(utilities[i] > utilities[i - 1],
                    $"Fight utility must strictly increase with DarkCore. " +
                    $"DarkCore={levels[i - 1]:F1}→{levels[i]:F1}: {utilities[i - 1]:F2}→{utilities[i]:F2}");
            }
        }

        #endregion Test 4 — Monotonicity: antagonism boost strictly increases with DarkCore

        #region Test 5 — Null DarkCore → no change (no-op)

        [TestMethod]
        public void NullDarkCore_IsNoOp_UtilityUnchanged()
        {
            const double baseUtility = 50.0;
            var candidates = new List<BehaviorCandidate>
            {
                new(Fight,          baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Physiological),
                new(ReachOut,       baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social),
                new(InviteIntimacy, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social),
            };

            // Build context with null DarkCore in Personality.
            new DarkCoreModifier().Modify(BuildContextNullDarkCore(), candidates);

            foreach (var c in candidates)
            {
                Assert.AreEqual(baseUtility, c.Utility, 0.001,
                    $"Null DarkCore must leave {c.Name} utility unchanged at {baseUtility:F1}. Got {c.Utility:F2}.");
            }
        }

        #endregion Test 5 — Null DarkCore → no change (no-op)

        #region Test 6 — SocialComparisonMath: high darkCore amplifies malicious hostility

        [TestMethod]
        public void SocialComparisonMath_HighDarkCore_AmplifiesMaliciousHostility()
        {
            // Set up an upward-contrast scenario guaranteed to cross the malicious-envy threshold.
            // Low agreeableness + large unattainable gap → malicious envy.
            var cfg = new SocialComparisonConfig(
                MinSalientGap: 5.0,
                AttainabilityGap: 10.0,     // gap=40 is unattainable → contrast
                IdentificationCloseness: 50.0,
                MaliciousEnvyDispositionWeight: 0.9,
                MaliciousEnvyThreshold: 0.10, // low threshold to ensure malicious
                MaliciousEnvyHostilityWeight: 6.0,
                DarkCoreMaliciousAmplification: 0.5);

            const double selfStanding = 30.0;
            const double targetStanding = 70.0;  // gap = 40 > AttainabilityGap
            const double closeness = 20.0;  // below IdentificationCloseness
            const double neuroticism = 0.5;
            const double agreeableness = 0.1;   // low A → high malicious disposition
            const double selfEsteem = 0.5;

            var resultNoDark = SocialComparisonMath.Evaluate(
                selfStanding, targetStanding, closeness,
                neuroticism, agreeableness, selfEsteem, cfg,
                darkCore: 0.0);

            var resultHighDark = SocialComparisonMath.Evaluate(
                selfStanding, targetStanding, closeness,
                neuroticism, agreeableness, selfEsteem, cfg,
                darkCore: 0.9);

            Assert.IsTrue(resultHighDark.TargetHostilityDelta > resultNoDark.TargetHostilityDelta,
                $"High DarkCore hostility ({resultHighDark.TargetHostilityDelta:F3}) must exceed " +
                $"no-dark-core hostility ({resultNoDark.TargetHostilityDelta:F3}).");
        }

        #endregion Test 6 — SocialComparisonMath: high darkCore amplifies malicious hostility

        #region Test 7 — SocialComparisonMath: default darkCore=0 does not change existing behaviour

        [TestMethod]
        public void SocialComparisonMath_DefaultDarkCore_ZeroAmplification_NoChange()
        {
            var cfg = new SocialComparisonConfig(
                MinSalientGap: 5.0,
                AttainabilityGap: 10.0,
                IdentificationCloseness: 50.0,
                MaliciousEnvyDispositionWeight: 0.9,
                MaliciousEnvyThreshold: 0.10,
                MaliciousEnvyHostilityWeight: 6.0,
                DarkCoreMaliciousAmplification: 0.5);

            const double selfStanding = 30.0;
            const double targetStanding = 70.0;
            const double closeness = 20.0;
            const double neuroticism = 0.5;
            const double agreeableness = 0.1;
            const double selfEsteem = 0.5;

            var resultDefault = SocialComparisonMath.Evaluate(
                selfStanding, targetStanding, closeness,
                neuroticism, agreeableness, selfEsteem, cfg);

            var resultExplicitZero = SocialComparisonMath.Evaluate(
                selfStanding, targetStanding, closeness,
                neuroticism, agreeableness, selfEsteem, cfg,
                darkCore: 0.0);

            Assert.AreEqual(resultDefault.TargetHostilityDelta,
                            resultExplicitZero.TargetHostilityDelta, 1e-9,
                "Omitting darkCore (default 0.0) must produce identical hostility to explicit 0.0.");
        }

        #endregion Test 7 — SocialComparisonMath: default darkCore=0 does not change existing behaviour

        #region Helpers

        /// <summary>
        /// Builds a minimal <see cref="BehaviorContext"/> with the specified DarkCore value.
        /// Mirrors the pattern in <see cref="LossAversionTests"/>.
        /// </summary>
        private static BehaviorContext BuildContext(double darkCore, BehaviorConfig? config = null)
        {
            var darkCoreProfile = new DarkCoreProfile(darkCore, 0.5);
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                DarkCore: darkCoreProfile);

            return BuildContextWithPersonality(personality, config);
        }

        /// <summary>
        /// Builds a <see cref="BehaviorContext"/> with null <c>DarkCore</c> in <see cref="Personality"/>
        /// — used to test the no-op branch.
        /// </summary>
        private static BehaviorContext BuildContextNullDarkCore(BehaviorConfig? config = null)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                DarkCore: null);   // explicitly null

            return BuildContextWithPersonality(personality, config);
        }

        private static BehaviorContext BuildContextWithPersonality(Personality personality, BehaviorConfig? config)
        {
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
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
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("DarkCore"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                config ?? new BehaviorConfig(),
                new Dictionary<string, double>());
        }

        #endregion Helpers
    }
}
