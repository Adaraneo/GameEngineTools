// LifeStageTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.LifeStage;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for R6 psychological life transitions: event-triggered (never age-locked) evaluation
    /// episodes, small-positive empty nest, null-effect base rate, and the R3 IdealSelf hook.
    /// </summary>
    [TestClass]
    public class LifeStageTests : TestBase
    {
        #region Test 1 — no scripted crisis: episode base rate respected

        [TestMethod]
        public void LifeStage_NoScriptedCrisis_BaseRateRespected()
        {
            const int population = 1000;
            var rng = new SeededRandom(424242);
            var episodes = 0;

            for (var i = 0; i < population; i++)
            {
                var (engine, ctx, self) = MakeEngine(rng);
                var outbox = new EventCollector();
                engine.Handle(
                    new LifeStageTransitionOccurred(At(100), self, StadiumType.Adult, StadiumType.MidAged),
                    ctx, outbox);

                if (outbox.Drain().OfType<LifeEvaluationEpisodeStarted>().Any())
                    episodes++;
            }

            var rate = episodes / (double)population;
            // Entering mid-life is the broad transition (~10–25%); never everyone.
            Assert.IsTrue(rate is > 0.08 and < 0.25,
                $"Mid-life evaluation episode rate must sit in the empirical band. Got: {rate:P1}");
        }

        #endregion Test 1 — no scripted crisis: episode base rate respected

        #region Test 2 — null-effect probability is non-zero

        [TestMethod]
        public void LifeStage_NullEffectProbability_NonZero()
        {
            const int population = 500;
            var rng = new SeededRandom(7);
            var nullEffects = 0;

            for (var i = 0; i < population; i++)
            {
                var (engine, ctx, self) = MakeEngine(rng);
                var outbox = new EventCollector();
                engine.Handle(
                    new LifeStageTransitionOccurred(At(100), self, StadiumType.Adult, StadiumType.MidAged),
                    ctx, outbox);

                if (!outbox.Drain().OfType<LifeEvaluationEpisodeStarted>().Any())
                    nullEffects++;
            }

            Assert.IsTrue(nullEffects > 0, "A non-zero fraction of transitions must produce no episode.");
            // In fact the majority are null (most transitions are uneventful).
            Assert.IsTrue(nullEffects > population / 2, "Most transitions should be uneventful (no scripted crisis).");
        }

        #endregion Test 2 — null-effect probability is non-zero

        #region Test 3 — empty nest default is a small positive

        [TestMethod]
        public void EmptyNest_DefaultEffect_SmallPositive()
        {
            // Typical empty-nester (no strong parenting identity) → positive shift.
            var (normal, normalCtx, normalSelf) = MakeEngine(new ZeroRandom(), needCare: 30);
            var beforeNormal = normal.State.Valence;
            normal.Handle(new EmptyNestOccurred(At(100), normalSelf), normalCtx, new EventCollector());
            Assert.IsTrue(normal.State.Valence > beforeNormal,
                $"Default empty-nest effect must be positive. before={beforeNormal:F3}, after={normal.State.Valence:F3}");

            // Strong parenting identity (high NeedCare) → transient negative.
            var (parent, parentCtx, parentSelf) = MakeEngine(new ZeroRandom(), needCare: 90);
            var beforeParent = parent.State.Valence;
            parent.Handle(new EmptyNestOccurred(At(100), parentSelf), parentCtx, new EventCollector());
            Assert.IsTrue(parent.State.Valence < beforeParent,
                $"Strong-parenting empty-nest effect must be negative. before={beforeParent:F3}, after={parent.State.Valence:F3}");
        }

        #endregion Test 3 — empty nest default is a small positive

        #region Test 4 — empty nest: majority positive across population

        [TestMethod]
        public void EmptyNest_Population_MajorityPositive()
        {
            const int population = 500;
            var rng = new Random(99);
            var positive = 0;

            for (var i = 0; i < population; i++)
            {
                // ~18% strong parenting identity.
                var needCare = rng.NextDouble() < LifeStageMath.ParentingIdentityNegativeFraction ? 90.0 : 30.0;
                var (engine, ctx, self) = MakeEngine(new ZeroRandom(), needCare: needCare);
                var before = engine.State.Valence;
                engine.Handle(new EmptyNestOccurred(At(100), self), ctx, new EventCollector());
                if (engine.State.Valence > before) positive++;
            }

            var fraction = positive / (double)population;
            Assert.IsTrue(fraction > 0.70,
                $"Most empty-nesters should experience a positive shift. Got: {fraction:P0}");
        }

        #endregion Test 4 — empty nest: majority positive across population

        #region Test 5 — R3 hook: mid-life transition shifts IdealSelf and seeds FindMeaning

        [TestMethod]
        public void LifeStage_MidlifeTransition_ShiftsIdealAndSeedsFindMeaning()
        {
            var engine = new DefaultSelfConceptEngine(Options.Create(new SelfConceptConfig()));
            engine.SeedFromPersonality(MakePersonality());
            var self = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self, new ZeroRandom(), needCare: 50);

            var idealCBefore = engine.State.IdealConscientiousness;
            var outbox = new EventCollector();
            engine.Handle(new LifeStageTransitionOccurred(At(100), self, StadiumType.Adult, StadiumType.MidAged),
                ctx, outbox);

            Assert.IsTrue(engine.State.IdealConscientiousness > idealCBefore,
                "Mid-life must raise ideal conscientiousness (maturing ideals).");
            var injected = outbox.Drain().OfType<GoalInjected>().FirstOrDefault();
            Assert.IsNotNull(injected, "Mid-life transition must seed a goal.");
            Assert.AreEqual(PersistentGoalKind.FindMeaning, injected!.Kind);
        }

        #endregion Test 5 — R3 hook: mid-life transition shifts IdealSelf and seeds FindMeaning

        #region Test 6 — R3 hook: teen→adult seeds BuildIdentity

        [TestMethod]
        public void LifeStage_TeenToAdult_SeedsBuildIdentity()
        {
            var engine = new DefaultSelfConceptEngine(Options.Create(new SelfConceptConfig()));
            engine.SeedFromPersonality(MakePersonality());
            var self = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self, new ZeroRandom(), needCare: 50);

            var outbox = new EventCollector();
            engine.Handle(new LifeStageTransitionOccurred(At(100), self, StadiumType.Teenager, StadiumType.Adult),
                ctx, outbox);

            var injected = outbox.Drain().OfType<GoalInjected>().FirstOrDefault();
            Assert.IsNotNull(injected, "Teen→Adult must seed a goal.");
            Assert.AreEqual(PersistentGoalKind.BuildIdentity, injected!.Kind);
        }

        #endregion Test 6 — R3 hook: teen→adult seeds BuildIdentity

        #region Test 7 — LifeStageMath base rates and empty-nest signs

        [TestMethod]
        public void LifeStageMath_BaseRates_AndEmptyNestSigns()
        {
            Assert.IsTrue(LifeStageMath.EvaluationEpisodeProbability(StadiumType.Adult, StadiumType.MidAged)
                        > LifeStageMath.EvaluationEpisodeProbability(StadiumType.Child, StadiumType.Teenager),
                "Mid-life (broad) base rate must exceed the generic strict rate.");

            Assert.IsTrue(LifeStageMath.EmptyNestValenceShift(strongParentingIdentity: false) > 0,
                "Default empty-nest valence shift must be positive.");
            Assert.IsTrue(LifeStageMath.EmptyNestValenceShift(strongParentingIdentity: true) < 0,
                "Strong-parenting empty-nest valence shift must be negative.");

            Assert.IsTrue(LifeStageMath.MidlifeMoodDip(StadiumType.Adult, StadiumType.MidAged) > 0,
                "Entering mid-life applies a small mood dip.");
            Assert.AreEqual(0.0, LifeStageMath.MidlifeMoodDip(StadiumType.Teenager, StadiumType.Adult),
                "Non-midlife transitions apply no mood dip.");
        }

        #endregion Test 7 — LifeStageMath base rates and empty-nest signs

        #region Helpers

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private (DefaultPsychologyEngine engine, IHumanContext ctx, HumanId self) MakeEngine(
            IRandomSource random, double needCare = 50)
        {
            var self = new HumanId(Guid.NewGuid());
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);
            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

            engine.RestoreState(new PsychologyState(
                Valence: 0.0, Arousal: 0.4, Dominance: 0.5, Stress: 0, CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral,
                MoodBaseline: 50,
                Motivations: new MotivationState(NeedCare: needCare)));

            return (engine, BuildContext(self, random, needCare), self);
        }

        private static IHumanContext BuildContext(HumanId self, IRandomSource random, double needCare)
        {
            var personality = MakePersonality();
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral,
                MoodBaseline: 50, Motivations: new MotivationState(NeedCare: needCare));
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "T", Familiar = new[] { "T" } },
                    new Surname { Male = "H", Female = "H" },
                    WDateOnly.New(60, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static Personality MakePersonality()
            => new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;

            public SeededRandom(int seed) => _r = new Random(seed);

            public int Next(int min, int max) => _r.Next(min, max);

            public double NextUnit() => _r.NextDouble();

            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion Helpers
    }
}
