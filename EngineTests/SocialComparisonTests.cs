// SocialComparisonTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Engines.Social;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Tests for the social comparison engine (Finding 4): contrast-as-default, the assimilation
    /// gate, benign/malicious envy bifurcation, downward mood repair, and throttled emission.
    /// Calibrated against Gerber, Wheeler &amp; Suls (2018), Gibbons &amp; Buunk (1999), Wills (1981),
    /// and van de Ven et al.
    /// </summary>
    [TestClass]
    public class SocialComparisonTests : TestBase
    {
        private static readonly SocialComparisonConfig Cfg = new();

        #region Math — contrast is the default upward response

        [TestMethod]
        public void Math_Upward_LargeGap_DefaultsToContrast_LowersSelfEval()
        {
            // selfStanding 50 vs target 85 (gap +35 > attainability 25), low closeness → not identified.
            var r = SocialComparisonMath.Evaluate(
                selfStanding: 50, targetStanding: 85, closeness: 15,
                neuroticism: 0.5, agreeableness: 0.5, selfEsteem: 0.5, Cfg);

            Assert.AreEqual(ComparisonDirection.Upward, r.Direction);
            Assert.AreEqual(ComparisonReaction.Contrast, r.Reaction, "Contrast is the default upward response (Gerber 2018).");
            Assert.IsTrue(r.SelfEsteemDelta < 0, $"Upward contrast must lower self-evaluation. Got: {r.SelfEsteemDelta:F4}");
            Assert.IsTrue(r.MoodValenceDelta < 0, "Upward contrast dents mood.");
            Assert.AreEqual(0.0, r.AchievementMotivationDelta, 1e-9, "Pure contrast gives no inspiration.");
        }

        #endregion

        #region Math — assimilation requires attainability + identification

        [TestMethod]
        public void Math_Upward_Attainable_AndIdentified_Assimilates_BenignEnvy()
        {
            // Small gap (+15 ≤ 25 attainable) AND high closeness (70 ≥ 50 identified) → assimilation.
            var r = SocialComparisonMath.Evaluate(
                selfStanding: 50, targetStanding: 65, closeness: 70,
                neuroticism: 0.5, agreeableness: 0.5, selfEsteem: 0.5, Cfg);

            Assert.AreEqual(ComparisonDirection.Upward, r.Direction);
            Assert.AreEqual(ComparisonReaction.Assimilation, r.Reaction);
            Assert.AreEqual(ComparisonEnvy.Benign, r.Envy);
            Assert.IsTrue(r.AchievementMotivationDelta > 0, $"Benign envy must raise achievement motivation. Got: {r.AchievementMotivationDelta:F3}");
            Assert.IsTrue(r.SelfEsteemDelta >= 0, "Assimilation does not lower self-esteem.");
        }

        #endregion

        #region Math — malicious envy under low agreeableness

        [TestMethod]
        public void Math_Upward_Contrast_LowAgreeableness_TriggersMaliciousEnvy()
        {
            var hostile = SocialComparisonMath.Evaluate(
                selfStanding: 40, targetStanding: 90, closeness: 10,
                neuroticism: 0.7, agreeableness: 0.1, selfEsteem: 0.4, Cfg);

            Assert.AreEqual(ComparisonEnvy.Malicious, hostile.Envy, "Unattainable upward + low agreeableness → malicious envy.");
            Assert.IsTrue(hostile.TargetHostilityDelta > 0, $"Malicious envy must emit hostility. Got: {hostile.TargetHostilityDelta:F3}");

            // Same comparison, high agreeableness → no hostility.
            var benignAgreeable = SocialComparisonMath.Evaluate(
                selfStanding: 40, targetStanding: 90, closeness: 10,
                neuroticism: 0.7, agreeableness: 0.95, selfEsteem: 0.4, Cfg);

            Assert.AreNotEqual(ComparisonEnvy.Malicious, benignAgreeable.Envy, "Agreeable comparers do not begrudge.");
            Assert.AreEqual(0.0, benignAgreeable.TargetHostilityDelta, 1e-9);
        }

        #endregion

        #region Math — downward mood repair, stronger for low self-esteem

        [TestMethod]
        public void Math_Downward_RepairsMood_StrongerForLowSelfEsteem()
        {
            // Target below self (gap −30), not identified → contrast / self-enhancement.
            var lowSe = SocialComparisonMath.Evaluate(
                selfStanding: 60, targetStanding: 30, closeness: 30,
                neuroticism: 0.5, agreeableness: 0.5, selfEsteem: 0.25, Cfg);

            var highSe = SocialComparisonMath.Evaluate(
                selfStanding: 60, targetStanding: 30, closeness: 30,
                neuroticism: 0.5, agreeableness: 0.5, selfEsteem: 0.75, Cfg);

            Assert.AreEqual(ComparisonDirection.Downward, lowSe.Direction);
            Assert.IsTrue(lowSe.SelfEsteemDelta > 0 && lowSe.MoodValenceDelta > 0, "Downward comparison repairs mood and self-eval.");
            Assert.IsTrue(lowSe.MoodValenceDelta > highSe.MoodValenceDelta,
                $"Low self-esteem comparers benefit more from downward comparison (Wills 1981). low={lowSe.MoodValenceDelta:F4}, high={highSe.MoodValenceDelta:F4}");
        }

        #endregion

        #region Math — orientation + salience gate

        [TestMethod]
        public void Math_ComparisonOrientation_RisesWithNeuroticism_AndLowSelfEsteem()
        {
            var calm = SocialComparisonMath.ComparisonOrientation(neuroticism: 0.2, selfEsteem: 0.8, Cfg);
            var anxious = SocialComparisonMath.ComparisonOrientation(neuroticism: 0.9, selfEsteem: 0.2, Cfg);
            Assert.IsTrue(anxious > calm, $"Comparison orientation rises with N and low SE (Gibbons & Buunk 1999). calm={calm:F3}, anxious={anxious:F3}");
        }

        [TestMethod]
        public void Math_NegligibleGap_ReturnsNone()
        {
            var r = SocialComparisonMath.Evaluate(
                selfStanding: 50, targetStanding: 52, closeness: 40,
                neuroticism: 0.5, agreeableness: 0.5, selfEsteem: 0.5, Cfg);
            Assert.AreEqual(ComparisonDirection.None, r.Direction, "A standing gap below MinSalientGap is not a comparison.");
        }

        #endregion

        #region Engine — emission + throttle

        [TestMethod]
        public void Tick_EmitsComparison_AgainstMostAdmiredPeer()
        {
            var self = new HumanId(System.Guid.NewGuid());
            var peer = new HumanId(System.Guid.NewGuid());
            var ctx = BuildContext(self, selfEsteem: 0.5, valence: 0.0,
                edges: new() { [peer] = Edge(self, peer, standing: 85, closeness: 20, familiarity: 40) });

            var engine = new DefaultSocialComparisonEngine(Options.Create(Cfg), LoggerFactory.Create(_ => { }));
            var outbox = new EventCollector();
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, outbox);

            var sc = outbox.Drain().OfType<SocialComparisonOccurred>().SingleOrDefault();
            Assert.IsNotNull(sc, "An upward peer must produce a SocialComparisonOccurred.");
            Assert.AreEqual(peer, sc!.Target);
            Assert.AreEqual(ComparisonDirection.Upward, sc.Direction);
            Assert.IsTrue(sc.SelfEsteemDelta < 0, "Upward contrast lowers self-esteem.");
        }

        [TestMethod]
        public void Tick_Throttle_NoSecondComparisonWithinCooldown()
        {
            var self = new HumanId(System.Guid.NewGuid());
            var peer = new HumanId(System.Guid.NewGuid());
            var ctx = BuildContext(self, selfEsteem: 0.5, valence: 0.0,
                edges: new() { [peer] = Edge(self, peer, standing: 85, closeness: 20, familiarity: 40) });

            var engine = new DefaultSocialComparisonEngine(Options.Create(Cfg), LoggerFactory.Create(_ => { }));

            var first = new EventCollector();
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, first);
            Assert.AreEqual(1, first.Drain().OfType<SocialComparisonOccurred>().Count(), "First tick compares.");

            // Second tick one hour later — well inside the 0.5-day cooldown.
            var second = new EventCollector();
            engine.Tick(new WDateTime(0) + WTimeSpan.FromHours(1), WTimeSpan.FromHours(1), ctx, second);
            Assert.AreEqual(0, second.Drain().OfType<SocialComparisonOccurred>().Count(), "Comparison is throttled within the cooldown.");
        }

        #endregion

        #region Helpers

        private static RelationshipEdge Edge(HumanId self, HumanId other, double standing, double closeness, double familiarity)
            => new RelationshipEdge(
                self, other,
                Like: 50, Trust: 50, Familiarity: familiarity,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 20, SexualInterest: 20,
                Closeness: closeness, Respect: standing, Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PerceivedPrestige: standing);

        private IHumanContext BuildContext(
            HumanId id, double selfEsteem, double valence,
            Dictionary<HumanId, RelationshipEdge> edges)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var psych = new PsychologyState(
                Valence: valence, Arousal: 0.5, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 0, DominantEmotion: DiscreteEmotion.Neutral);

            var selfConcept = new SelfConcept(
                0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5,
                SelfEsteem: selfEsteem, SelfDiscrepancy: 0.0);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown, null),
                new RelationshipState(edges),
                new MemoryIndex(new List<EpisodicMemory>()),
                SelfConcept: selfConcept);

            return new HumanContext
            {
                Id = id,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandomSource(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private sealed class ZeroRandomSource : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => false;
        }

        #endregion
    }
}
