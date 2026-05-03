// BigFiveApplicationTests.cs
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
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Testy pro aplikaci Big Five rysů na chování NPC.
    /// B1 Neuroticism → stress growth, B2 Extraversion → social cap,
    /// B3 Conscientiousness → inertia, B4 Agreeableness → acceptance,
    /// B5 Openness → novelty.
    /// </summary>
    [TestClass]
    public sealed class BigFiveApplicationTests : TestBase
    {
        private static readonly SleepConfig NoSleepCfg = new SleepConfig() with { SleepPromptThreshold = 999.0 };

        // ── B1: Neuroticism → stress growth ─────────────────────────────────────

        [TestMethod]
        public void HighNeuroticism_StressAccumulatesFaster_ThanLowNeuroticism()
        {
            // Physio: sleep debt + pain produce stress growth
            var physio = new PhysiologyState(
                Energy: 70, SleepDebtHours: 4, Hunger: 10, Thirst: 10,
                Pain: 30, ImmuneLoad: 0, BodyTempDelta: 0, Cycle: null);

            var engineLowN  = BuildPsychEngine();
            var engineHighN = BuildPsychEngine();

            var ctxLowN  = BuildPsychContext(neuroticism: 0.1, physio: physio);
            var ctxHighN = BuildPsychContext(neuroticism: 0.9, physio: physio);

            var outbox = new EventCollector();
            engineLowN.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxLowN, outbox);
            engineHighN.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxHighN, outbox);

            Assert.IsTrue(engineHighN.State.Stress > engineLowN.State.Stress,
                $"High-N stress ({engineHighN.State.Stress:F2}) should exceed Low-N stress ({engineLowN.State.Stress:F2})");
        }

        // ── B2: Extraversion → social capacity cap ────────────────────────────

        [TestMethod]
        public void LowExtraversion_CapsNeedBelonging_BelowHighExtraversion()
        {
            // Both characters have no social contact (MeanCloseness=50 from empty edges)
            // and same negative valence — maximum possible belonging pressure.
            var physio = new PhysiologyState(70, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(Valence: -1.0, Arousal: 0.5, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 0, DominantEmotion: DiscreteEmotion.Neutral);

            var ctxIntrovert = BuildBehaviorContext(extraversion: 0.1, physio: physio, psych: psych);
            var ctxExtravert = BuildBehaviorContext(extraversion: 0.9, physio: physio, psych: psych);

            var stateIntrovert = BehaviorMath.ComputeNeedState(ctxIntrovert, new Dictionary<string, double>(), ctxIntrovert.Snapshot.Behavior);
            var stateExtravert = BehaviorMath.ComputeNeedState(ctxExtravert, new Dictionary<string, double>(), ctxExtravert.Snapshot.Behavior);

            // E=0.1: raw(35) + (0.1-0.5)*20 = 35 - 8 = 27
            // E=0.9: raw(35) + (0.9-0.5)*20 = 35 + 8 = 43
            Assert.IsTrue(stateIntrovert.NeedBelonging < stateExtravert.NeedBelonging,
                $"Introvert NeedBelonging ({stateIntrovert.NeedBelonging:F1}) should be below " +
                $"Extravert ({stateExtravert.NeedBelonging:F1})");

            Assert.IsTrue(stateIntrovert.NeedBelonging <= 28.0,
                $"Introvert NeedBelonging ({stateIntrovert.NeedBelonging:F1}) should be ~27 (raw 35 - bias 8)");
        }

        // ── B3: Conscientiousness → inertia (via HabitRoutineEngine) ─────────

        [TestMethod]
        public void HighConscientiousness_StrongerInertiaOnWork_ThanLowConscientiousness()
        {
            // Same setup: current plan = Work, Create=55 > Work=50 without inertia.
            // High-C: effectiveInertia = 0.25 * 0.9 = 0.225 → Work=50*1.225=61.25 > Create=55
            // Low-C:  effectiveInertia = 0.25 * 0.1 = 0.025 → Work=50*1.025=51.25 < Create=55 + noveltyPenalty
            //         But Create is same Productive category → no novelty penalty.
            //         Low-C: Work=51.25 < Create=55 → Create wins (if low-C)
            var farFuture = new WDateTime(WTimeSpan.FromDays(2).Ticks);

            var engineHighC = BuildBehaviorEngine();
            var engineLowC  = BuildBehaviorEngine();

            engineHighC.RestoreState(engineHighC.State with
                { CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0) });
            engineLowC.RestoreState(engineLowC.State with
                { CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0) });

            var ctxHighC = BuildBehaviorContext(conscientiousness: 0.9, curiosity: 0.6);
            var ctxLowC  = BuildBehaviorContext(conscientiousness: 0.1, curiosity: 0.6);

            var outboxH = new EventCollector();
            var outboxL = new EventCollector();
            engineHighC.Tick(farFuture, WTimeSpan.FromHours(1), ctxHighC, outboxH);
            engineLowC.Tick(farFuture, WTimeSpan.FromHours(1), ctxLowC, outboxL);

            var highCChoice = outboxH.Drain().OfType<ActionCommitted>().FirstOrDefault()?.ActionName;
            var lowCChoice  = outboxL.Drain().OfType<ActionCommitted>().FirstOrDefault()?.ActionName;

            Assert.AreEqual(Work, highCChoice,
                $"High-C should keep Work via strong inertia. Got: {highCChoice}");
            Assert.AreEqual(Create, lowCChoice,
                $"Low-C should switch to Create (higher base utility). Got: {lowCChoice}");
        }

        // ── B4: Agreeableness → acceptance threshold ─────────────────────────

        [TestMethod]
        public void HighAgreeableness_AcceptsInteractions_MoreThanLowAgreeableness()
        {
            // 1000 interactions — check acceptance rate by counting.
            // High-A should have higher acceptance rate.
            var cfg = new InteractionConfig();
            var engineHighA = new DefaultInteractionEngine(Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            var engineLowA  = new DefaultInteractionEngine(Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

            // Predictable random (fixed seed via seeded random)
            var rng = new SeededRandom(42);
            var ctxHighA = BuildInteractionContext(agreeableness: 0.9, random: rng);
            var ctxLowA  = BuildInteractionContext(agreeableness: 0.1, random: new SeededRandom(42));

            var outboxH = new EventCollector();
            var outboxL = new EventCollector();

            var proposer = new HumanId(Guid.NewGuid());
            for (var i = 0; i < 200; i++)
            {
                var ev = new InteractionProposed(WDateTime.New(100, 1, 1), proposer, ctxHighA.Id, SpeechAct.SmallTalk, "test", null);
                engineHighA.Handle(ev, ctxHighA, outboxH);
                engineLowA.Handle(ev, ctxLowA, outboxL);
            }

            var highAAccepted = outboxH.Drain().OfType<InteractionOutcome>().Count(o => o.Accepted);
            var lowAAccepted  = outboxL.Drain().OfType<InteractionOutcome>().Count(o => o.Accepted);

            Assert.IsTrue(highAAccepted > lowAAccepted,
                $"High-A accepted {highAAccepted}/200, Low-A accepted {lowAAccepted}/200 — High-A should be higher");
        }

        // ── B5: Openness → reduced novelty penalty (via HabitRoutineEngine) ─

        [TestMethod]
        public void HighOpenness_SmallerNoveltyPenalty_ThanLowOpenness()
        {
            // Current plan = Work (Productive). ReachOut (Social) is cross-category.
            // With NoveltyPenalty=0.1:
            //   Low-O  (0.0): penalty = 0.1 * (1 - 0.0*0.6) = 0.10 → ReachOut *= 0.90
            //   High-O (1.0): penalty = 0.1 * (1 - 1.0*0.6) = 0.04 → ReachOut *= 0.96
            // When ReachOut base utility is chosen to sit just above Work after high-O reduction,
            // but just below Work after low-O reduction:
            //   Need raw utility s.t. u*0.90 < Work and u*0.96 > Work
            //   Choose Work=50, u=53: 53*0.90=47.7<50, 53*0.96=50.88>50 ✓

            var farFuture = new WDateTime(WTimeSpan.FromDays(2).Ticks);

            var engineHighO = BuildBehaviorEngine();
            var engineLowO  = BuildBehaviorEngine();

            engineHighO.RestoreState(engineHighO.State with
                { CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0) });
            engineLowO.RestoreState(engineLowO.State with
                { CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0) });

            // affiliation=0.65 → NeedBelonging×affil ≈ 53 for ReachOut
            // competence=0.5   → Work=50
            var ctxHighO = BuildBehaviorContext(openness: 1.0, affiliation: 0.65, competence: 0.5,
                                               conscientiousness: 0.0);  // C=0 → inertia=0
            var ctxLowO  = BuildBehaviorContext(openness: 0.0, affiliation: 0.65, competence: 0.5,
                                               conscientiousness: 0.0);

            var outboxH = new EventCollector();
            var outboxL = new EventCollector();
            engineHighO.Tick(farFuture, WTimeSpan.FromHours(1), ctxHighO, outboxH);
            engineLowO.Tick(farFuture, WTimeSpan.FromHours(1), ctxLowO, outboxL);

            var highOChoice = outboxH.Drain().OfType<ActionCommitted>().FirstOrDefault()?.ActionName;
            var lowOChoice  = outboxL.Drain().OfType<ActionCommitted>().FirstOrDefault()?.ActionName;

            // High-O: weaker penalty → social action beats Work
            // Low-O:  stronger penalty → Work wins (social is penalized below Work)
            Assert.AreNotEqual(highOChoice, lowOChoice,
                $"High-O and Low-O should pick different actions when novelty penalty differs. Got both: {highOChoice}");
        }

        // ── Factory helpers ───────────────────────────────────────────────────

        private static DefaultPsychologyEngine BuildPsychEngine()
            => new DefaultPsychologyEngine(
                Options.Create(new PsychologyConfig(BaselineAffectVariance: 0.0)),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

        private static IHumanContext BuildPsychContext(double neuroticism, PhysiologyState physio)
        {
            var psych = new PsychologyState(Valence: 0.1, Arousal: 0.4, Dominance: 0.5,
                Stress: 20, CognitiveLoad: 20, DominantEmotion: DiscreteEmotion.Neutral);
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildBehaviorContext(
            double extraversion = 0.5, double conscientiousness = 0.5,
            double openness = 0.5, double affiliation = 0.5, double competence = 0.5,
            double curiosity = 0.5,
            PhysiologyState? physio = null, PsychologyState? psych = null,
            IRandomSource? random = null)
        {
            physio ??= new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            psych  ??= new PsychologyState(0.0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral);
            var personality = new Personality(
                BigFive: new BigFive(openness, conscientiousness, extraversion, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(affiliation, 0.5, 0.3, 0.4, competence, 0.5, curiosity, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = random ?? new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildInteractionContext(double agreeableness, IRandomSource random)
        {
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral);
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, agreeableness, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("loc", true, 0.2, 0.2, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private static DefaultBehaviorEngine BuildBehaviorEngine()
            => new DefaultBehaviorEngine(
                Options.Create(new BehaviorConfig()),
                Options.Create(NoSleepCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        // ── Minimal stubs ─────────────────────────────────────────────────────

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent e) { }
            public IDisposable Subscribe<T>(Action<T> h) where T : class, IDomainEvent => new D();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime w, ScheduledAction a, string? t = null) => new(Guid.NewGuid());
            public ScheduledId ScheduleAfter(WDateTime n, WTimeSpan d, ScheduledAction a, string? t = null) => new(Guid.NewGuid());
            public bool Cancel(ScheduledId id) => true;
            public System.Collections.Generic.IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n)
                => System.Linq.Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class D : IDisposable { public void Dispose() { } }

        private sealed class ZeroRandom : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => false;  // never conflicts — best candidate always wins
        }

        /// <summary>Seeded pseudo-random — returns values based on a repeatable sequence.</summary>
        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;
            public SeededRandom(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }
    }
}
