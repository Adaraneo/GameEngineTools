// LossAversionTests.cs
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
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Tests for <see cref="LossAversionModifier"/> — prospect-theory loss weighting relative to a
    /// status-quo reference point, domain-contingent λ, Neuroticism scaling, and independence from the
    /// status-quo inertia component.
    /// </summary>
    [TestClass]
    public class LossAversionTests : TestBase
    {
        [TestMethod]
        public void Loss_IsPenalizedApproximatelyLambdaTimes_AnEqualMagnitudeGain()
        {
            const double reference = 50.0;
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, reference, WTimeSpan.FromHours(2), BehaviorDomain.Competence), // status quo
                new(Create, 60.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),    // +10 gain
                new(Eat, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)     // -10 loss
            };

            new LossAversionModifier().Modify(BuildContext(currentPlan: Work, neuroticism: 0.5), candidates);

            var gain = candidates.First(c => c.Name == Create).Utility;
            var loss = candidates.First(c => c.Name == Eat).Utility;

            Assert.AreEqual(60.0, gain, 0.001, "Gains relative to the reference are not reweighted.");
            // Loss magnitude ≈ λ × gain magnitude (λ = 1.96 for non-risky domains, N = 0.5).
            var gainMagnitude = 60.0 - reference;   // 10
            var lossMagnitude = reference - loss;   // expected ≈ 19.6
            Assert.AreEqual(1.96 * gainMagnitude, lossMagnitude, 0.05,
                $"A loss should be weighted ~λ× an equal-magnitude gain. Loss utility={loss:F2}");
        }

        [TestMethod]
        public void HighNeuroticism_ShowsStrongerLossAversion()
        {
            List<BehaviorCandidate> Make() => new()
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),
                new(Eat, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
            };

            var highN = Make();
            new LossAversionModifier().Modify(BuildContext(Work, neuroticism: 0.9), highN);
            var lowN = Make();
            new LossAversionModifier().Modify(BuildContext(Work, neuroticism: 0.1), lowN);

            var highNloss = highN.First(c => c.Name == Eat).Utility;
            var lowNloss = lowN.First(c => c.Name == Eat).Utility;

            Assert.IsTrue(highNloss < lowNloss,
                $"High-Neuroticism characters weight losses more heavily. HighN={highNloss:F2}, LowN={lowNloss:F2}");
        }

        [TestMethod]
        public void RiskyChoiceDomain_UsesLowerLambda_ThanGeneralDomain()
        {
            // A loss in a Social (risky-choice) candidate is penalized less than the same loss in a
            // general (Competence) candidate, reflecting λ_risky (1.31) < λ_general (1.96).
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),       // reference
                new(ReachOut, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Social),       // -10 risky loss
                new(Eat, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)      // -10 general loss
            };

            new LossAversionModifier().Modify(BuildContext(Work, neuroticism: 0.5), candidates);

            var risky = candidates.First(c => c.Name == ReachOut).Utility;
            var general = candidates.First(c => c.Name == Eat).Utility;

            Assert.IsTrue(risky > general,
                $"Risky-choice losses use the lower λ → less penalty. Risky={risky:F2}, General={general:F2}");
        }

        [TestMethod]
        public void LossWeighting_IsTogglable_IndependentlyOfInertia()
        {
            // With λ = 1.0 the loss-weighting component is off; a loss is not amplified, while the
            // separate InertiaWeight is unaffected (still its default).
            var cfg = new BehaviorConfig(LossAversionLambda: 1.0);
            Assert.AreEqual(0.25, cfg.InertiaWeight, 0.0001, "InertiaWeight is an independent parameter.");

            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 50.0, WTimeSpan.FromHours(2), BehaviorDomain.Competence),
                new(Eat, 40.0, WTimeSpan.FromHours(1), BehaviorDomain.Physiological)
            };

            new LossAversionModifier().Modify(BuildContext(Work, neuroticism: 0.5, config: cfg), candidates);

            Assert.AreEqual(40.0, candidates.First(c => c.Name == Eat).Utility, 0.001,
                "With λ = 1.0 the loss is not amplified — loss weighting is off, independent of inertia.");
        }

        #region Helpers

        private static BehaviorContext BuildContext(string currentPlan, double neuroticism, BehaviorConfig? config = null)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

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
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("LossAversion"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            var state = new BehaviorState(10, 5, 5, 20, 50, 30,
                new PlannedAction(currentPlan, new WDateTime(0), WTimeSpan.FromHours(1), 50.0));

            return new BehaviorContext(
                new WDateTime(0),
                WTimeSpan.FromHours(1),
                human,
                new EventCollector(),
                state,
                config ?? new BehaviorConfig(),
                new Dictionary<string, double>());
        }

        #endregion
    }
}
