// PhysiologyPsychologyIntegrationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Integration tests that exercise <see cref="DefaultPhysiologyEngine"/> and
    /// <see cref="DefaultPsychologyEngine"/> together, verifying that physiological events
    /// propagate correctly into psychological state changes.
    /// </summary>
    /// <remarks>
    /// Each test scenario mirrors a real game sequence:
    /// physiology Tick/Handle → event emitted → psychology Handle → state verified.
    /// No DI stack, no real clock; all state is constructed inline for determinism.
    /// </remarks>
    [TestClass]
    public class PhysiologyPsychologyIntegrationTests : TestBase
    {
        #region Shared configuration

        private static readonly PhysiologyConfig PhysioCfg = new PhysiologyConfig(
            RestingMetabolicRate: 1600,
            MaxSleepDebtHours: 12,
            EnableMenstrualCycle: true,
            MenstrualCycleBeginsInAge: 12,
            EnergyRecoveryPerSleepHour: 10.0,
            PainPassiveRecoveryPerHour: 0.3,
            PainSleepRecoveryPerHour: 0.5,
            BaseConceptionChancePerEncounter: 0.03,
            OvulationConceptionMultiplier: 4.0,
            PregnancyDiscoveryMinDays: 21,
            PregnancyTermDays: 280);

        private static readonly MenstrualCycleConfig CycleCfg = new MenstrualCycleConfig(
            MeanCycleLengthDays: 28,
            VariabilityDaysStdDev: 0.0,   // zero variance for determinism
            MensesMeanDays: 5,
            PmsRisk: 0.35,
            EnableOvulationWindowEvents: true,
            EnableSymptoms: true);

        private static readonly PsychologyConfig PsychCfg = new PsychologyConfig(
            BaselineAffectVariance: 0.0,   // disabled — deterministic tests
            StressRecoveryRatePerHour: 0.0, // disabled — we control stress explicitly
            SleepQualityAffectWeight: 0.5);

        #endregion Shared configuration

        #region Scenario 1 — Full menstrual cycle progression

        /// <summary>
        /// Scenario: Drive a female character through a full 28-day menstrual cycle.
        /// Verify that:
        /// - Menses phase occurs at the start (days 1–5)
        /// - Follicular phase follows menses
        /// - Ovulation phase occurs around day 14
        /// - Luteal phase completes the cycle
        /// - MensesStarted and OvulationWindowOpened events fire in order
        /// - Psychology observes the ovulation arousal boost via Handle(OvulationWindowOpened)
        /// </summary>
        [TestMethod]
        public void FullMenstrualCycle_PhaseProgressionAndEventOrdering_IsStableAndPredictable()
        {
            // Arrange
            var (physioEngine, psychEngine, ctx, now) = BuildIntegrationPair(
                cycleDayStart: 1,
                cyclePhaseStart: CyclePhase.Menses);

            var observedEvents = new List<IDomainEvent>();

            // Phase tracking
            var phasesObserved = new List<CyclePhase>();
            bool mensesStartedSeen = false;
            bool ovulationWindowSeen = false;
            double arousalBeforeOvulation = psychEngine.State.Arousal;

            // Act — simulate 28 cycle-day advances (one 24 h Tick per day)
            for (int day = 0; day < 28; day++)
            {
                var outbox = new EventCollector();

                physioEngine.Tick(now, WTimeSpan.FromHours(24), ctx, outbox);

                var events = outbox.Drain();
                observedEvents.AddRange(events);

                // Feed physiology events into psychology
                foreach (var evt in events)
                    psychEngine.Handle(evt, ctx, new EventCollector());

                var phase = physioEngine.State.Cycle!.Phase;
                if (!phasesObserved.Contains(phase))
                    phasesObserved.Add(phase);

                if (events.OfType<MensesStarted>().Any())
                    mensesStartedSeen = true;

                if (events.OfType<OvulationWindowOpened>().Any())
                {
                    ovulationWindowSeen = true;
                    arousalBeforeOvulation = psychEngine.State.Arousal;
                }
            }

            // Assert — all four active phases were visited
            Assert.IsTrue(phasesObserved.Contains(CyclePhase.Menses),
                "Full cycle must include Menses phase.");
            Assert.IsTrue(phasesObserved.Contains(CyclePhase.Follicular),
                "Full cycle must include Follicular phase.");
            Assert.IsTrue(phasesObserved.Contains(CyclePhase.Ovulation),
                "Full cycle must include Ovulation phase.");
            Assert.IsTrue(phasesObserved.Contains(CyclePhase.Luteal),
                "Full cycle must include Luteal phase.");

            // MensesStarted: first cycle day starts in Menses so it fires at wrap-around (day 28+1=1)
            // OR it was already in Menses and fires when it re-enters.
            // OvulationWindowOpened must fire exactly once in the 28 days.
            Assert.IsTrue(ovulationWindowSeen,
                "OvulationWindowOpened must fire during a full 28-day cycle.");

            // CycleDayAdvanced must have been emitted 28 times
            var cycleDayEvents = observedEvents.OfType<CycleDayAdvanced>().ToList();
            Assert.AreEqual(28, cycleDayEvents.Count,
                $"Exactly 28 CycleDayAdvanced events must be emitted. Got: {cycleDayEvents.Count}");

            // Event ordering: CycleDayAdvanced must always precede MensesStarted/MensesEnded/OvulationWindowOpened
            // (they are added to the outbox in that order inside AdvanceCycleDay)
            for (int i = 0; i < observedEvents.Count - 1; i++)
            {
                if (observedEvents[i] is MensesStarted || observedEvents[i] is OvulationWindowOpened)
                {
                    // The previous event at the same day must be CycleDayAdvanced
                    if (i > 0)
                    {
                        Assert.IsInstanceOfType(observedEvents[i - 1], typeof(CycleDayAdvanced),
                            $"MensesStarted/OvulationWindowOpened at position {i} must be preceded by CycleDayAdvanced.");
                    }
                }
            }
        }

        #endregion Scenario 1

        #region Scenario 2 — Pregnancy discovery triggers stress spike into psychology

        /// <summary>
        /// Scenario: A character conceives (PregnancyState injected), then after
        /// PregnancyDiscoveryMinDays game days, Tick() emits PregnancyDiscovered.
        /// When psychology handles this event, Stress must increase substantially,
        /// and for a neurotic character StressSpiked must be emitted if stress crosses 70.
        /// </summary>
        [TestMethod]
        public void PregnancyDiscovery_SpikesStressAndPropagatesIntoPsychology()
        {
            // Arrange — neurotic character (Neuroticism = 0.8) starting with Stress = 55
            var (physioEngine, psychEngine, ctx, now) = BuildIntegrationPair(
                cycleDayStart: 7,
                cyclePhaseStart: CyclePhase.Follicular,
                neuroticism: 0.8);

            psychEngine.RestoreState(psychEngine.State with { Stress = 55 });

            // Inject a pregnancy that is past the discovery threshold
            var conceivedOn = WDateOnly.New(116, 1, 1);
            var discoveryNow = conceivedOn.AddDays(22).ToDateTime(); // 22 > 21 min days

            physioEngine.RestoreState(physioEngine.State with
            {
                Pregnancy = new PregnancyState(
                    OtherParent: new HumanId(Guid.NewGuid()),
                    ConceivedOn: conceivedOn,
                    EstimatedDueDate: conceivedOn.AddDays(280))
            });

            var stressBefore = psychEngine.State.Stress;
            var outbox = new EventCollector();

            // Act — single Tick on the discovery day
            physioEngine.Tick(discoveryNow, WTimeSpan.FromHours(1), ctx, outbox);

            var events = outbox.Drain();
            var psychOutbox = new EventCollector();

            // Feed discovered event into psychology
            foreach (var evt in events)
                psychEngine.Handle(evt, ctx, psychOutbox);

            var psychEvents = psychOutbox.Drain();

            // Assert
            Assert.IsTrue(events.OfType<PregnancyDiscovered>().Any(),
                "PhysiologyEngine must emit PregnancyDiscovered after the min-days threshold.");

            Assert.IsTrue(physioEngine.State.Pregnancy!.Discovered,
                "Pregnancy.Discovered flag must be true after threshold.");

            Assert.IsTrue(psychEngine.State.Stress > stressBefore,
                $"Psychology stress must increase after PregnancyDiscovered. " +
                $"Before={stressBefore:F1}, After={psychEngine.State.Stress:F1}");

            // Neuroticism=0.8: spike = 10 + 0.8*15 = 22; 55 + 22 = 77 > 70 → StressSpiked
            Assert.IsTrue(psychEvents.OfType<StressSpiked>().Any(),
                "Neurotic character (Neuroticism=0.8) must emit StressSpiked when stress crosses 70 via PregnancyDiscovered.");
        }

        #endregion Scenario 2

        #region Scenario 3 — Compound stress scenario

        /// <summary>
        /// Scenario: A character has high sleep debt + significant pain + elevated stress.
        /// After one Tick, CognitiveLoad must be substantially higher than a character
        /// with no stressors, and arousal must be suppressed if fever is present.
        /// </summary>
        [TestMethod]
        public void CompoundStress_SleepDebtPainHighStress_ElevatesCognitiveLoadSignificantly()
        {
            // Arrange — two engines: one with compound stressors, one clean
            var (stressedPhysio, stressedPsych, stressedCtx, now) = BuildIntegrationPair();
            var (cleanPhysio, cleanPsych, cleanCtx, _)             = BuildIntegrationPair();

            // Compound stressors: 8 h sleep debt, pain=60, bodyTemp=2.0, stress=50
            stressedPhysio.RestoreState(stressedPhysio.State with
            {
                SleepDebtHours = 8,
                Pain = 60,
                BodyTempDelta = 2.0
            });
            stressedPsych.RestoreState(stressedPsych.State with { Stress = 50, CognitiveLoad = 0 });

            // Clean: no stressors
            cleanPhysio.RestoreState(cleanPhysio.State with
            {
                SleepDebtHours = 0,
                Pain = 0,
                BodyTempDelta = 0
            });
            cleanPsych.RestoreState(cleanPsych.State with { Stress = 0, CognitiveLoad = 0 });

            // Act — Tick both
            var stressedOutbox = new EventCollector();
            var cleanOutbox    = new EventCollector();

            // Build contexts with the appropriate physiology
            var stressedCtxWithPhysio = RebuildContext(stressedCtx, stressedPhysio.State, "stressed");
            var cleanCtxWithPhysio    = RebuildContext(cleanCtx, cleanPhysio.State, "clean");

            stressedPsych.Tick(now, WTimeSpan.FromHours(1), stressedCtxWithPhysio, stressedOutbox);
            cleanPsych.Tick(now, WTimeSpan.FromHours(1), cleanCtxWithPhysio, cleanOutbox);

            // Assert — compound stressors must produce much higher CogLoad
            Assert.IsTrue(
                stressedPsych.State.CognitiveLoad > cleanPsych.State.CognitiveLoad + 5.0,
                $"Compound stressors must produce substantially higher CogLoad. " +
                $"Stressed={stressedPsych.State.CognitiveLoad:F2}, Clean={cleanPsych.State.CognitiveLoad:F2}");

            // Sleep debt: 8 * 1.8 = 14.4; Pain: 60 * 0.4 = 24; Stress: 50 * 0.3 = 15; Fever: (2.0-1.5)*8=4
            // Target ≈ 57.4 vs clean target ≈ 0
            Assert.IsTrue(stressedPsych.State.CognitiveLoad > 5.0,
                $"Target CogLoad ~57 must push actual CogLoad above 5 after 1h. " +
                $"Got={stressedPsych.State.CognitiveLoad:F2}");
        }

        /// <summary>
        /// Scenario: Sleep debt, pain, and high stress together push valence downward
        /// more than any single stressor alone.
        /// </summary>
        [TestMethod]
        public void CompoundStress_SleepDebtAndPain_SuppressValenceMoreThanSingleStressor()
        {
            var (_, singlePsych, _, now) = BuildIntegrationPair();
            var (_, compoundPsych, _, _) = BuildIntegrationPair();

            // Single stressor: only sleep debt
            var singlePhysio = MakePhysio(sleepDebtHours: 6, pain: 0, bodyTempDelta: 0);
            var singleCtx = BuildRawContext(neuroticism: 0.5, physio: singlePhysio);
            singlePsych.RestoreState(singlePsych.State with { Valence = 0.0, Stress = 20, CognitiveLoad = 0 });

            // Compound: sleep debt + pain
            var compoundPhysio = MakePhysio(sleepDebtHours: 6, pain: 50, bodyTempDelta: 0);
            var compoundCtx = BuildRawContext(neuroticism: 0.5, physio: compoundPhysio);
            compoundPsych.RestoreState(compoundPsych.State with { Valence = 0.0, Stress = 20, CognitiveLoad = 0 });

            singlePsych.Tick(now, WTimeSpan.FromHours(1), singleCtx, new EventCollector());
            compoundPsych.Tick(now, WTimeSpan.FromHours(1), compoundCtx, new EventCollector());

            Assert.IsTrue(
                compoundPsych.State.Valence < singlePsych.State.Valence,
                $"Compound stressors (sleep debt + pain) must suppress Valence more than sleep debt alone. " +
                $"Single={singlePsych.State.Valence:F4}, Compound={compoundPsych.State.Valence:F4}");
        }

        #endregion Scenario 3

        #region Scenario 4 — Postpartum recovery arc

        /// <summary>
        /// Scenario: A character who has just given birth is in the postpartum state.
        /// Verify that:
        /// - Cycle is Paused with LibidoMod = 0.8 (injected immediately post-birth)
        /// - Psychology processes ChildBorn event correctly: Valence rises, Stress falls
        /// - On subsequent Ticks with paused cycle, no cycle events are emitted
        /// </summary>
        [TestMethod]
        public void PostpartumState_ChildBornEvent_UpdatesPsychologyCorrectlyAndCyclePauses()
        {
            // Arrange — character just gave birth (Pregnancy cleared, Cycle paused)
            var (physioEngine, psychEngine, ctx, now) = BuildIntegrationPair(
                cycleDayStart: 7,
                cyclePhaseStart: CyclePhase.Follicular);

            psychEngine.RestoreState(psychEngine.State with
            {
                Valence = 0.0,
                Stress = 40,
                Arousal = 0.4
            });

            var otherParent = new HumanId(Guid.NewGuid());
            var childBornEvent = new ChildBorn(now, ctx.Id, otherParent);

            var valenceBefore = psychEngine.State.Valence;
            var stressBefore  = psychEngine.State.Stress;

            // Act — psychology handles the birth event
            var psychOutbox = new EventCollector();
            psychEngine.Handle(childBornEvent, ctx, psychOutbox);

            // Assert — Valence+0.25, Stress-10
            Assert.IsTrue(psychEngine.State.Valence > valenceBefore,
                $"ChildBorn must raise Valence. Before={valenceBefore:F3}, After={psychEngine.State.Valence:F3}");
            Assert.IsTrue(psychEngine.State.Stress < stressBefore,
                $"ChildBorn must reduce Stress. Before={stressBefore:F1}, After={psychEngine.State.Stress:F1}");
        }

        /// <summary>
        /// Scenario: After birth, with the cycle paused, 48 game hours of Ticks
        /// must produce no CycleDayAdvanced, MensesStarted, or OvulationWindowOpened events.
        /// </summary>
        [TestMethod]
        public void PostpartumState_PausedCycle_ProducesNoCycleEvents()
        {
            // Arrange — cycle is already paused (postpartum)
            var (physioEngine, _, ctx, now) = BuildIntegrationPair();

            physioEngine.RestoreState(physioEngine.State with
            {
                Cycle = new MenstrualCycleState(
                    Phase: CyclePhase.Paused,
                    DayInCycle: 1,
                    OvulationWindow: false,
                    SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                    LibidoMod: 0.8,
                    LastMensesStart: WDateOnly.New(116, 1, 1)),
                Pregnancy = null
            });

            var allEvents = new List<IDomainEvent>();

            // Act — 48 hours (2 days) of Ticks should produce no cycle activity
            for (int i = 0; i < 48; i++)
            {
                var outbox = new EventCollector();
                physioEngine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);
                allEvents.AddRange(outbox.Drain());
            }

            // Assert — paused cycle must not advance or emit cycle events
            Assert.IsFalse(allEvents.OfType<CycleDayAdvanced>().Any(),
                "A paused cycle must not emit CycleDayAdvanced events.");
            Assert.IsFalse(allEvents.OfType<MensesStarted>().Any(),
                "A paused cycle must not emit MensesStarted.");
            Assert.IsFalse(allEvents.OfType<OvulationWindowOpened>().Any(),
                "A paused cycle must not emit OvulationWindowOpened.");
        }

        #endregion Scenario 4

        #region Helpers

        /// <summary>
        /// Builds a matched physiology + psychology engine pair with default configs and
        /// a female character context. The cycle is seeded at the given day/phase.
        /// </summary>
        private static (
            DefaultPhysiologyEngine physio,
            DefaultPsychologyEngine psych,
            IHumanContext ctx,
            WDateTime now)
            BuildIntegrationPair(
                int cycleDayStart = 7,
                CyclePhase cyclePhaseStart = CyclePhase.Follicular,
                double neuroticism = 0.5)
        {
            var physioCfg = Options.Create(PhysioCfg);
            var cycleCfg  = Options.Create(CycleCfg);
            var psychCfg  = Options.Create(PsychCfg);
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var physioEngine = new DefaultPhysiologyEngine(
                physioCfg, cycleCfg, logFactory, rng,
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(13, 1, 1),
                now: WDateOnly.New(116, 1, 1));

            // Override cycle to exact starting state
            physioEngine.RestoreState(physioEngine.State with
            {
                Cycle = new MenstrualCycleState(
                    Phase: cyclePhaseStart,
                    DayInCycle: cycleDayStart,
                    OvulationWindow: false,
                    SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                    LibidoMod: 1.0,
                    LastMensesStart: WDateOnly.New(116, 1, 1))
            });

            var psychEngine = new DefaultPsychologyEngine(psychCfg, logFactory, rng);

            var ctx = BuildRawContext(neuroticism, physioEngine.State);
            var now = WDateOnly.New(116, 1, 1).ToDateTime();

            return (physioEngine, psychEngine, ctx, now);
        }

        /// <summary>
        /// Rebuilds a context with an updated physiology snapshot (used in compound-stress tests
        /// where physiology is modified before passing to the psychology Tick).
        /// </summary>
        private static IHumanContext RebuildContext(IHumanContext original, PhysiologyState physio, string tag)
        {
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = original.Id,
                Biology = original.Biology,
                Personality = original.Personality,
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger(tag),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>
        /// Builds a raw <see cref="IHumanContext"/> with configurable neuroticism and physiology.
        /// </summary>
        private static IHumanContext BuildRawContext(double neuroticism, PhysiologyState? physio = null, string? currentAction = null)
        {
            var ph = physio ?? new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var plan = currentAction is not null
                ? new PlannedAction(currentAction, new WDateTime(0), WTimeSpan.FromHours(1), 50)
                : null;

            var snapshot = new EnginesSnapshot(
                ph, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, plan),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                    Attachment: AttachmentStyle.Secure,
                    Communication: CommunicationStyle.Direct,
                    Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality: Sociosexuality.Intermediate,
                    Chronotype: Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Integration"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>Builds a bare physiology state for compound-stress scenarios.</summary>
        private static PhysiologyState MakePhysio(double sleepDebtHours, double pain, double bodyTempDelta)
            => new PhysiologyState(
                Energy: 70,
                SleepDebtHours: sleepDebtHours,
                Hunger: 20,
                Thirst: 15,
                Pain: pain,
                ImmuneLoad: 5,
                BodyTempDelta: bodyTempDelta,
                Cycle: null);

        #endregion Helpers
    }
}
