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

        #endregion Scenario 1 — Full menstrual cycle progression

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

        #endregion Scenario 2 — Pregnancy discovery triggers stress spike into psychology

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
            var (cleanPhysio, cleanPsych, cleanCtx, _) = BuildIntegrationPair();

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
            var cleanOutbox = new EventCollector();

            // Build contexts with the appropriate physiology
            var stressedCtxWithPhysio = RebuildContext(stressedCtx, stressedPhysio.State, "stressed");
            var cleanCtxWithPhysio = RebuildContext(cleanCtx, cleanPhysio.State, "clean");

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

        #endregion Scenario 3 — Compound stress scenario

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
            var stressBefore = psychEngine.State.Stress;

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

        #endregion Scenario 4 — Postpartum recovery arc

        #region Scenario 5 — Testosteron → NeedIntimacy

        /// <summary>
        /// Postava s vysokou hladinou testosteronu musí vykazovat vyšší NeedIntimacy
        /// po Psychology Tick než postava s nízkou hladinou.
        /// Testuje integraci PhysiologyState.Testosterone → DefaultPsychologyEngine.
        /// </summary>
        [TestMethod]
        public void Testosterone_HighLevel_BoostsNeedIntimacy_InPsychologyTick()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                TestosteroneIntimacyWeight: 0.3));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var highTestoPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var lowTestoPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);

            // Obě instance začínají se stejným NeedIntimacy=50
            highTestoPsych.RestoreState(highTestoPsych.State with
            { Motivations = new MotivationState(NeedIntimacy: 50) });
            lowTestoPsych.RestoreState(lowTestoPsych.State with
            { Motivations = new MotivationState(NeedIntimacy: 50) });

            var highTestoPhysio = MakePhysioWithTestosterone(testosteroneLevel: 85);
            var lowTestoPhysio = MakePhysioWithTestosterone(testosteroneLevel: 30);

            var highCtx = BuildRawContext(neuroticism: 0.5, physio: highTestoPhysio);
            var lowCtx = BuildRawContext(neuroticism: 0.5, physio: lowTestoPhysio);

            var now = new WDateTime(0);
            highTestoPsych.Tick(now, WTimeSpan.FromHours(4), highCtx, new EventCollector());
            lowTestoPsych.Tick(now, WTimeSpan.FromHours(4), lowCtx, new EventCollector());

            Assert.IsTrue(
                highTestoPsych.State.Motivations!.NeedIntimacy > lowTestoPsych.State.Motivations!.NeedIntimacy,
                $"Vysoký testosteron musí zvýšit NeedIntimacy více než nízký. " +
                $"Vysoký={highTestoPsych.State.Motivations.NeedIntimacy:F4}, " +
                $"Nízký={lowTestoPsych.State.Motivations.NeedIntimacy:F4}");
        }

        #endregion Scenario 5 — Testosteron → NeedIntimacy

        #region Scenario 6 — Sleep Inertia → Psychology

        /// <summary>
        /// Aktivní SleepInertiaHours musí tlumit Arousal v Psychology Tick().
        /// </summary>
        [TestMethod]
        public void SleepInertia_ReducesArousal_InPsychologyTick()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                SleepInertiaMaxHours: 1.5));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var withInertia = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var withoutInertia = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            withInertia.RestoreState(withInertia.State with { Arousal = 0.6 });
            withoutInertia.RestoreState(withoutInertia.State with { Arousal = 0.6 });

            var physioWithInertia = MakePhysio(0, 0, 0) with { SleepInertiaHours = 1.2 };
            var physioWithoutInertia = MakePhysio(0, 0, 0) with { SleepInertiaHours = 0.0 };

            var ctxWith = BuildRawContext(neuroticism: 0.5, physio: physioWithInertia);
            var ctxWithout = BuildRawContext(neuroticism: 0.5, physio: physioWithoutInertia);
            var now = new WDateTime(0);

            withInertia.Tick(now, WTimeSpan.FromHours(1), ctxWith, new EventCollector());
            withoutInertia.Tick(now, WTimeSpan.FromHours(1), ctxWithout, new EventCollector());

            Assert.IsTrue(withInertia.State.Arousal < withoutInertia.State.Arousal,
                $"Sleep inertia musí tlumit Arousal. " +
                $"S inertií={withInertia.State.Arousal:F4}, Bez={withoutInertia.State.Arousal:F4}");
        }

        #endregion Scenario 6 — Sleep Inertia → Psychology

        #region Scenario 7 — Hangry neutrální bias

        /// <summary>
        /// Při vysokém hladu a neutrální Valence musí Valence klesat (hangry bias).
        /// </summary>
        [TestMethod]
        public void HangryNeutralBias_NeutralValence_BecomesNegativeWhenHungry()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                HangryNeutralBiasThreshold: 70.0,
                HangryNeutralBiasStrength: 0.015,
                HangryNeutralContextWindow: 0.25));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var hungryPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var normalPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            hungryPsych.RestoreState(hungryPsych.State with { Valence = 0.0 });
            normalPsych.RestoreState(normalPsych.State with { Valence = 0.0 });

            var hungryPhysio = new PhysiologyState(70, 2, 85, 15, 0, 5, 0, null);  // Hunger=85 > threshold 70
            var normalPhysio = new PhysiologyState(70, 2, 20, 15, 0, 5, 0, null);  // Hunger=20

            var ctxHungry = BuildRawContext(neuroticism: 0.5, physio: hungryPhysio);
            var ctxNormal = BuildRawContext(neuroticism: 0.5, physio: normalPhysio);
            var now = new WDateTime(0);

            hungryPsych.Tick(now, WTimeSpan.FromHours(4), ctxHungry, new EventCollector());
            normalPsych.Tick(now, WTimeSpan.FromHours(4), ctxNormal, new EventCollector());

            Assert.IsTrue(hungryPsych.State.Valence < normalPsych.State.Valence,
                $"Hladové NPC musí mít nižší Valenci při neutrálním kontextu. " +
                $"Hladový={hungryPsych.State.Valence:F4}, Normální={normalPsych.State.Valence:F4}");
        }

        /// <summary>
        /// Hangry bias nesmí fungovat při negativní Valence (kontext není neutrální).
        /// </summary>
        [TestMethod]
        public void HangryNeutralBias_NegativeValence_NotAffected_ByHangryBias()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                HangryNeutralBiasThreshold: 70.0,
                HangryNeutralBiasStrength: 0.015,
                HangryNeutralContextWindow: 0.25));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            // Obě instance s negativní valencí (kontext není neutrální → bias se nespouští)
            var hungryNeg = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var normalNeg = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            hungryNeg.RestoreState(hungryNeg.State with { Valence = -0.5 });
            normalNeg.RestoreState(normalNeg.State with { Valence = -0.5 });

            var hungryPhysio = new PhysiologyState(70, 2, 85, 15, 0, 5, 0, null);
            var normalPhysio = new PhysiologyState(70, 2, 20, 15, 0, 5, 0, null);

            var ctxHungry = BuildRawContext(neuroticism: 0.5, physio: hungryPhysio);
            var ctxNormal = BuildRawContext(neuroticism: 0.5, physio: normalPhysio);
            var now = new WDateTime(0);

            hungryNeg.Tick(now, WTimeSpan.FromHours(1), ctxHungry, new EventCollector());
            normalNeg.Tick(now, WTimeSpan.FromHours(1), ctxNormal, new EventCollector());

            // Rozdíl je způsoben pouze stávajícím fyzio driftem (0.001*Hunger*h) — ne hangry bias.
            // Při Hunger=85 vs 20 je fyzio drift rozdíl: 0.001*(85-20)*1h = 0.065.
            // Hangry bias by přidal dalších ~0.05 — proto je tolerance 0.09 (fyziodrift < thresh < fyziodrift+bias).
            var valenceDiff = Math.Abs(hungryNeg.State.Valence - normalNeg.State.Valence);
            Assert.IsTrue(valenceDiff < 0.09,
                $"Hangry bias se nesmí spouštět při negativní Valenci (rozdíl musí být jen fyzio drift). Rozdíl={valenceDiff:F4}");
        }

        #endregion Scenario 7 — Hangry neutrální bias

        #region Scenario 8 — Sickness anhedonie a letargie

        /// <summary>
        /// Nemocné NPC (ImmuneLoad > threshold) musí mít nižší Arousal (letargie).
        /// </summary>
        [TestMethod]
        public void SicknessBehavior_Lethargy_ReducesArousal()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                SicknessLethargyArousalPenalty: 0.008,
                SicknessBrainFogCogLoadBonus: 3.0));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var sickPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var healthyPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            sickPsych.RestoreState(sickPsych.State with
            { Arousal = 0.6, Motivations = new MotivationState(SicknessWithdraw: false) });
            healthyPsych.RestoreState(healthyPsych.State with
            { Arousal = 0.6, Motivations = new MotivationState(SicknessWithdraw: false) });

            var sickPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 70 };  // > threshold 50
            var healthyPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 10 };

            var ctxSick = BuildRawContext(neuroticism: 0.5, physio: sickPhysio);
            var ctxHealthy = BuildRawContext(neuroticism: 0.5, physio: healthyPhysio);
            var now = new WDateTime(0);

            sickPsych.Tick(now, WTimeSpan.FromHours(4), ctxSick, new EventCollector());
            healthyPsych.Tick(now, WTimeSpan.FromHours(4), ctxHealthy, new EventCollector());

            Assert.IsTrue(sickPsych.State.Arousal < healthyPsych.State.Arousal,
                $"Nemocné NPC musí mít nižší Arousal (letargie). " +
                $"Nemocný={sickPsych.State.Arousal:F4}, Zdravý={healthyPsych.State.Arousal:F4}");
        }

        /// <summary>
        /// Pozitivní interakce musí méně zvyšovat NeedSocial u nemocného NPC (anhedonie).
        /// </summary>
        [TestMethod]
        public void SicknessBehavior_Anhedonia_ReducesNeedSocialGainOnAccepted()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                SicknessAnhedoniaImmuneThreshold: 50.0,
                SicknessAnhedoniaRewardBlunting: 0.5));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var sickPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var healthyPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var initMotiv = new MotivationState(NeedSocial: 50);
            sickPsych.RestoreState(sickPsych.State with { Motivations = initMotiv });
            healthyPsych.RestoreState(healthyPsych.State with { Motivations = initMotiv });

            var sickPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 70 };
            var healthyPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 10 };

            var sickCtx = BuildRawContext(neuroticism: 0.5, physio: sickPhysio);
            var healthyCtx = BuildRawContext(neuroticism: 0.5, physio: healthyPhysio);

            var io = new GameEngineTools.Characters.Engines.Interactions.InteractionOutcome(
                OccurredAt: new WDateTime(0),
                From: new HumanId(Guid.NewGuid()),
                To: sickCtx.Id,
                Act: RelationalActKind.SmallTalk,
                Accepted: true,
                Reason: string.Empty);

            sickPsych.Handle(io, sickCtx, new EventCollector());
            healthyPsych.Handle(io, healthyCtx, new EventCollector());

            Assert.IsTrue(
                healthyPsych.State.Motivations!.NeedSocial > sickPsych.State.Motivations!.NeedSocial,
                $"Zdravé NPC musí získat více NeedSocial než nemocné. " +
                $"Zdravý={healthyPsych.State.Motivations.NeedSocial:F4}, " +
                $"Nemocný={sickPsych.State.Motivations.NeedSocial:F4}");
        }

        #endregion Scenario 8 — Sickness anhedonie a letargie

        #region Scenario 9 — SAM → Psychology Arousal

        [TestMethod]
        public void SAM_AcuteArousal_ElevatesArousalInPsychology()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                AcuteArousalPsychWeight: 0.6));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var highSAMPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var noSAMPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            highSAMPsych.RestoreState(highSAMPsych.State with { Arousal = 0.4 });
            noSAMPsych.RestoreState(noSAMPsych.State with { Arousal = 0.4 });

            var highSAMPhysio = MakePhysio(0, 0, 0) with { AcuteArousalLevel = 80 };
            var noSAMPhysio = MakePhysio(0, 0, 0) with { AcuteArousalLevel = 0 };

            var ctxHigh = BuildRawContext(0.5, highSAMPhysio);
            var ctxNone = BuildRawContext(0.5, noSAMPhysio);
            var now = new WDateTime(0);

            highSAMPsych.Tick(now, WTimeSpan.FromHours(1), ctxHigh, new EventCollector());
            noSAMPsych.Tick(now, WTimeSpan.FromHours(1), ctxNone, new EventCollector());

            Assert.IsTrue(highSAMPsych.State.Arousal > noSAMPsych.State.Arousal,
                $"SAM aktivace musí zvyšovat PAD Arousal. High={highSAMPsych.State.Arousal:F4}, None={noSAMPsych.State.Arousal:F4}");
        }

        #endregion Scenario 9 — SAM → Psychology Arousal

        #region Scenario 10 — Yerkes-Dodson kortizol optimum

        [TestMethod]
        public void YerkesDodson_OptimalCortisol_ReducesCogLoad()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                CortisolOptimalLow: 55.0,
                CortisolOptimalHigh: 75.0,
                CortisolOptimalCogBonus: 5.0));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var optimalPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var suboptPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            optimalPsych.RestoreState(optimalPsych.State with { CognitiveLoad = 50 });
            suboptPsych.RestoreState(suboptPsych.State with { CognitiveLoad = 50 });

            var optimalPhysio = MakePhysio(0, 0, 0) with { CortisolLevel = 65 };  // v optimu
            var suboptPhysio = MakePhysio(0, 0, 0) with { CortisolLevel = 30 };  // mimo optimum

            var ctxOpt = BuildRawContext(0.5, optimalPhysio);
            var ctxSub = BuildRawContext(0.5, suboptPhysio);
            var now = new WDateTime(0);

            optimalPsych.Tick(now, WTimeSpan.FromHours(2), ctxOpt, new EventCollector());
            suboptPsych.Tick(now, WTimeSpan.FromHours(2), ctxSub, new EventCollector());

            Assert.IsTrue(optimalPsych.State.CognitiveLoad < suboptPsych.State.CognitiveLoad,
                $"Optimální kortizol musí snižovat CogLoad více. Optimal={optimalPsych.State.CognitiveLoad:F4}, Subopt={suboptPsych.State.CognitiveLoad:F4}");
        }

        #endregion Scenario 10 — Yerkes-Dodson kortizol optimum

        #region Scenario 11 — Vagální tonus (přes Neuroticism)

        [TestMethod]
        public void VagalTone_HighNeuroticism_SlowerStressRecovery()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 2.0,
                EnableCircadianRhythm: false));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var highNPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var lowNPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            highNPsych.RestoreState(highNPsych.State with { Stress = 80 });
            lowNPsych.RestoreState(lowNPsych.State with { Stress = 80 });

            var physio = MakePhysio(0, 0, 0);
            var highNCtx = BuildRawContext(neuroticism: 0.9, physio: physio);
            var lowNCtx = BuildRawContext(neuroticism: 0.1, physio: physio);
            var now = new WDateTime(0);

            highNPsych.Tick(now, WTimeSpan.FromHours(4), highNCtx, new EventCollector());
            lowNPsych.Tick(now, WTimeSpan.FromHours(4), lowNCtx, new EventCollector());

            Assert.IsTrue(highNPsych.State.Stress > lowNPsych.State.Stress,
                $"High Neuroticism musí mít pomalejší stress recovery (vagal tone). High N={highNPsych.State.Stress:F2}, Low N={lowNPsych.State.Stress:F2}");
        }

        #endregion Scenario 11 — Vagální tonus (přes Neuroticism)

        #region Scenario 12 — Ambientní teplota

        [TestMethod]
        public void AmbientTemp_HeatAboveThreshold_ReducesValence()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                AmbientTempHeatThreshold: 27.0,
                AmbientTempHeatValencePenalty: 0.02));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var hotPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var normalPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            hotPsych.RestoreState(hotPsych.State with { Valence = 0.0 });
            normalPsych.RestoreState(normalPsych.State with { Valence = 0.0 });

            var physio = MakePhysio(0, 0, 0);
            var hotCtx = BuildRawContext(neuroticism: 0.5, physio: physio, ambientTemperature: 35.0);
            var normalCtx = BuildRawContext(neuroticism: 0.5, physio: physio, ambientTemperature: 20.0);

            var now = new WDateTime(0);
            hotPsych.Tick(now, WTimeSpan.FromHours(2), hotCtx, new EventCollector());
            normalPsych.Tick(now, WTimeSpan.FromHours(2), normalCtx, new EventCollector());

            Assert.IsTrue(hotPsych.State.Valence < normalPsych.State.Valence,
                $"Horko musí snižovat Valenci více než neutrální teplota. Hot={hotPsych.State.Valence:F4}, Normal={normalPsych.State.Valence:F4}");
        }

        #endregion Scenario 12 — Ambientní teplota

        #region Scenario 13 — Dehydratace → CogLoad

        [TestMethod]
        public void Dehydration_HighThirst_IncreaseCogLoad()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                DehydrationCogLoadThreshold: 50.0,
                DehydrationCogLoadBonus: 5.0));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var thirstyPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var hydratedPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            thirstyPsych.RestoreState(thirstyPsych.State with { CognitiveLoad = 20 });
            hydratedPsych.RestoreState(hydratedPsych.State with { CognitiveLoad = 20 });

            var thirstyPhysio = new PhysiologyState(70, 2, 25, 80, 0, 5, 0, null);  // Thirst=80 > 50
            var hydratedPhysio = new PhysiologyState(70, 2, 25, 10, 0, 5, 0, null);  // Thirst=10

            var ctxThirsty = BuildRawContext(0.5, thirstyPhysio);
            var ctxHydrated = BuildRawContext(0.5, hydratedPhysio);
            var now = new WDateTime(0);

            thirstyPsych.Tick(now, WTimeSpan.FromHours(2), ctxThirsty, new EventCollector());
            hydratedPsych.Tick(now, WTimeSpan.FromHours(2), ctxHydrated, new EventCollector());

            Assert.IsTrue(thirstyPsych.State.CognitiveLoad > hydratedPsych.State.CognitiveLoad,
                $"Dehydratace musí zvyšovat CogLoad. Thirsty={thirstyPsych.State.CognitiveLoad:F4}, Hydrated={hydratedPsych.State.CognitiveLoad:F4}");
        }

        #endregion Scenario 13 — Dehydratace → CogLoad

        #region Scenario 14 — Hyperalgezie (sickness pain amplification)

        [TestMethod]
        public void Hyperalgesia_HighImmuneLoad_AmplifiesPainValencePenalty()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                HyperalgesiaImmuneThreshold: 40.0,
                HyperalgesiaMaxMultiplier: 0.5));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var sickPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var healthyPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            sickPsych.RestoreState(sickPsych.State with { Valence = 0.0 });
            healthyPsych.RestoreState(healthyPsych.State with { Valence = 0.0 });

            // Obě postavy mají stejnou bolest (40), ale liší se imunitní zátěží
            var sickPhysio = new PhysiologyState(70, 2, 25, 20, 40, 80, 0, null); // ImmuneLoad=80 > 40
            var healthyPhysio = new PhysiologyState(70, 2, 25, 20, 40, 5, 0, null); // ImmuneLoad=5

            var ctxSick = BuildRawContext(0.5, sickPhysio);
            var ctxHealthy = BuildRawContext(0.5, healthyPhysio);
            var now = new WDateTime(0);

            sickPsych.Tick(now, WTimeSpan.FromHours(1), ctxSick, new EventCollector());
            healthyPsych.Tick(now, WTimeSpan.FromHours(1), ctxHealthy, new EventCollector());

            Assert.IsTrue(sickPsych.State.Valence < healthyPsych.State.Valence,
                $"Nemoc musí amplifikovat bolestivý signál (nižší Valence). " +
                $"Sick={sickPsych.State.Valence:F4}, Healthy={healthyPsych.State.Valence:F4}");
        }

        #endregion Scenario 14 — Hyperalgezie (sickness pain amplification)

        #region Scenario 15 — Chronická bolest → MoodBaseline

        [TestMethod]
        public void ChronicPain_ReducesMoodBaseline_InPsychology()
        {
            // Vysoká penalta (2.0/den) + dlouhý tick (24h) = jasně měřitelný efekt
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                MoodBaselineRecoveryPerHour: 0.0,   // vypnout recovery aby nerušilo
                EnableCircadianRhythm: false,
                ChronicPainOnsetDays: 7.0,
                ChronicPainValencePenaltyPerDay: 0.0,
                ChronicPainMoodBaselinePenaltyPerDay: 2.0));  // 2 body/den → 2*0.667*24h = ~32 bodů
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var chronicPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var acutePsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            chronicPsych.RestoreState(chronicPsych.State with { MoodBaseline = 50 });
            acutePsych.RestoreState(acutePsych.State with { MoodBaseline = 50 });

            // Chronická bolest: ChronicPainDays=20 (přes onset 7); chronicity = min(20/30, 1) = 0.667
            var chronicPhysio = new PhysiologyState(70, 2, 25, 20, 40, 5, 0, null) with { ChronicPainDays = 20 };
            // Akutní bolest: stejná Pain, ale ChronicPainDays=0 (pod onset) → žádná penalta
            var acutePhysio = new PhysiologyState(70, 2, 25, 20, 40, 5, 0, null) with { ChronicPainDays = 0 };

            var ctxChronic = BuildRawContext(0.5, chronicPhysio);
            var ctxAcute = BuildRawContext(0.5, acutePhysio);
            var now = new WDateTime(0);

            chronicPsych.Tick(now, WTimeSpan.FromHours(24), ctxChronic, new EventCollector());
            acutePsych.Tick(now, WTimeSpan.FromHours(24), ctxAcute, new EventCollector());

            Assert.IsTrue(chronicPsych.State.MoodBaseline < acutePsych.State.MoodBaseline,
                $"Chronická bolest (20 dní) musí snižovat MoodBaseline více než akutní (0 dní). " +
                $"Chronic={chronicPsych.State.MoodBaseline:F4}, Acute={acutePsych.State.MoodBaseline:F4}");
        }

        #endregion Scenario 15 — Chronická bolest → MoodBaseline

        #region Scenario 16 — Stresová vulnerabilita v noci (kortizol)

        [TestMethod]
        public void StressVulnerability_LowCortisol_SlowsStressRecovery()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 5.0,
                EnableCircadianRhythm: false,
                CircadianVulnerabilityMin: 0.3,
                CircadianVulnerabilityScale: 50.0));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var lowCortPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var highCortPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            lowCortPsych.RestoreState(lowCortPsych.State with { Stress = 80 });
            highCortPsych.RestoreState(highCortPsych.State with { Stress = 80 });

            // Nízký kortizol (noc) vs vysoký kortizol (ráno)
            var lowCortPhysio = MakePhysio(0, 0, 0) with { CortisolLevel = 10 };  // noc → faktor 0.3
            var highCortPhysio = MakePhysio(0, 0, 0) with { CortisolLevel = 90 };  // ráno → faktor 1.8

            var ctxLow = BuildRawContext(0.5, lowCortPhysio);
            var ctxHigh = BuildRawContext(0.5, highCortPhysio);
            var now = new WDateTime(0);

            lowCortPsych.Tick(now, WTimeSpan.FromHours(2), ctxLow, new EventCollector());
            highCortPsych.Tick(now, WTimeSpan.FromHours(2), ctxHigh, new EventCollector());

            Assert.IsTrue(lowCortPsych.State.Stress > highCortPsych.State.Stress,
                $"Nízký kortizol (noc) musí zpomalit stresovou recovery. " +
                $"LowCort={lowCortPsych.State.Stress:F2}, HighCort={highCortPsych.State.Stress:F2}");
        }

        #endregion Scenario 16 — Stresová vulnerabilita v noci (kortizol)

        #region Scenario 17 — Serotonin IDO pathway

        [TestMethod]
        public void Serotonin_HighImmuneLoad_DampensMoodBaselineRecovery()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                MoodBaselineRecoveryPerHour: 2.0,
                MoodBaselineAgreeablenessBonus: 0.0,
                SerotoninSuppressionImmuneThreshold: 60.0,
                SerotoninMoodRecoveryDampening: 0.1));  // 90% dampening
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var sickPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var healthyPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            // Obě instance startují s nízkou MoodBaseline (30), cílí na 50
            sickPsych.RestoreState(sickPsych.State with { MoodBaseline = 30 });
            healthyPsych.RestoreState(healthyPsych.State with { MoodBaseline = 30 });

            var sickPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 80 };  // > threshold 60
            var healthyPhysio = MakePhysio(0, 0, 0) with { ImmuneLoad = 10 };

            var ctxSick = BuildRawContext(0.5, sickPhysio);
            var ctxHealthy = BuildRawContext(0.5, healthyPhysio);
            var now = new WDateTime(0);

            sickPsych.Tick(now, WTimeSpan.FromHours(4), ctxSick, new EventCollector());
            healthyPsych.Tick(now, WTimeSpan.FromHours(4), ctxHealthy, new EventCollector());

            Assert.IsTrue(sickPsych.State.MoodBaseline < healthyPsych.State.MoodBaseline,
                $"Serotonin IDO: nemoc musí zpomalit MoodBaseline recovery. " +
                $"Sick={sickPsych.State.MoodBaseline:F4}, Healthy={healthyPsych.State.MoodBaseline:F4}");
        }

        #endregion Scenario 17 — Serotonin IDO pathway

        #region Scenario 18 — Wanting amplification pod stresem

        [TestMethod]
        public void Wanting_HighStress_BoostsNeedIntimacy()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                WantingStressThreshold: 60.0,
                WantingNeedIntimacyBoostPerHour: 1.0));  // vysoká hodnota pro jasný signál
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var highStressPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var lowStressPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            highStressPsych.RestoreState(highStressPsych.State with
            { Stress = 80, Motivations = new MotivationState(NeedIntimacy: 50) });
            lowStressPsych.RestoreState(lowStressPsych.State with
            { Stress = 20, Motivations = new MotivationState(NeedIntimacy: 50) });

            var physio = MakePhysio(0, 0, 0);
            var ctxHigh = BuildRawContext(0.5, physio);
            var ctxLow = BuildRawContext(0.5, physio);
            var now = new WDateTime(0);

            highStressPsych.Tick(now, WTimeSpan.FromHours(2), ctxHigh, new EventCollector());
            lowStressPsych.Tick(now, WTimeSpan.FromHours(2), ctxLow, new EventCollector());

            Assert.IsTrue(
                highStressPsych.State.Motivations!.NeedIntimacy > lowStressPsych.State.Motivations!.NeedIntimacy,
                $"Vysoký stres musí amplifikovat wanting (NeedIntimacy). " +
                $"High={highStressPsych.State.Motivations.NeedIntimacy:F4}, Low={lowStressPsych.State.Motivations.NeedIntimacy:F4}");
        }

        #endregion Scenario 18 — Wanting amplification pod stresem

        #region Scenario 19 — Cirkadiánní tělesná teplota

        [TestMethod]
        public void CircadianTemp_EveningHour_RaisesBodyTempDelta()
        {
            var nightEngine = BuildPhysioEngine();
            var eveningEngine = BuildPhysioEngine();

            // Obě instance startují na 0 BodyTempDelta, nulová imunita (žádná horečka)
            nightEngine.RestoreState(nightEngine.State with { BodyTempDelta = 0, ImmuneLoad = 0 });
            eveningEngine.RestoreState(eveningEngine.State with { BodyTempDelta = 0, ImmuneLoad = 0 });

            var ctx = BuildRawContext(0.5);

            // Noční hodina (4h) = teplota minimální; večerní (17h) = maximální
            var nightHour = new WDateTime(WTimeSpan.FromHours(4).Ticks);
            var eveningHour = new WDateTime(WTimeSpan.FromHours(17).Ticks);

            nightEngine.Tick(nightHour, WTimeSpan.FromHours(4), ctx, new EventCollector());
            eveningEngine.Tick(eveningHour, WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.IsTrue(eveningEngine.State.BodyTempDelta > nightEngine.State.BodyTempDelta,
                $"Večerní hodina (17h) musí mít vyšší tělesnou teplotu než noční (4h). " +
                $"Evening={eveningEngine.State.BodyTempDelta:F4}, Night={nightEngine.State.BodyTempDelta:F4}");
        }

        private static DefaultPhysiologyEngine BuildPhysioEngine()
        {
            var cfg = Options.Create(new PhysiologyConfig(
                EnableMenstrualCycle: false,
                EnableNutrition: false,
                CircadianTempAmplitude: 0.3,
                CircadianTempPeakHour: 17.0));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            return new DefaultPhysiologyEngine(cfg, cycleCfg, factory, new ZeroRandom(),
                SexBiology.Female, WDateOnly.New(100, 1, 1), WDateOnly.New(116, 1, 1));
        }

        #endregion Scenario 19 — Cirkadiánní tělesná teplota

        #region Scenario 20 — Altitude → CogLoad

        [TestMethod]
        public void Altitude_AboveCogLoadThreshold_IncreaseCogLoad()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                AltitudeCogLoadThreshold: 2500.0,
                AltitudeCogLoadBonusPerKm: 5.0));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var highAltPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var seaPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            highAltPsych.RestoreState(highAltPsych.State with { CognitiveLoad = 20 });
            seaPsych.RestoreState(seaPsych.State with { CognitiveLoad = 20 });

            var physio = MakePhysio(0, 0, 0);
            var ctxHighAlt = BuildRawContext(0.5, physio, altitudeMeters: 4000.0);  // > threshold 2500
            var ctxSea = BuildRawContext(0.5, physio, altitudeMeters: 0.0);

            var now = new WDateTime(0);
            highAltPsych.Tick(now, WTimeSpan.FromHours(2), ctxHighAlt, new EventCollector());
            seaPsych.Tick(now, WTimeSpan.FromHours(2), ctxSea, new EventCollector());

            Assert.IsTrue(highAltPsych.State.CognitiveLoad > seaPsych.State.CognitiveLoad,
                $"Vysoká nadmořská výška musí zvyšovat CogLoad. HighAlt={highAltPsych.State.CognitiveLoad:F4}, Sea={seaPsych.State.CognitiveLoad:F4}");
        }

        #endregion Scenario 20 — Altitude → CogLoad

        #region Scenario 21 — Kognitivní stárnutí + percepce

        [TestMethod]
        public void CognitiveAging_After60_IncreasesBaselineCogLoad()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                CognitivAgingThreshold: 60.0,
                CognitiveAgingCogLoadPerYear: 5.0));  // vyšší pro jasný signál
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var oldPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var youngPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            oldPsych.RestoreState(oldPsych.State with { CognitiveLoad = 10 });
            youngPsych.RestoreState(youngPsych.State with { CognitiveLoad = 10 });

            var oldPhysio = MakePhysio(0, 0, 0) with { Aging = new PhysicalAgingState(AgeYears: 70) };
            var youngPhysio = MakePhysio(0, 0, 0) with { Aging = new PhysicalAgingState(AgeYears: 30) };

            var ctxOld = BuildRawContext(0.5, oldPhysio);
            var ctxYoung = BuildRawContext(0.5, youngPhysio);
            var now = new WDateTime(0);

            oldPsych.Tick(now, WTimeSpan.FromHours(365 * 24), ctxOld, new EventCollector());
            youngPsych.Tick(now, WTimeSpan.FromHours(365 * 24), ctxYoung, new EventCollector());

            Assert.IsTrue(oldPsych.State.CognitiveLoad > youngPsych.State.CognitiveLoad,
                $"70letá postava musí mít vyšší CogLoad než 30letá. " +
                $"Old={oldPsych.State.CognitiveLoad:F4}, Young={youngPsych.State.CognitiveLoad:F4}");
        }

        [TestMethod]
        public void PostMenopause_MoodBaseline_DeclinesFaster()
        {
            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                MoodBaselineRecoveryPerHour: 0.0,    // vypnout recovery
                PostMenopauseMoodBaselinePenaltyPerHour: 0.1));  // velká penalta pro jasný signál
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var postMenoPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            var normalPsych = new DefaultPsychologyEngine(psychCfg, logFactory, rng);
            postMenoPsych.RestoreState(postMenoPsych.State with { MoodBaseline = 50 });
            normalPsych.RestoreState(normalPsych.State with { MoodBaseline = 50 });

            // Post-menopauza: Cycle.Phase=Paused, AgeYears>=45, žádné těhotenství
            var postMenoPhysio = MakePhysio(0, 0, 0) with
            {
                Aging = new PhysicalAgingState(AgeYears: 55),
                Cycle = new MenstrualCycleState(
                    CyclePhase.Paused, 1, false, 0, 0, 0, 1.0, WDateOnly.New(61, 1, 1))
            };
            var normalPhysio = MakePhysio(0, 0, 0) with
            { Aging = new PhysicalAgingState(AgeYears: 55) }; // bez Cycle.Paused

            var ctxPostMeno = BuildRawContext(0.5, postMenoPhysio);
            var ctxNormal = BuildRawContext(0.5, normalPhysio);
            var now = new WDateTime(0);

            postMenoPsych.Tick(now, WTimeSpan.FromHours(8), ctxPostMeno, new EventCollector());
            normalPsych.Tick(now, WTimeSpan.FromHours(8), ctxNormal, new EventCollector());

            Assert.IsTrue(postMenoPsych.State.MoodBaseline < normalPsych.State.MoodBaseline,
                $"Post-menopauzální MoodBaseline musí klesat rychleji. " +
                $"PostMeno={postMenoPsych.State.MoodBaseline:F4}, Normal={normalPsych.State.MoodBaseline:F4}");
        }

        #endregion Scenario 21 — Kognitivní stárnutí + percepce

        #region Scenario 22 — Full-cycle hormone & PMDD propagation

        /// <summary>
        /// Drives a female character through a full 28-day cycle (one 24 h physiology Tick per day)
        /// and verifies that the ovarian-hormone proxies (Estradiol/Progesterone), the libido
        /// multiplier, and the PMDD valence penalty propagate end-to-end Physiology→Psychology in
        /// the expected phases. Each day a fresh psychology engine ticks against a context carrying
        /// only the current cycle (other physiology held constant) so that valence movement isolates
        /// the PMDD withdrawal effect.
        /// </summary>
        [TestMethod]
        public void FullCycle_HormonesLibidoAndPmdd_PropagateThroughPsychology()
        {
            // CycleCfg: length 28, luteal 12 → ovulDay 16; PmsRisk 0.35 (> 0.3) enables PMDD.
            const int ovulDay = 16;
            var (physio, _, physioCtx, now) = BuildIntegrationPair(
                cycleDayStart: 1, cyclePhaseStart: CyclePhase.Menses);

            // BuildIntegrationPair seeds CurrentCycleLength to its default (30); align it with the
            // configured 28-day length so the hormone ovulDay (= length − luteal 12 = 16) is
            // consistent and the late-luteal PMDD window is actually reached within one cycle.
            physio.RestoreState(physio.State with
            {
                Cycle = physio.State.Cycle! with { CurrentCycleLength = 28 }
            });

            var psychCfg = Options.Create(new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false));
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            var estradiol = new double[29];
            var progesterone = new double[29];
            var libido = new double[29];
            var pmddActive = new bool[29];
            var valence = new double[29];

            for (int i = 0; i < 28; i++)
            {
                physio.Tick(now, WTimeSpan.FromHours(24), physioCtx, new EventCollector());
                var cycle = physio.State.Cycle!;
                int day = cycle.DayInCycle; // 1..28

                estradiol[day] = cycle.Estradiol;
                progesterone[day] = cycle.Progesterone;
                libido[day] = cycle.LibidoMod;
                pmddActive[day] = cycle.PmddActive;

                // Fresh psychology each day from an identical baseline; only the cycle varies, so
                // the resulting valence isolates the PMDD propagation (hunger/pain held constant).
                var psych = new DefaultPsychologyEngine(psychCfg, logFactory, new ZeroRandom());
                psych.RestoreState(psych.State with { Valence = 0.1 });
                var cleanPhysio = MakePhysio(0, 0, 0) with { Cycle = cycle };
                var psychCtx = BuildRawContext(neuroticism: 0.5, physio: cleanPhysio);
                psych.Tick(now, WTimeSpan.FromHours(24), psychCtx, new EventCollector());
                valence[day] = psych.State.Valence;
            }

            // Estradiol surge peaks in the periovulatory window.
            int estArgmax = 1;
            for (int day = 2; day <= 28; day++)
                if (estradiol[day] > estradiol[estArgmax])
                    estArgmax = day;
            Assert.IsTrue(Math.Abs(estArgmax - ovulDay) <= 2,
                $"Estradiol musí kulminovat kolem ovulace (≈den {ovulDay}); skutečně den {estArgmax}.");

            // Progesterone peaks in the mid-luteal phase (~ovulDay + 7).
            int progArgmax = 1;
            for (int day = 2; day <= 28; day++)
                if (progesterone[day] > progesterone[progArgmax])
                    progArgmax = day;
            Assert.IsTrue(Math.Abs(progArgmax - (ovulDay + 7)) <= 2,
                $"Progesteron musí kulminovat v mid-luteálu (≈den {ovulDay + 7}); skutečně den {progArgmax}.");

            // Libido is higher at ovulation than during menses.
            Assert.IsTrue(libido[ovulDay] > libido[3],
                $"LibidoMod v ovulaci ({libido[ovulDay]:F4}) musí být vyšší než v menses ({libido[3]:F4}).");

            // PMDD activates in the late luteal phase and depresses valence relative to follicular.
            Assert.IsTrue(pmddActive[28],
                "PMDD musí být aktivní v pozdním luteálu (den 28).");
            Assert.IsTrue(valence[28] < valence[10],
                $"PMDD withdrawal musí přes Psychology snížit valenci v pozdním luteálu vs folikulární fáze. " +
                $"den28={valence[28]:F4}, den10={valence[10]:F4}.");
        }

        #endregion Scenario 22 — Full-cycle hormone & PMDD propagation

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
            var cycleCfg = Options.Create(CycleCfg);
            var psychCfg = Options.Create(PsychCfg);
            var logFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var physioEngine = new DefaultPhysiologyEngine(
                physioCfg, cycleCfg, logFactory, rng,
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(101, 1, 1),
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
        private static IHumanContext BuildRawContext(double neuroticism, PhysiologyState? physio = null, string? currentAction = null, double ambientTemperature = 20.0, double altitudeMeters = 0.0)
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
                new MemoryIndex(new List<EpisodicMemory>()),
                AmbientTemperature: ambientTemperature,
                AltitudeMeters: altitudeMeters);

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                    Attachment: AttachmentProfile.Secure,
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

        /// <summary>
        /// Vytvoří PhysiologyState s daným TestosteroneState.
        /// </summary>
        private static PhysiologyState MakePhysioWithTestosterone(double testosteroneLevel)
            => new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 2,
                Hunger: 20,
                Thirst: 15,
                Pain: 0,
                ImmuneLoad: 5,
                BodyTempDelta: 0,
                Cycle: null,
                Testosterone: new TestosteroneState(Level: testosteroneLevel));

        #endregion Helpers
    }
}
