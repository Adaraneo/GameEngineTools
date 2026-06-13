// ValuesProfileTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Values;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for <see cref="ValuesProfileGenerator"/>, <see cref="ActionValueLoadings"/>,
    /// <see cref="ValueLoadVector"/>, <see cref="ValuesBehaviorModifier"/>, and integration
    /// with <see cref="DefaultPsychologyEngine"/>.
    /// </summary>
    [TestClass]
    public class ValuesProfileTests : TestBase
    {
        #region Test 1 — High Agreeableness → High Benevolence

        [TestMethod]
        public void ValuesProfileGenerator_HighAgreeableness_HighBenevolence()
        {
            // Arrange — A=0.9, O=0.5, E=0.5, C=0.5
            var bigFive = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                     Extraversion: 0.5, Agreeableness: 0.9, Neuroticism: 0.5);

            // Act — deterministic (null random)
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — Benevolence is elevated; after ipsatization should be > 0.60.
            // Threshold lowered from 0.70 after the Parks-Leduc 2015 recalibration (ρ .61→.43).
            Assert.IsTrue(profile.Benevolence > 0.60,
                $"High A should produce Benevolence > 0.60. Got: {profile.Benevolence:F3}");
        }

        #endregion

        #region Test 2 — Low Agreeableness → Low Benevolence

        [TestMethod]
        public void ValuesProfileGenerator_LowAgreeableness_LowBenevolence()
        {
            // Arrange — A=0.1
            var bigFive = new BigFive(Openness: 0.5, Conscientiousness: 0.5,
                                     Extraversion: 0.5, Agreeableness: 0.1, Neuroticism: 0.5);

            // Act
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — Benevolence < 0.40 (threshold widened from 0.35 after recalibration ρ .61→.43).
            Assert.IsTrue(profile.Benevolence < 0.40,
                $"Low A should produce Benevolence < 0.40. Got: {profile.Benevolence:F3}");
        }

        #endregion

        #region Test 3 — High Openness → High SelfDirection

        [TestMethod]
        public void ValuesProfileGenerator_HighOpenness_HighSelfDirection()
        {
            // Arrange — O=0.95, others=0.5
            var bigFive = new BigFive(Openness: 0.95, Conscientiousness: 0.5,
                                     Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);

            // Act
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — SelfDirection > 0.62 (ρ=.42 from Parks-Leduc et al. 2015; was .52/0.70).
            Assert.IsTrue(profile.SelfDirection > 0.62,
                $"High O should produce SelfDirection > 0.62. Got: {profile.SelfDirection:F3}");
        }

        #endregion

        #region Test 4 — High Extraversion, Low Agreeableness → Power > Achievement

        [TestMethod]
        public void ValuesProfileGenerator_HighExtraversion_PowerExceedsAchievement()
        {
            // Arrange — E=0.9, A=0.1, C=0.3 (low A drives Power up, low C barely dents Achievement)
            var bigFive = new BigFive(Openness: 0.5, Conscientiousness: 0.3,
                                     Extraversion: 0.9, Agreeableness: 0.1, Neuroticism: 0.5);

            // Act
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — Power dominates (E+ and A- both drive Power)
            Assert.IsTrue(profile.Power > profile.Achievement,
                $"High E + Low A should produce Power > Achievement. " +
                $"Power={profile.Power:F3}, Achievement={profile.Achievement:F3}");
        }

        #endregion

        #region Test 5 — High Conscientiousness, Low Openness → High Security

        [TestMethod]
        public void ValuesProfileGenerator_HighConscientiousness_HighSecurity()
        {
            // Arrange — C=0.9, O=0.2 (C+ and O- both drive Security)
            var bigFive = new BigFive(Openness: 0.2, Conscientiousness: 0.9,
                                     Extraversion: 0.5, Agreeableness: 0.5, Neuroticism: 0.5);

            // Act
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — Security > 0.60 (threshold lowered from 0.65 after recalibration ρ .37→.21).
            Assert.IsTrue(profile.Security > 0.60,
                $"High C + Low O should produce Security > 0.60. Got: {profile.Security:F3}");
        }

        #endregion

        #region Test 6 — Low Openness, Mid-High Agreeableness → High Tradition

        [TestMethod]
        public void ValuesProfileGenerator_LowOpenness_HighTradition()
        {
            // Arrange — O=0.1, A=0.6 (O− is primary driver of Tradition)
            var bigFive = new BigFive(Openness: 0.1, Conscientiousness: 0.5,
                                     Extraversion: 0.5, Agreeableness: 0.6, Neuroticism: 0.5);

            // Act
            var profile = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — Tradition > 0.60
            Assert.IsTrue(profile.Tradition > 0.60,
                $"Low O + Moderate A should produce Tradition > 0.60. Got: {profile.Tradition:F3}");
        }

        #endregion

        #region Test 7 — Ipsatization: mean of all 10 values ≈ 0.5

        [TestMethod]
        public void ValuesProfileGenerator_Ipsatization_MeanApproximately0Point5()
        {
            // Arrange — any BigFive
            var bigFive = new BigFive(Openness: 0.7, Conscientiousness: 0.3,
                                     Extraversion: 0.8, Agreeableness: 0.4, Neuroticism: 0.6);

            // Act
            var p = ValuesProfileGenerator.Generate(bigFive, random: null);

            var mean = (p.Benevolence + p.Universalism + p.SelfDirection + p.Stimulation +
                        p.Hedonism + p.Achievement + p.Power + p.Security +
                        p.Conformity + p.Tradition) / 10.0;

            // Assert — within 0.02 of 0.5 (ipsatization removes scale-use bias)
            Assert.IsTrue(Math.Abs(mean - 0.5) < 0.02,
                $"Ipsatized values should have mean ≈ 0.5. Got: {mean:F4}");
        }

        #endregion

        #region Test 8 — Deterministic without noise

        [TestMethod]
        public void ValuesProfileGenerator_Deterministic_WithNullRandom()
        {
            // Arrange
            var bigFive = new BigFive(Openness: 0.4, Conscientiousness: 0.7,
                                     Extraversion: 0.6, Agreeableness: 0.3, Neuroticism: 0.5);

            // Act — two calls, null random
            var p1 = ValuesProfileGenerator.Generate(bigFive, random: null);
            var p2 = ValuesProfileGenerator.Generate(bigFive, random: null);

            // Assert — byte-identical
            Assert.AreEqual(p1, p2,
                "Null-random generation must be deterministic (no noise).");
        }

        #endregion

        #region Test 8b — Coefficient magnitudes stay within meta-analytic bounds

        [TestMethod]
        public void ValuesProfileGenerator_AllCoefficients_WithinMetaAnalyticUpperBound()
        {
            // Each regression coefficient must not exceed (in magnitude) the cited Parks-Leduc et al.
            // (2015) meta-analytic ceiling. Guards against silent re-inflation of personality→value
            // coupling.
            foreach (var (name, coefficient, upperBound) in ValuesProfileGenerator.CoefficientAudit)
            {
                Assert.IsTrue(Math.Abs(coefficient) <= upperBound + 1e-9,
                    $"Coefficient {name} magnitude {Math.Abs(coefficient):F3} exceeds meta-analytic " +
                    $"upper bound {upperBound:F3}.");
            }
        }

        #endregion

        #region Test 9 — ActionValueLoadings: Work has positive Achievement

        [TestMethod]
        public void ActionValueLoadings_WorkAction_PositiveAchievement()
        {
            var loading = ActionValueLoadings.Get(ActionNames.Work);

            Assert.IsTrue(loading.Achievement > 0,
                $"Work should have positive Achievement loading. Got: {loading.Achievement:F2}");
        }

        #endregion

        #region Test 10 — ActionValueLoadings: InviteIntimacy has negative Conformity

        [TestMethod]
        public void ActionValueLoadings_InviteIntimacy_NegativeConformity()
        {
            var loading = ActionValueLoadings.Get(ActionNames.InviteIntimacy);

            Assert.IsTrue(loading.Conformity < 0,
                $"InviteIntimacy should have negative Conformity loading. Got: {loading.Conformity:F2}");
        }

        #endregion

        #region Test 11 — ValueLoadVector: ReachOut congruent with high-Benevolence profile

        [TestMethod]
        public void ValueLoadVector_Congruence_HighBenevolenceProfile_ReachOutPositive()
        {
            // Arrange — profile that strongly values Benevolence
            var profile = new ValuesProfile(
                Benevolence: 0.8, Universalism: 0.5, SelfDirection: 0.5,
                Stimulation: 0.5, Hedonism: 0.5, Achievement: 0.5,
                Power: 0.5, Security: 0.5, Conformity: 0.5, Tradition: 0.5);

            var loading = ActionValueLoadings.Get(ActionNames.ReachOut);

            // Act
            var congruence = loading.Congruence(profile);

            // Assert — ReachOut strongly loads Benevolence (+0.50), should be positive
            Assert.IsTrue(congruence > 0,
                $"ReachOut should be congruent with high-Benevolence profile. Got: {congruence:F3}");
        }

        #endregion

        #region Test 12 — ValuesBehaviorModifier: high Benevolence boosts ReachOut utility

        [TestMethod]
        public void ValuesBehaviorModifier_HighBenevolenceChar_ReachOutBoosted()
        {
            // Arrange — high-Benevolence values profile
            var values = new ValuesProfile(
                Benevolence: 0.85, Universalism: 0.5, SelfDirection: 0.5,
                Stimulation: 0.5, Hedonism: 0.5, Achievement: 0.5,
                Power: 0.5, Security: 0.5, Conformity: 0.5, Tradition: 0.5);

            const double baseUtility = 50.0;
            var ctx = BuildContextWithValues(values, stress: 10, cogLoad: 20);
            var candidates = new List<BehaviorCandidate>
            {
                new(ActionNames.ReachOut, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            // Act
            new ValuesBehaviorModifier().Modify(ctx, candidates);

            // Assert — utility increased
            Assert.IsTrue(candidates[0].Utility > baseUtility,
                $"ReachOut utility should increase for high-Benevolence character. " +
                $"Before={baseUtility}, After={candidates[0].Utility:F2}");
        }

        #endregion

        #region Test 13 — ValuesBehaviorModifier: high Conformity reduces InviteIntimacy utility

        [TestMethod]
        public void ValuesBehaviorModifier_HighConformityChar_InviteIntimacyReduced()
        {
            // Arrange — high-Conformity and high-Tradition, low-Hedonism/Benevolence/Stimulation
            // (suppresses InviteIntimacy's positive loadings so the negative ones dominate)
            var values = new ValuesProfile(
                Benevolence: 0.1, Universalism: 0.5, SelfDirection: 0.5,
                Stimulation: 0.1, Hedonism: 0.05, Achievement: 0.5,
                Power: 0.5, Security: 0.5, Conformity: 0.95, Tradition: 0.95);

            const double baseUtility = 50.0;
            var ctx = BuildContextWithValues(values, stress: 10, cogLoad: 20);
            var candidates = new List<BehaviorCandidate>
            {
                new(ActionNames.InviteIntimacy, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            // Act
            new ValuesBehaviorModifier().Modify(ctx, candidates);

            // Assert — utility decreased
            Assert.IsTrue(candidates[0].Utility < baseUtility,
                $"InviteIntimacy utility should decrease for high-Conformity character. " +
                $"Before={baseUtility}, After={candidates[0].Utility:F2}");
        }

        #endregion

        #region Test 14 — ValuesBehaviorModifier: congruence < -0.30 emits ValueCongruenceViolated

        [TestMethod]
        public void ValuesBehaviorModifier_GuiltThreshold_EmitsValueCongruenceViolated()
        {
            // Arrange — high Conformity/Tradition, low Hedonism/Benevolence/Stimulation
            // so InviteIntimacy's positive loadings are suppressed by low profile weights
            var values = new ValuesProfile(
                Benevolence: 0.1, Universalism: 0.5, SelfDirection: 0.5,
                Stimulation: 0.1, Hedonism: 0.05, Achievement: 0.5,
                Power: 0.5, Security: 0.5, Conformity: 0.95, Tradition: 0.95);

            var outbox = new EventCollector();
            var ctx = BuildContextWithValues(values, stress: 10, cogLoad: 20, outbox: outbox);
            var candidates = new List<BehaviorCandidate>
            {
                new(ActionNames.InviteIntimacy, 50.0, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            // Act
            new ValuesBehaviorModifier().Modify(ctx, candidates);

            // Assert — ValueCongruenceViolated emitted
            var events = outbox.Drain();
            var violation = events.OfType<ValueCongruenceViolated>().FirstOrDefault();
            Assert.IsNotNull(violation,
                "ValueCongruenceViolated must be emitted when congruence < −0.01.");
            Assert.IsTrue(violation!.Congruence < 0.0,
                $"Emitted violation must have negative congruence. Got: {violation.Congruence:F4}");
        }

        #endregion

        #region Test 15 — ValuesBehaviorModifier: high stress attenuates value effect

        [TestMethod]
        public void ValuesBehaviorModifier_HighStress_AttenuatesValueEffect()
        {
            // Arrange — same high-Benevolence profile, same action; vary only stress
            var values = new ValuesProfile(
                Benevolence: 0.9, Universalism: 0.5, SelfDirection: 0.5,
                Stimulation: 0.5, Hedonism: 0.5, Achievement: 0.5,
                Power: 0.5, Security: 0.5, Conformity: 0.5, Tradition: 0.5);

            const double baseUtility = 50.0;
            var ctxLowStress = BuildContextWithValues(values, stress: 10, cogLoad: 20);
            var ctxHighStress = BuildContextWithValues(values, stress: 95, cogLoad: 20);

            var candidatesLow  = new List<BehaviorCandidate> { new(ActionNames.ReachOut, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social) };
            var candidatesHigh = new List<BehaviorCandidate> { new(ActionNames.ReachOut, baseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social) };

            // Act
            var modifier = new ValuesBehaviorModifier();
            modifier.Modify(ctxLowStress,  candidatesLow);
            modifier.Modify(ctxHighStress, candidatesHigh);

            var deltaLowStress  = candidatesLow[0].Utility  - baseUtility;
            var deltaHighStress = candidatesHigh[0].Utility - baseUtility;

            // Assert — high stress produces < 30% of the low-stress delta
            Assert.IsTrue(deltaHighStress < deltaLowStress * 0.30,
                $"High-stress delta ({deltaHighStress:F2}) should be < 30% of low-stress delta ({deltaLowStress:F2}).");
        }

        #endregion

        #region Test 16 — PsychologyEngine: ValueCongruenceViolated applies Guilt spike

        [TestMethod]
        public void PsychologyEngine_ValueCongruenceViolated_AppliesGuiltSpike()
        {
            // Arrange — high Agreeableness + Conscientiousness amplifies guilt
            var actor = new HumanId(Guid.NewGuid());
            var personality = MakePersonality(agreeableness: 0.9, conscientiousness: 0.8);

            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);

            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

            engine.RestoreState(new PsychologyState(
                Valence: 0.0, Arousal: 0.4, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral));

            var ctx = BuildPsychologyContext(actor, personality);
            var outbox = new EventCollector();

            var violation = new ValueCongruenceViolated(
                OccurredAt: new WDateTime(100),
                Actor: actor,
                ActionName: ActionNames.InviteIntimacy,
                Congruence: -0.50,
                DominantViolatedValue: "Conformity");

            // Act
            engine.Handle(violation, ctx, outbox);

            // Assert — Valence must drop; Dominance must NOT drop to Shame territory (D > 0.25)
            Assert.IsTrue(engine.State.Valence < 0.0,
                $"Valence must drop after guilt spike. Got: {engine.State.Valence:F3}");
            Assert.IsTrue(engine.State.Dominance > 0.25,
                $"Dominance must remain above 0.25 (guilt, not shame). Got: {engine.State.Dominance:F3}");
        }

        #endregion

        #region Test 17 — InferEmotion: correct VAD → Guilt (not Shame, not Neutral)

        [TestMethod]
        public void InferEmotion_GuiltVAD_ReturnsGuilt()
        {
            // Arrange — Guilt VAD: V=-0.50, A=0.50, D=0.35, Stress=20
            var actor = new HumanId(Guid.NewGuid());
            var personality = MakePersonality(agreeableness: 0.5, conscientiousness: 0.5);

            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);

            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

            engine.RestoreState(new PsychologyState(
                Valence: -0.50, Arousal: 0.50, Dominance: 0.35,
                Stress: 20, CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral));

            // Act — force one Tick to trigger InferEmotion
            var ctx = BuildPsychologyContext(actor, personality);
            var outbox = new EventCollector();
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(0), ctx, outbox);

            // Assert — Guilt should be inferred from this VAD region
            var shifted = outbox.Drain().OfType<EmotionShifted>().FirstOrDefault();
            Assert.IsNotNull(shifted, "EmotionShifted must be emitted from Neutral → Guilt transition.");
            Assert.AreEqual(DiscreteEmotion.Guilt, shifted!.To,
                $"VAD (V=-0.50, A=0.50, D=0.35) must infer Guilt. Got: {shifted.To}");
        }

        #endregion

        #region Test 18 — InferEmotion: Guilt VAD does NOT collapse to Shame

        [TestMethod]
        public void InferEmotion_GuiltVAD_DoesNotCollapseIntoShame()
        {
            // Arrange — V=-0.45, A=0.45, D=0.40 (in the Guilt zone, above Shame's D<0.30 threshold)
            var actor = new HumanId(Guid.NewGuid());
            var personality = MakePersonality(agreeableness: 0.5, conscientiousness: 0.5);

            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);

            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

            engine.RestoreState(new PsychologyState(
                Valence: -0.45, Arousal: 0.45, Dominance: 0.40,
                Stress: 20, CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral));

            var ctx = BuildPsychologyContext(actor, personality);
            var outbox = new EventCollector();
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(0), ctx, outbox);

            // Assert — the inferred emotion is NOT Shame
            var shifted = outbox.Drain().OfType<EmotionShifted>().FirstOrDefault();
            if (shifted is not null)
            {
                Assert.AreNotEqual(DiscreteEmotion.Shame, shifted.To,
                    $"VAD with D=0.40 must not infer Shame (D must be < 0.30 for Shame). Got: {shifted.To}");
            }
            // If no EmotionShifted was emitted, the emotion stayed Neutral — that's fine too;
            // we only assert it is NOT Shame.
            Assert.AreNotEqual(DiscreteEmotion.Shame, engine.State.DominantEmotion,
                "DominantEmotion with D=0.40 must not be Shame.");
        }

        #endregion

        #region Test 19 — EmotionDecayGuilt faster than Shame

        [TestMethod]
        public void EmotionDecayGuilt_FasterThanShame()
        {
            var cfg = new PsychologyConfig();

            Assert.IsTrue(cfg.EmotionDecayGuilt > cfg.EmotionDecayShame,
                $"EmotionDecayGuilt ({cfg.EmotionDecayGuilt}) must be > EmotionDecayShame ({cfg.EmotionDecayShame}).");
        }

        #endregion

        #region Helper methods

        private static Personality MakePersonality(
            double agreeableness = 0.5,
            double conscientiousness = 0.5,
            double neuroticism = 0.5,
            double extraversion = 0.5,
            double openness = 0.5)
            => new Personality(
                BigFive: new BigFive(
                    Openness: openness,
                    Conscientiousness: conscientiousness,
                    Extraversion: extraversion,
                    Agreeableness: agreeableness,
                    Neuroticism: neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        private static IHumanContext BuildPsychologyContext(HumanId self, Personality personality)
        {
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id          = self,
                Biology     = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot    = snapshot,
                Random      = new ZeroRandom(),
                Logger      = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus    = new NullEventBus(),
                Scheduler   = new NullScheduler()
            };
        }

        /// <summary>
        /// Builds a <see cref="BehaviorContext"/> with the given <see cref="ValuesProfile"/>
        /// injected into the snapshot — used to test <see cref="ValuesBehaviorModifier"/>.
        /// </summary>
        private static BehaviorContext BuildContextWithValues(
            ValuesProfile values,
            double stress,
            double cogLoad,
            EventCollector? outbox = null)
        {
            var self = new HumanId(Guid.NewGuid());
            var personality = MakePersonality();

            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, stress, cogLoad, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                Values: ValuesState.FromBaseline(values));

            var ctx = new HumanContext
            {
                Id          = self,
                Biology     = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot    = snapshot,
                Random      = new ZeroRandom(),
                Logger      = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus    = new NullEventBus(),
                Scheduler   = new NullScheduler()
            };

            return new BehaviorContext(
                Now:        new WDateTime(0),
                Dt:         WTimeSpan.FromHours(1),
                HumanContext: ctx,
                Outbox:     outbox ?? new EventCollector(),
                State:      new BehaviorState(10, 5, 5, 20, 50, 30, null),
                Config:     new BehaviorConfig(),
                Cooldowns:  new Dictionary<string, double>());
        }

        #endregion Helper methods
    }
}
