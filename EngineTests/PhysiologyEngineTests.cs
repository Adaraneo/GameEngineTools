// PhysiologyEngineTests.cs
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
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Unit testy pro <see cref="DefaultPhysiologyEngine"/>.
    /// Pokrývá reakci na <see cref="SleepEnded"/> a ověřuje,
    /// že mrtvý kód pro <c>ActionCommitted("Sleep")</c> již neexistuje.
    /// </summary>
    [TestClass]
    public class PhysiologyEngineTests : TestBase
    {
        #region Soukromá pole

        private IEventCollector _outbox = default!;
        private WDateTime _now;
        private IHumanContext _ctx = default!;

        #endregion Soukromá pole

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
            _ctx = BuildContext();
        }

        #endregion Setup

        #region SleepEnded — spánkový dluh

        /// <summary>
        /// Kvalitní spánek (kvalita 100) musí výrazně snížit spánkový dluh.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_PerfectQuality_ReducesSleepDebt()
        {
            // Arrange
            var engine = BuildEngine(sleepDebtHours: 8);
            var ended = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);
            var debtBefore = engine.State.SleepDebtHours;

            // Act
            engine.Handle(ended, _ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.SleepDebtHours < debtBefore,
                $"Perfektní spánek musí snížit dluh. Před: {debtBefore:F2}, po: {engine.State.SleepDebtHours:F2}");
        }

        /// <summary>
        /// Přerušený spánek nízké kvality (kvalita 0) musí snížit dluh minimálně.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_ZeroQuality_ReducesDebtMinimally()
        {
            // Arrange
            var engine = BuildEngine(sleepDebtHours: 8);
            var fullEnded = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);
            var badEnded = MakeSleepEnded(quality: 0, hoursSlept: 8, wasInterrupted: true);

            var fullEngine = BuildEngine(sleepDebtHours: 8);
            var badEngine = BuildEngine(sleepDebtHours: 8);

            // Act
            fullEngine.Handle(fullEnded, _ctx, new EventCollector());
            badEngine.Handle(badEnded, _ctx, new EventCollector());

            // Assert — plný spánek splatí více dluhu než nulová kvalita
            Assert.IsTrue(
                fullEngine.State.SleepDebtHours < badEngine.State.SleepDebtHours,
                $"Kvalitní spánek musí splatit více dluhu. " +
                $"Plný={fullEngine.State.SleepDebtHours:F2}, Špatný={badEngine.State.SleepDebtHours:F2}");
        }

        /// <summary>
        /// Spánkový dluh nesmí klesnout pod 0 ani po velmi dlouhém kvalitním spánku.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_DebtNeverGoesNegative()
        {
            // Arrange — malý dluh, velký spánek
            var engine = BuildEngine(sleepDebtHours: 1);
            var ended = MakeSleepEnded(quality: 100, hoursSlept: 12, wasInterrupted: false);

            // Act
            engine.Handle(ended, _ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.SleepDebtHours >= 0,
                $"SleepDebtHours nesmí být záporný. Aktuálně: {engine.State.SleepDebtHours:F4}");
        }

        /// <summary>
        /// Délka spánku ovlivňuje kolik dluhu se splatí — delší spánek splatí více.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_LongerSleep_ReducesMoreDebt()
        {
            // Arrange — stejná kvalita, různá délka
            var shortEngine = BuildEngine(sleepDebtHours: 10);
            var longEngine = BuildEngine(sleepDebtHours: 10);

            var shortSleep = MakeSleepEnded(quality: 80, hoursSlept: 4, wasInterrupted: false);
            var longSleep = MakeSleepEnded(quality: 80, hoursSlept: 8, wasInterrupted: false);

            // Act
            shortEngine.Handle(shortSleep, _ctx, new EventCollector());
            longEngine.Handle(longSleep, _ctx, new EventCollector());

            // Assert
            Assert.IsTrue(
                longEngine.State.SleepDebtHours < shortEngine.State.SleepDebtHours,
                $"Delší spánek musí splatit více dluhu. " +
                $"Krátký={shortEngine.State.SleepDebtHours:F2}, Dlouhý={longEngine.State.SleepDebtHours:F2}");
        }

        #endregion SleepEnded — spánkový dluh

        #region SleepEnded — imunita a bolest

        /// <summary>
        /// Kvalitní spánek musí regenerovat imunitní systém (snížit ImmuneLoad).
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_GoodQuality_ReducesImmuneLoad()
        {
            // Arrange
            var engine = BuildEngine(immuneLoad: 50);
            var ended = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);
            var loadBefore = engine.State.ImmuneLoad;

            // Act
            engine.Handle(ended, _ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.ImmuneLoad < loadBefore,
                $"Kvalitní spánek musí snížit ImmuneLoad. Před: {loadBefore:F1}, po: {engine.State.ImmuneLoad:F1}");
        }

        /// <summary>
        /// Kvalitní spánek (>= 60) musí mírně snížit bolest.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_QualityAbove60_ReducesPain()
        {
            // Arrange
            var engine = BuildEngine(pain: 30);
            var ended = MakeSleepEnded(quality: 80, hoursSlept: 8, wasInterrupted: false);
            var painBefore = engine.State.Pain;

            // Act
            engine.Handle(ended, _ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Pain < painBefore,
                $"Spánek kvality >= 60 musí snížit bolest. Před: {painBefore:F1}, po: {engine.State.Pain:F1}");
        }

        /// <summary>
        /// Nekvalitní spánek (< 60) nesmí snižovat bolest.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_QualityBelow60_DoesNotReducePain()
        {
            // Arrange
            var engine = BuildEngine(pain: 30);
            var ended = MakeSleepEnded(quality: 30, hoursSlept: 2, wasInterrupted: true);
            var painBefore = engine.State.Pain;

            // Act
            engine.Handle(ended, _ctx, _outbox);

            // Assert
            Assert.AreEqual(painBefore, engine.State.Pain, delta: 0.001,
                "Spánek kvality < 60 nesmí snižovat bolest.");
        }

        #endregion SleepEnded — imunita a bolest

        #region Menstrual Cycle

        [TestMethod]
        public void Ctor_MenstrualCycleEnabledForAgeLessThanInConfiguration_ReturnsNullCycle()
        {
            var engine = BuildEngine(birthYear: 111, cycleEnabled: true);

            Assert.IsNull(engine.State.Cycle);
        }

        [TestMethod]
        public void Ctor_MenstrualCycleEnabledForAgeGreaterThanInConfiguration_ReturnsNotNullCycle()
        {
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);

            Assert.IsNotNull(engine.State.Cycle);
        }

        [TestMethod]
        public void Ctor_MenstrualCycleDiabled_ReturnsNullCycle()
        {
            var engine = BuildEngine();

            Assert.IsNull(engine.State.Cycle);
        }

        #endregion Menstrual Cycle

        #region Menstrual Cycle — cycle length randomness

        /// <summary>
        /// With ZeroRandom (Normal returns 0), cycle length equals MeanCycleLengthDays (28).
        /// After 28 ticks of 24 h each the engine wraps back to day 1.
        /// </summary>
        [TestMethod]
        public void AdvanceCycleDay_WithZeroRandom_CycleLengthEqualsMean()
        {
            // Arrange — cycle-enabled engine, start the cycle at day 1
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);
            var ctx = BuildContext();
            var outbox = new EventCollector();
            var now = new WDateTime(0);

            // Seed the cycle to day 1 so we can count a full revolution
            var initialCycle = engine.State.Cycle!;
            engine.RestoreState(engine.State with
            {
                Cycle = initialCycle with { DayInCycle = 1, Phase = CyclePhase.Menses }
            });

            // Act — advance exactly 28 days (each Tick accumulates 24 h → one cycle-day advance)
            int wraps = 0;
            int prevDay = engine.State.Cycle!.DayInCycle;
            for (int i = 0; i < 28; i++)
            {
                engine.Tick(now, WTimeSpan.FromHours(24), ctx, outbox);
                var newDay = engine.State.Cycle!.DayInCycle;
                if (newDay < prevDay)
                    wraps++;
                prevDay = newDay;
            }

            // With ZeroRandom Normal(0, std)=0, length = Clamp(28+0, 21, 35) = 28 → exactly 1 wrap
            Assert.AreEqual(1, wraps,
                $"ZeroRandom must produce cycle length of 28 days (one wrap after 28 advances). Wraps={wraps}");
        }

        /// <summary>
        /// Cycle length is always clamped to the biological minimum of 21 days.
        /// </summary>
        [TestMethod]
        public void AdvanceCycleDay_CycleLengthNeverBelow21Days()
        {
            // Use a random source that always returns 0 for the Box-Muller inputs
            // which produces the mean; verify the clamp by checking the wrap point.
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);
            var ctx = BuildContext();
            var outbox = new EventCollector();
            var now = new WDateTime(0);

            engine.RestoreState(engine.State with
            {
                Cycle = engine.State.Cycle! with { DayInCycle = 1 }
            });

            // Advance 20 days — should NOT wrap regardless of length distribution
            for (int i = 0; i < 20; i++)
                engine.Tick(now, WTimeSpan.FromHours(24), ctx, outbox);

            Assert.IsTrue(engine.State.Cycle!.DayInCycle >= 1,
                "After 20 advances the cycle must still be in progress (minimum 21 days).");
        }

        /// <summary>
        /// Cycle length is always clamped to the biological maximum of 35 days.
        /// A cycle advanced 36 times must have wrapped at least once.
        /// </summary>
        [TestMethod]
        public void AdvanceCycleDay_CycleLengthNeverExceeds35Days()
        {
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);
            var ctx = BuildContext();
            var outbox = new EventCollector();
            var now = new WDateTime(0);

            engine.RestoreState(engine.State with
            {
                Cycle = engine.State.Cycle! with { DayInCycle = 1 }
            });

            int wraps = 0;
            int prev = engine.State.Cycle!.DayInCycle;
            for (int i = 0; i < 36; i++)
            {
                engine.Tick(now, WTimeSpan.FromHours(24), ctx, outbox);
                var cur = engine.State.Cycle!.DayInCycle;
                if (cur < prev) wraps++;
                prev = cur;
            }

            Assert.IsTrue(wraps >= 1,
                $"After 36 advances the cycle must have wrapped at least once (max length 35). Wraps={wraps}");
        }

        #endregion Menstrual Cycle — cycle length randomness

        #region Conception chance

        /// <summary>
        /// No contraception during ovulation window must produce a higher conception chance
        /// than no contraception outside the ovulation window.
        /// </summary>
        [TestMethod]
        public void ConceptionChance_OvulationWindow_HigherThanOutsideOvulation()
        {
            // Arrange — test ovulation window boost on conception chance
            var outboxOvul = new EventCollector();
            var outboxNon = new EventCollector();

            // Act — ovulation window with AlwaysConceiveRandom should conceive
            var ovulEngineConceiving = BuildEngineWithCycle(ovulationWindowOpen: true, alwaysConceive: true);
            var ctxConceiving = BuildContextForConception(SexBiology.Female, alwaysChance: true);
            var encounterOvul = MakeEncounter(ctxConceiving.Id, ReproductiveIntent.OpenToPregnancy, ContraceptionLevel.None);
            ovulEngineConceiving.Handle(encounterOvul, ctxConceiving, outboxOvul);

            // Outside ovulation window with ZeroRandom should not conceive
            var nonOvulEngineNot = BuildEngineWithCycle(ovulationWindowOpen: false, alwaysConceive: false);
            var ctxNot = BuildContextForConception(SexBiology.Female, alwaysChance: false);
            var encounterNon = MakeEncounter(ctxNot.Id, ReproductiveIntent.OpenToPregnancy, ContraceptionLevel.None);
            nonOvulEngineNot.Handle(encounterNon, ctxNot, outboxNon);

            var ovulEvents = outboxOvul.Drain();
            var nonEvents = outboxNon.Drain();

            Assert.IsTrue(ovulEvents.OfType<PregnancyStarted>().Any(),
                "Ovulation window + no contraception + Chance=true must result in PregnancyStarted.");
            Assert.IsFalse(nonEvents.OfType<PregnancyStarted>().Any(),
                "Outside ovulation + Chance=false must not result in pregnancy.");
        }

        /// <summary>
        /// High contraception (0.04 modifier) must prevent pregnancy even during ovulation
        /// when the base-chance random roll is seeded to never conceive.
        /// </summary>
        [TestMethod]
        public void ConceptionChance_HighContraception_PreventsConception()
        {
            var engine = BuildEngineWithCycle(ovulationWindowOpen: true, alwaysConceive: false);
            var ctx = BuildContextForConception(SexBiology.Female, alwaysChance: false);
            var outbox = new EventCollector();
            var encounter = MakeEncounter(ctx.Id, ReproductiveIntent.OpenToPregnancy, ContraceptionLevel.High);

            engine.Handle(encounter, ctx, outbox);

            Assert.IsFalse(outbox.Drain().OfType<PregnancyStarted>().Any(),
                "High contraception with Chance=false must not produce a pregnancy.");
        }

        /// <summary>
        /// A male biology context must never start a pregnancy regardless of ovulation or contraception.
        /// </summary>
        [TestMethod]
        public void ConceptionChance_MaleBiology_NeverConceives()
        {
            var engine = BuildEngineWithCycle(ovulationWindowOpen: true, alwaysConceive: true);
            var ctx = BuildContextForConception(SexBiology.Male, alwaysChance: true);
            var outbox = new EventCollector();
            var encounter = MakeEncounter(ctx.Id, ReproductiveIntent.TryingForChild, ContraceptionLevel.None);

            engine.Handle(encounter, ctx, outbox);

            Assert.IsFalse(outbox.Drain().OfType<PregnancyStarted>().Any(),
                "Male biology must never start a pregnancy.");
        }

        #endregion Conception chance

        #region Pregnancy discovery timing

        /// <summary>
        /// Before PregnancyDiscoveryMinDays, the pregnancy must not be marked as Discovered.
        /// </summary>
        [TestMethod]
        public void AdvancePregnancy_BeforeDiscoveryMinDays_NotDiscovered()
        {
            var engine = BuildEngine();
            var ctx = BuildContext();
            var outbox = new EventCollector();

            var conceivedOn = WDateOnly.New(116, 1, 1);
            var now = conceivedOn.AddDays(10).ToDateTime(); // 10 days < 21 min

            engine.RestoreState(engine.State with
            {
                Pregnancy = new PregnancyState(
                    OtherParent: new HumanId(Guid.NewGuid()),
                    ConceivedOn: conceivedOn,
                    EstimatedDueDate: conceivedOn.AddDays(280))
            });

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            Assert.IsFalse(engine.State.Pregnancy!.Discovered,
                "Pregnancy must not be Discovered before PregnancyDiscoveryMinDays (21).");
            Assert.IsFalse(outbox.Drain().OfType<PregnancyDiscovered>().Any(),
                "PregnancyDiscovered must not be emitted before the minimum days.");
        }

        /// <summary>
        /// After PregnancyDiscoveryMinDays, the Discovered flag must flip and PregnancyDiscovered must be emitted.
        /// </summary>
        [TestMethod]
        public void AdvancePregnancy_AfterDiscoveryMinDays_FlipsDiscoveredAndEmitsEvent()
        {
            var engine = BuildEngine();
            var ctx = BuildContext();
            var outbox = new EventCollector();

            var conceivedOn = WDateOnly.New(116, 1, 1);
            // 22 days > default 21 minimum
            var now = conceivedOn.AddDays(22).ToDateTime();

            engine.RestoreState(engine.State with
            {
                Pregnancy = new PregnancyState(
                    OtherParent: new HumanId(Guid.NewGuid()),
                    ConceivedOn: conceivedOn,
                    EstimatedDueDate: conceivedOn.AddDays(280))
            });

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            Assert.IsTrue(engine.State.Pregnancy!.Discovered,
                "Pregnancy must be Discovered after PregnancyDiscoveryMinDays.");
            Assert.IsTrue(outbox.Drain().OfType<PregnancyDiscovered>().Any(),
                "PregnancyDiscovered event must be emitted when discovery threshold is crossed.");
        }

        #endregion Pregnancy discovery timing

        #region Postpartum state

        /// <summary>
        /// After ChildBorn is emitted (EstimatedDueDate reached), Pregnancy is cleared
        /// and the cycle transitions to CyclePhase.Paused with LibidoMod = 0.8.
        /// </summary>
        [TestMethod]
        public void AdvancePregnancy_OnDueDate_EmitsChildBornAndPausesCycle()
        {
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);
            var ctx = BuildContext();
            var outbox = new EventCollector();

            // Set due date to today
            var dueDate = WDateOnly.New(116, 1, 1);
            var now = dueDate.ToDateTime();

            engine.RestoreState(engine.State with
            {
                Pregnancy = new PregnancyState(
                    OtherParent: new HumanId(Guid.NewGuid()),
                    ConceivedOn: dueDate.AddDays(-280),
                    EstimatedDueDate: dueDate,
                    Discovered: true)
            });

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            var events = outbox.Drain();
            Assert.IsNull(engine.State.Pregnancy,
                "Pregnancy record must be cleared after birth.");
            Assert.IsTrue(events.OfType<ChildBorn>().Any(),
                "ChildBorn event must be emitted on the due date.");
        }

        /// <summary>
        /// After birth, if the character had a cycle, it transitions to Paused with LibidoMod = 0.8.
        /// </summary>
        [TestMethod]
        public void AdvancePregnancy_OnDueDate_CyclePausedWithReducedLibido()
        {
            var engine = BuildEngine(birthYear: 13, cycleEnabled: true);
            var ctx = BuildContext();
            var outbox = new EventCollector();

            var dueDate = WDateOnly.New(116, 1, 1);
            var now = dueDate.ToDateTime();

            // Ensure a cycle exists
            Assert.IsNotNull(engine.State.Cycle, "Pre-condition: cycle must exist for this test.");

            engine.RestoreState(engine.State with
            {
                Pregnancy = new PregnancyState(
                    OtherParent: new HumanId(Guid.NewGuid()),
                    ConceivedOn: dueDate.AddDays(-280),
                    EstimatedDueDate: dueDate,
                    Discovered: true)
            });

            engine.Tick(now, WTimeSpan.FromHours(1), ctx, outbox);

            Assert.AreEqual(CyclePhase.Paused, engine.State.Cycle?.Phase,
                "Cycle must be Paused after childbirth (postpartum amenorrhea).");
            Assert.AreEqual(0.8, engine.State.Cycle?.LibidoMod ?? 0, delta: 0.001,
                "LibidoMod must be 0.8 during postpartum period.");
        }

        #endregion Postpartum state

        #region Immune load and fever

        /// <summary>
        /// BodyTempDelta approaches the fever target proportional to ImmuneLoad.
        /// With ImmuneLoad > 30, target fever = (ImmuneLoad - 30) / 70 * 2.0 > 0.
        /// </summary>
        [TestMethod]
        public void Tick_ImmuneLoad_AboveThreshold_RaisesBodyTempDelta()
        {
            var highImmuneEngine = BuildEngine(immuneLoad: 80, birthYear: 100, todayYear: 116);
            var lowImmuneEngine  = BuildEngine(immuneLoad: 10, birthYear: 100, todayYear: 116);
            var ctx = BuildContext();

            // Start both at neutral temp
            highImmuneEngine.RestoreState(highImmuneEngine.State with { BodyTempDelta = 0 });
            lowImmuneEngine.RestoreState(lowImmuneEngine.State with { BodyTempDelta = 0 });

            highImmuneEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctx, new EventCollector());
            lowImmuneEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.IsTrue(highImmuneEngine.State.BodyTempDelta > lowImmuneEngine.State.BodyTempDelta,
                $"High ImmuneLoad must drive BodyTempDelta higher. " +
                $"High={highImmuneEngine.State.BodyTempDelta:F4}, Low={lowImmuneEngine.State.BodyTempDelta:F4}");
        }

        /// <summary>
        /// With ImmuneLoad at or below 30, fever target is 0; BodyTempDelta must not increase.
        /// </summary>
        [TestMethod]
        public void Tick_ImmuneLoad_AtOrBelowThreshold_NoFeverDevelopment()
        {
            var engine = BuildEngine(immuneLoad: 30);
            engine.RestoreState(engine.State with { BodyTempDelta = 0 });
            var ctx = BuildContext();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.AreEqual(0.0, engine.State.BodyTempDelta, delta: 0.01,
                "ImmuneLoad = 30 produces fever target = 0; BodyTempDelta must not increase.");
        }

        #endregion Immune load and fever

        #region Pomocné metody

        /// <summary>Sestaví engine s nastavenými počátečními hodnotami.</summary>
        private static DefaultPhysiologyEngine BuildEngine(
            double sleepDebtHours = 2,
            double energy = 70,
            double hunger = 25,
            double thirst = 20,
            double pain = 5,
            double immuneLoad = 10,
            int birthYear = 100,
            int todayYear = 116,
            bool cycleEnabled = false)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                RestingMetabolicRate: 1600,
                MaxSleepDebtHours: 12,
                EnableMenstrualCycle: cycleEnabled,
                MenstrualCycleBeginsInAge: 12));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var engine = new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, rng,
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(birthYear, 1, 1),
                now: WDateOnly.New(todayYear, 1, 1));

            engine.RestoreState(new PhysiologyState(
                Energy: energy,
                SleepDebtHours: sleepDebtHours,
                Hunger: hunger,
                Thirst: thirst,
                Pain: pain,
                ImmuneLoad: immuneLoad,
                BodyTempDelta: 0,
                Cycle: engine.State.Cycle));

            return engine;
        }

        /// <summary>Sestaví minimální fake kontext — PhysiologyEngine ho v Handle() nepotřebuje.</summary>
        private static IHumanContext BuildContext()
        {
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    AttachmentStyle.Secure,
                    CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate,
                    Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                                           .CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>Vytvoří <see cref="SleepEnded"/> s danými parametry.</summary>
        private SleepEnded MakeSleepEnded(double quality, double hoursSlept, bool wasInterrupted)
            => new SleepEnded(
                OccurredAt: _now,
                Human: new HumanId(Guid.NewGuid()),
                TotalHoursSlept: hoursSlept,
                Quality: quality,
                WasInterrupted: wasInterrupted);

        /// <summary>
        /// Builds an engine with a cycle seeded to a specific ovulation-window state.
        /// Uses a custom <see cref="IRandomSource"/> to control conception roll outcome.
        /// </summary>
        private static DefaultPhysiologyEngine BuildEngineWithCycle(
            bool ovulationWindowOpen,
            bool alwaysConceive = false)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                RestingMetabolicRate: 1600,
                MaxSleepDebtHours: 12,
                EnableMenstrualCycle: true,
                MenstrualCycleBeginsInAge: 12));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            IRandomSource rng = alwaysConceive ? new AlwaysConceiveRandom() : (IRandomSource)new ZeroRandom();

            var engine = new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, rng,
                biology: SexBiology.Female,
                birthDate: WDateOnly.New(13, 1, 1),
                now: WDateOnly.New(116, 1, 1));

            // Override the cycle to control the ovulation window precisely
            var phase = ovulationWindowOpen ? CyclePhase.Ovulation : CyclePhase.Follicular;
            engine.RestoreState(engine.State with
            {
                Cycle = new MenstrualCycleState(
                    Phase: phase,
                    DayInCycle: ovulationWindowOpen ? 14 : 7,
                    OvulationWindow: ovulationWindowOpen,
                    SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                    LibidoMod: 1.0,
                    LastMensesStart: WDateOnly.New(116, 1, 1))
            });

            return engine;
        }

        /// <summary>
        /// Builds a context for conception tests with configurable biology and chance outcome.
        /// </summary>
        private static IHumanContext BuildContextForConception(SexBiology biology, bool alwaysChance = true)
        {
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            IRandomSource rng = alwaysChance ? new AlwaysConceiveRandom() : (IRandomSource)new ZeroRandom();

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = biology,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    AttachmentStyle.Secure,
                    CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate,
                    Chronotype.Neutral),
                Snapshot = snapshot,
                Random = rng,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>Creates a <see cref="SexualEncounterOutcome"/> directed at the given character.</summary>
        private static SexualEncounterOutcome MakeEncounter(
            HumanId to,
            ReproductiveIntent intent,
            ContraceptionLevel contraception)
            => new SexualEncounterOutcome(
                OccurredAt: new WDateTime(0),
                From: new HumanId(Guid.NewGuid()),
                To: to,
                Accepted: true,
                Reason: string.Empty,
                Intent: intent,
                Contraception: contraception,
                ReproductivePotential: true);

        #endregion Pomocné metody

        #region Fake / Stub implementace

        /// <summary>
        /// Random source that always returns true for <c>Chance()</c>, used to guarantee
        /// conception in tests that verify the conception code path.
        /// </summary>
        private sealed class AlwaysConceiveRandom : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            // Always conceive — probability check always passes
            public bool Chance(double p) => true;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => true;

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime now)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        #endregion Fake / Stub implementace
    }
}
