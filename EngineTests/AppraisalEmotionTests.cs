// AppraisalEmotionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Psychology.Appraisal;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for the Scherer-CPM appraisal path (<see cref="AppraisalEvaluator"/>,
    /// <see cref="AppraisalEmotionMap"/>) and its integration with <see cref="DefaultPsychologyEngine"/>.
    /// The defining property is that the emotion <i>generator</i> is no longer PAD-only: identical
    /// physiological PAD with a different appraisal context yields a different discrete emotion.
    /// </summary>
    [TestClass]
    public class AppraisalEmotionTests : TestBase
    {
        #region AppraisalEmotionMap — agency discriminates negative emotions

        [TestMethod]
        public void Map_GoalConducive_SelfAgency_ProducesJoy()
        {
            var outcome = new AppraisalOutcome(
                Relevance: 0.9, Novelty: 0.3, IntrinsicPleasantness: 0.6,
                GoalConduciveness: 1.0, Agency: AppraisalAgency.Self,
                Certainty: 0.95, CopingPotential: 0.8, NormCompatibility: 0.4);

            var result = AppraisalEmotionMap.Map(outcome, new PsychologyConfig());

            Assert.AreEqual(DiscreteEmotion.Joy, result.Emotion);
            Assert.IsTrue(result.DeltaValence > 0, $"Joy must raise valence. Got {result.DeltaValence:F3}");
        }

        [TestMethod]
        public void Map_GoalBlocked_OtherAgency_ProducesAnger()
        {
            var outcome = new AppraisalOutcome(
                Relevance: 0.85, Novelty: 0.2, IntrinsicPleasantness: -0.4,
                GoalConduciveness: -1.0, Agency: AppraisalAgency.Other,
                Certainty: 0.8, CopingPotential: 0.7, NormCompatibility: 0.0);

            var result = AppraisalEmotionMap.Map(outcome, new PsychologyConfig());

            Assert.AreEqual(DiscreteEmotion.Anger, result.Emotion);
        }

        [TestMethod]
        public void Map_Threat_LowCoping_Uncertain_ProducesFear()
        {
            var outcome = new AppraisalOutcome(
                Relevance: 0.85, Novelty: 0.3, IntrinsicPleasantness: -0.3,
                GoalConduciveness: -0.8, Agency: AppraisalAgency.Circumstance,
                Certainty: 0.3, CopingPotential: 0.2, NormCompatibility: 0.0);

            var result = AppraisalEmotionMap.Map(outcome, new PsychologyConfig());

            Assert.AreEqual(DiscreteEmotion.Fear, result.Emotion);
        }

        [TestMethod]
        public void Map_RealisedLoss_Certain_Circumstance_ProducesSadness()
        {
            var outcome = new AppraisalOutcome(
                Relevance: 0.85, Novelty: 0.1, IntrinsicPleasantness: -0.3,
                GoalConduciveness: -0.8, Agency: AppraisalAgency.Circumstance,
                Certainty: 0.95, CopingPotential: 0.2, NormCompatibility: 0.0);

            var result = AppraisalEmotionMap.Map(outcome, new PsychologyConfig());

            Assert.AreEqual(DiscreteEmotion.Sadness, result.Emotion);
        }

        [TestMethod]
        public void Map_SelfNormViolation_ProducesGuilt()
        {
            var outcome = new AppraisalOutcome(
                Relevance: 0.8, Novelty: 0.1, IntrinsicPleasantness: 0.0,
                GoalConduciveness: 0.0, Agency: AppraisalAgency.Self,
                Certainty: 0.8, CopingPotential: 0.6, NormCompatibility: -0.8);

            var result = AppraisalEmotionMap.Map(outcome, new PsychologyConfig());

            Assert.AreEqual(DiscreteEmotion.Guilt, result.Emotion);
        }

        [TestMethod]
        public void Map_Irrelevant_ProducesNeutral()
        {
            var result = AppraisalEmotionMap.Map(AppraisalOutcome.Irrelevant, new PsychologyConfig());
            Assert.AreEqual(DiscreteEmotion.Neutral, result.Emotion);
        }

        #endregion AppraisalEmotionMap — agency discriminates negative emotions

        #region Engine integration — same PAD, different appraisal → different emotion

        [TestMethod]
        public void Engine_SamePAD_GoalCompletedVsBlocked_ProducesDifferentEmotion()
        {
            var actor = new HumanId(Guid.NewGuid());
            var revengeGoal = new PersistentGoal(
                Id: Guid.NewGuid(),
                Kind: PersistentGoalKind.SeekRevenge,
                Origin: GoalOrigin.Event,
                Salience: 0.8, Progress: 0.3, Frustration: 0.6,
                CreatedAt: new WDateTime(0), LastProgressAt: new WDateTime(0),
                TargetHuman: new HumanId(Guid.NewGuid()));

            // Engine A — goal blocked (abandoned), relational target → other-accountability → Anger.
            var engineBlocked = MakeEngine();
            engineBlocked.RestoreState(NeutralPad());
            var ctxBlocked = BuildContext(actor, new GoalState(new[] { revengeGoal }));
            var outboxBlocked = new EventCollector();
            engineBlocked.Handle(
                new GoalResolved(new WDateTime(100), actor, revengeGoal.Id, revengeGoal.Kind, GoalResolution.Abandoned),
                ctxBlocked, outboxBlocked);

            // Engine B — same starting PAD, goal completed → goal-conducive → positive emotion.
            var masterGoal = new PersistentGoal(
                Id: Guid.NewGuid(),
                Kind: PersistentGoalKind.MasterCraft,
                Origin: GoalOrigin.Personality,
                Salience: 0.8, Progress: 1.0, Frustration: 0.0,
                CreatedAt: new WDateTime(0), LastProgressAt: new WDateTime(0));
            var engineDone = MakeEngine();
            engineDone.RestoreState(NeutralPad());
            var ctxDone = BuildContext(actor, new GoalState(new[] { masterGoal }));
            var outboxDone = new EventCollector();
            engineDone.Handle(
                new GoalResolved(new WDateTime(100), actor, masterGoal.Id, masterGoal.Kind, GoalResolution.Completed),
                ctxDone, outboxDone);

            // Both started from identical physiological PAD; appraisal context differs.
            Assert.AreEqual(DiscreteEmotion.Anger, engineBlocked.State.DominantEmotion,
                $"Goal blocked by another → Anger. Got {engineBlocked.State.DominantEmotion}");
            Assert.AreEqual(DiscreteEmotion.Joy, engineDone.State.DominantEmotion,
                $"Goal completed → Joy. Got {engineDone.State.DominantEmotion}");
            Assert.AreNotEqual(engineBlocked.State.DominantEmotion, engineDone.State.DominantEmotion,
                "Same PAD with different appraisal must produce different emotions (generator is not PAD-only).");

            Assert.IsTrue(outboxBlocked.Drain().OfType<EmotionAppraised>().Any(),
                "Blocked-goal appraisal must emit EmotionAppraised.");
            Assert.IsTrue(outboxDone.Drain().OfType<EmotionAppraised>().Any(),
                "Completed-goal appraisal must emit EmotionAppraised.");
        }

        #endregion Engine integration — same PAD, different appraisal → different emotion

        #region Helpers

        private static DefaultPsychologyEngine MakeEngine()
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);
            return new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
        }

        private static PsychologyState NeutralPad() => new PsychologyState(
            Valence: 0.0, Arousal: 0.4, Dominance: 0.5,
            Stress: 10, CognitiveLoad: 10, DominantEmotion: DiscreteEmotion.Neutral);

        private static Personality MakePersonality()
            => new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        private static IHumanContext BuildContext(HumanId self, GoalState goals)
        {
            var personality = MakePersonality();
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()),
                Goals: goals);
            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion Helpers
    }
}
