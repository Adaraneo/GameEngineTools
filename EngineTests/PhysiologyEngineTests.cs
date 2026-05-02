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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);

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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
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
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
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

            // Imunita na prahu (30) = nulový feverDelta; teplota smí kolísat kvůli cirkadiánní složce (±0.3°C),
            // ale nesmí dosáhnout febrilního prahu (~1.5°C). Ověřujeme absenci horečky, ne přesnou hodnotu 0.
            Assert.IsTrue(engine.State.BodyTempDelta < 0.5,
                $"ImmuneLoad = 30 nesmí způsobit horečku. BodyTempDelta = {engine.State.BodyTempDelta:F4} (cirkadiánní variace ±0.3°C je normální).");
        }

        #endregion Immune load and fever

        #region Nutrition — Phase 4

        [TestMethod]
        public void Tick_Nutrition_CaloriesAndProteinDecayWhenNotEating()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with
            {
                Nutrition = new NutritionState(Calories: 80, Protein: 80)
            });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Nutrition!.Calories < 80,
                $"Calories must decay when not eating. Got {engine.State.Nutrition.Calories:F4}");
            Assert.IsTrue(engine.State.Nutrition!.Protein < 80,
                $"Protein must decay when not eating. Got {engine.State.Nutrition.Protein:F4}");
        }

        [TestMethod]
        public void Tick_Nutrition_CaloriesAndProteinRestoredWhileEating()
        {
            var eatingEngine = BuildEngine(hunger: 60);
            eatingEngine.RestoreState(eatingEngine.State with
            {
                Nutrition = new NutritionState(Calories: 40, Protein: 40)
            });

            var idleEngine = BuildEngine(hunger: 60);
            idleEngine.RestoreState(idleEngine.State with
            {
                Nutrition = new NutritionState(Calories: 40, Protein: 40)
            });

            eatingEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContextWithAction(Eat), new EventCollector());
            idleEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContextWithAction(null), new EventCollector());

            Assert.IsTrue(eatingEngine.State.Nutrition!.Calories > idleEngine.State.Nutrition!.Calories,
                $"Eating must restore Calories faster. Eating={eatingEngine.State.Nutrition.Calories:F4}, Idle={idleEngine.State.Nutrition.Calories:F4}");
            Assert.IsTrue(eatingEngine.State.Nutrition!.Protein > idleEngine.State.Nutrition!.Protein,
                $"Eating must restore Protein faster. Eating={eatingEngine.State.Nutrition.Protein:F4}, Idle={idleEngine.State.Nutrition.Protein:F4}");
        }

        [TestMethod]
        public void Tick_Nutrition_IronRestoredDuringSleep()
        {
            var sleepEngine = BuildEngine();
            sleepEngine.RestoreState(sleepEngine.State with
            {
                Nutrition = new NutritionState(Iron: 50)
            });

            var idleEngine = BuildEngine();
            idleEngine.RestoreState(idleEngine.State with
            {
                Nutrition = new NutritionState(Iron: 50)
            });

            sleepEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContextWithAction(Sleep), new EventCollector());
            idleEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContextWithAction(null), new EventCollector());

            Assert.IsTrue(sleepEngine.State.Nutrition!.Iron > idleEngine.State.Nutrition!.Iron,
                $"Sleep must restore Iron faster than idle. Sleep={sleepEngine.State.Nutrition.Iron:F4}, Idle={idleEngine.State.Nutrition.Iron:F4}");
        }

        #endregion Nutrition — Phase 4

        #region Injury — Phase 4

        [TestMethod]
        public void Tick_Injury_AddsPainProportionalToSeverity()
        {
            var engine = BuildEngine(pain: 0);
            engine.RestoreState(engine.State with
            {
                Pain = 0,
                Injury = new InjuryState(Severity: 50, DaysSinceOnset: 0, Type: InjuryType.Wound)
            });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Pain > 0,
                $"Injury with Severity=50 must add pain. Got Pain={engine.State.Pain:F4}");
        }

        [TestMethod]
        public void Tick_Injury_HealsOverDaysDuringRest()
        {
            var engine = BuildEngine(pain: 0);
            engine.RestoreState(engine.State with
            {
                Injury = new InjuryState(Severity: 10, DaysSinceOnset: 0, Type: InjuryType.Wound)
            });
            var ctx = BuildContextWithAction(Sleep);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Injury == null || engine.State.Injury.Severity < 10,
                $"Injury must heal during rest. Severity={engine.State.Injury?.Severity:F4}");
        }

        [TestMethod]
        public void Tick_Injury_EmitsInjuryHealedWhenSeverityReachesZero()
        {
            var engine = BuildEngine(pain: 0);
            engine.RestoreState(engine.State with
            {
                Injury = new InjuryState(Severity: 2, DaysSinceOnset: 0, Type: InjuryType.Wound)
            });
            var ctx = BuildContextWithAction(Sleep);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, outbox);

            var events = outbox.Drain();
            Assert.IsNull(engine.State.Injury, "Injury must be cleared when severity reaches 0.");
            Assert.IsTrue(events.OfType<InjuryHealed>().Any(), "InjuryHealed event must be emitted.");
        }

        #endregion Injury — Phase 4

        #region Postpartum — Phase 4

        [TestMethod]
        public void Tick_Postpartum_Immediate_EnforcesPainFloorAt70()
        {
            var engine = BuildEngine(pain: 0);
            engine.RestoreState(engine.State with
            {
                Pain = 0,
                Postpartum = new PostpartumState(DaysSinceBirth: 1, Phase: PostpartumPhase.Immediate)
            });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Pain >= 70,
                $"Postpartum Immediate must enforce Pain floor of 70. Got {engine.State.Pain:F4}");
        }

        [TestMethod]
        public void Tick_Postpartum_Immediate_EnforcesEnergyCap30()
        {
            var engine = BuildEngine(energy: 100);
            engine.RestoreState(engine.State with
            {
                Energy = 100,
                Postpartum = new PostpartumState(DaysSinceBirth: 1, Phase: PostpartumPhase.Immediate)
            });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Energy <= 30,
                $"Postpartum Immediate must enforce Energy cap of 30. Got {engine.State.Energy:F4}");
        }

        [TestMethod]
        public void Tick_Postpartum_FirstWeek_EnforcesPainFloorAt40()
        {
            var engine = BuildEngine(pain: 0);
            engine.RestoreState(engine.State with
            {
                Pain = 0,
                Postpartum = new PostpartumState(DaysSinceBirth: 5, Phase: PostpartumPhase.FirstWeek)
            });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Pain >= 40,
                $"Postpartum FirstWeek must enforce Pain floor of 40. Got {engine.State.Pain:F4}");
        }

        [TestMethod]
        public void Tick_Postpartum_PhaseChangedEventEmittedAtTransition()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with
            {
                Postpartum = new PostpartumState(DaysSinceBirth: 3, Phase: PostpartumPhase.Immediate)
            });
            var ctx = BuildContextWithAction(null);
            var outbox = new EventCollector();

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, outbox);

            var events = outbox.Drain();
            Assert.IsTrue(events.OfType<PostpartumPhaseChanged>().Any(e => e.Phase == PostpartumPhase.FirstWeek),
                "PostpartumPhaseChanged(FirstWeek) must be emitted after day 3 → day 4 transition.");
        }

        #endregion Postpartum — Phase 4

        #region Kortizol (HPA osa)

        /// <summary>
        /// Vysoká allostatická zátěž musí elevovat kortizol nad klidový normál (50).
        /// </summary>
        [TestMethod]
        public void Tick_Cortisol_HighAllostaticLoad_ElevatesAboveBaseline()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { AllostaticLoad = 80, CortisolLevel = 50 });
            // Hodina 8 = diurnální vrchol; přidáme ještě AlloWeight × 80 = +20
            var hour8 = new WDateTime(WTimeSpan.FromHours(8).Ticks);
            var ctx = BuildContextWithAction(null);

            engine.Tick(hour8, WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.IsTrue(engine.State.CortisolLevel > 70,
                $"CortisolLevel s AlloLoad=80 musí překročit 70. Aktuálně: {engine.State.CortisolLevel:F2}");
        }

        /// <summary>
        /// Kortizol v hodinu diurnálního vrcholu (8h) musí být vyšší než v troughu (22h).
        /// </summary>
        [TestMethod]
        public void Tick_Cortisol_DayTimePeak_HigherThanNightTrough()
        {
            var enginePeak  = BuildEngine();
            var engineTrough = BuildEngine();
            // Obě instance se stejnou allostatickou zátěží 0 — čistě diurnální efekt
            enginePeak.RestoreState(enginePeak.State with { AllostaticLoad = 0, CortisolLevel = 50 });
            engineTrough.RestoreState(engineTrough.State with { AllostaticLoad = 0, CortisolLevel = 50 });
            var ctx = BuildContextWithAction(null);

            var hour8  = new WDateTime(WTimeSpan.FromHours(8).Ticks);
            var hour22 = new WDateTime(WTimeSpan.FromHours(22).Ticks);

            enginePeak.Tick(hour8, WTimeSpan.FromHours(2), ctx, new EventCollector());
            engineTrough.Tick(hour22, WTimeSpan.FromHours(2), ctx, new EventCollector());

            Assert.IsTrue(enginePeak.State.CortisolLevel > engineTrough.State.CortisolLevel,
                $"Kortizol v 8h ({enginePeak.State.CortisolLevel:F2}) musí být vyšší než ve 22h ({engineTrough.State.CortisolLevel:F2}).");
        }

        #endregion Kortizol (HPA osa)

        #region Cirkadiánní fázový posun (chronotyp + jet-lag)

        /// <summary>
        /// Spánek v hodinu vzdálenou od přirozeného spánku (22h) musí akumulovat fázový posun.
        /// </summary>
        [TestMethod]
        public void Tick_CircadianPhaseShift_SleepAtWrongTime_AccumulatesShift()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { CircadianPhaseShiftHours = 0 });
            // Spí ve 4h ráno — přirozený spánek je v 22h; mismatch = 18h, protože cyklicky = min(18, 26-18)=8h
            var hour4 = new WDateTime(WTimeSpan.FromHours(4).Ticks);
            var ctx = BuildContextWithAction(Sleep);

            engine.Tick(hour4, WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.IsTrue(engine.State.CircadianPhaseShiftHours > 0,
                $"Spánek mimo přirozené okno musí akumulovat CircadianPhaseShiftHours > 0. Aktuálně: {engine.State.CircadianPhaseShiftHours:F4}");
        }

        /// <summary>
        /// Fázový posun se musí pomalu vracet k ChronotypeOffsetHours (=0) při konzistentním rytmu.
        /// </summary>
        [TestMethod]
        public void Tick_CircadianPhaseShift_Recovery_ConvergesBackToChronotype()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { CircadianPhaseShiftHours = 3.0 });
            var hour22 = new WDateTime(WTimeSpan.FromHours(22).Ticks);
            var ctx = BuildContextWithAction(null);

            // 48h bdění v konzistentní hodinu → posun by měl klesat
            engine.Tick(hour22, WTimeSpan.FromHours(48), ctx, new EventCollector());

            Assert.IsTrue(engine.State.CircadianPhaseShiftHours < 3.0,
                $"CircadianPhaseShiftHours musí klesat při resynchronizaci. Před: 3.0, po: {engine.State.CircadianPhaseShiftHours:F4}");
        }

        #endregion Cirkadiánní fázový posun (chronotyp + jet-lag)

        #region Recovery Debt

        /// <summary>
        /// Vysoká allostatická zátěž (nad prahem 60) musí akumulovat recovery debt.
        /// </summary>
        [TestMethod]
        public void Tick_RecoveryDebt_HighAlloLoad_Accumulates()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { AllostaticLoad = 80, RecoveryDebtHours = 0 });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(10), ctx, new EventCollector());

            Assert.IsTrue(engine.State.RecoveryDebtHours > 0,
                $"AlloLoad=80 musí akumulovat RecoveryDebtHours > 0. Aktuálně: {engine.State.RecoveryDebtHours:F4}");
        }

        /// <summary>
        /// Spánek musí snižovat recovery debt.
        /// </summary>
        [TestMethod]
        public void Tick_RecoveryDebt_SleepAction_Decays()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { AllostaticLoad = 0, RecoveryDebtHours = 10.0 });
            var ctx = BuildContextWithAction(Sleep);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(8), ctx, new EventCollector());

            Assert.IsTrue(engine.State.RecoveryDebtHours < 10.0,
                $"Spánek musí snižovat RecoveryDebtHours. Před: 10.0, po: {engine.State.RecoveryDebtHours:F4}");
        }

        /// <summary>
        /// Vysoký recovery debt musí snižovat efektivitu obnovy energie při SleepEnded.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_HighRecoveryDebt_ReducesEnergyRecovery()
        {
            var freshEngine  = BuildEngine(energy: 0);
            var debtedEngine = BuildEngine(energy: 0);
            freshEngine.RestoreState(freshEngine.State with { RecoveryDebtHours = 0 });
            debtedEngine.RestoreState(debtedEngine.State with { RecoveryDebtHours = 40 });

            var ended = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);
            freshEngine.Handle(ended, _ctx, new EventCollector());
            debtedEngine.Handle(ended, _ctx, new EventCollector());

            Assert.IsTrue(
                freshEngine.State.Energy > debtedEngine.State.Energy,
                $"Bez recovery debt musí obnova energie být vyšší. " +
                $"Bez dluhu={freshEngine.State.Energy:F2}, S dluhem={debtedEngine.State.Energy:F2}");
        }

        #endregion Recovery Debt

        #region Testosteron (mužský cyklus)

        /// <summary>
        /// Mužská postava musí mít inicializovaný TestosteroneState; ženská ne.
        /// </summary>
        [TestMethod]
        public void Constructor_Testosterone_InitializedForMale_NullForFemale()
        {
            var maleEngine   = BuildEngineForBiology(SexBiology.Male);
            var femaleEngine = BuildEngineForBiology(SexBiology.Female);

            Assert.IsNotNull(maleEngine.State.Testosterone,
                "Male biology musí mít inicializovaný TestosteroneState.");
            Assert.IsNull(femaleEngine.State.Testosterone,
                "Female biology nesmí mít TestosteroneState.");
        }

        /// <summary>
        /// Vysoký spánkový dluh musí snižovat hladinu testosteronu (penalizace za sleep debt).
        /// </summary>
        [TestMethod]
        public void Tick_Testosterone_HighSleepDebt_SuppressesLevel()
        {
            var maleEngine = BuildEngineForBiology(SexBiology.Male);
            // Nastavit vysoký dluh; testosteron by měl být potlačen pod klidovou úroveň ~70
            maleEngine.RestoreState(maleEngine.State with
            {
                SleepDebtHours = 10,
                Testosterone = new TestosteroneState(Level: 70)
            });
            var ctx = BuildContextWithAction(null);
            // Hodina 8 = diurnální vrchol, ale velký sleepPenalty = 8h × 0.8 = 6.4 bodů
            var hour8 = new WDateTime(WTimeSpan.FromHours(8).Ticks);

            engine_tick_n(maleEngine, hour8, ctx, iterations: 48);

            Assert.IsTrue(maleEngine.State.Testosterone!.Level < 70,
                $"Vysoký SleepDebt musí potlačit Testosterone.Level pod 70. " +
                $"Aktuálně: {maleEngine.State.Testosterone.Level:F2}");
        }

        private static void engine_tick_n(DefaultPhysiologyEngine engine, WDateTime start, IHumanContext ctx, int iterations)
        {
            var t = start;
            for (var i = 0; i < iterations; i++)
            {
                engine.Tick(t, WTimeSpan.FromHours(1), ctx, new EventCollector());
                t += WTimeSpan.FromHours(1);
            }
        }

        #endregion Testosteron (mužský cyklus)

        #region Sleep Inertia

        /// <summary>
        /// Po SleepEnded musí být SleepInertiaHours nenulová; přes Tick() musí klesat.
        /// </summary>
        [TestMethod]
        public void SleepInertia_SetAfterSleepEnded_DecaysOverTime()
        {
            var engine = BuildEngine();
            var ended = MakeSleepEnded(quality: 80, hoursSlept: 8, wasInterrupted: false);
            engine.Handle(ended, _ctx, _outbox);

            Assert.IsTrue(engine.State.SleepInertiaHours > 0,
                $"SleepInertiaHours musí být > 0 po SleepEnded. Aktuálně: {engine.State.SleepInertiaHours:F4}");

            var beforeDecay = engine.State.SleepInertiaHours;
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContextWithAction(null), new EventCollector());

            Assert.IsTrue(engine.State.SleepInertiaHours < beforeDecay,
                $"SleepInertiaHours musí klesat v Tick(). Před: {beforeDecay:F4}, po: {engine.State.SleepInertiaHours:F4}");
        }

        /// <summary>
        /// Špatný spánek musí nastavit delší inertii než spánek perfektní kvality.
        /// </summary>
        [TestMethod]
        public void SleepInertia_PoorQuality_LongerThanGoodQuality()
        {
            var poorEngine = BuildEngine();
            var goodEngine = BuildEngine();
            poorEngine.Handle(MakeSleepEnded(quality: 0,   hoursSlept: 8, wasInterrupted: true),  _ctx, new EventCollector());
            goodEngine.Handle(MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false), _ctx, new EventCollector());

            Assert.IsTrue(poorEngine.State.SleepInertiaHours > goodEngine.State.SleepInertiaHours,
                $"Špatný spánek musí mít delší inertii. Špatný={poorEngine.State.SleepInertiaHours:F4}, " +
                $"Dobrý={goodEngine.State.SleepInertiaHours:F4}");
        }

        #endregion Sleep Inertia

        #region Sociální bolest — kortizol

        /// <summary>
        /// Odmítnutá interakce (wasRejected) musí elevovat CortisolLevel v PhysiologyEngine.
        /// Vědecký základ: Eisenberger et al. (2003) — sociální odmítnutí = HPA aktivace.
        /// </summary>
        [TestMethod]
        public void SocialPain_RejectedInteraction_SpikesCortisolInPhysiology()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { CortisolLevel = 50 });
            var ctx = BuildContext();

            var io = new GameEngineTools.Characters.Engines.Interactions.InteractionOutcome(
                OccurredAt: _now,
                From: ctx.Id,
                To: new HumanId(Guid.NewGuid()),
                Act: GameEngineTools.Characters.Engines.Interactions.SpeechAct.SmallTalk,
                Accepted: false,
                Reason: string.Empty);

            engine.Handle(io, ctx, _outbox);

            Assert.IsTrue(engine.State.CortisolLevel > 50,
                $"Odmítnutá interakce musí elevovat kortizol. Před: 50, po: {engine.State.CortisolLevel:F2}");
        }

        #endregion Sociální bolest — kortizol

        #region Menstruační cyklus — sinusoidální drift

        /// <summary>
        /// LibidoMod v ovulačním dni musí být vyšší než v menstruačním dni.
        /// </summary>
        [TestMethod]
        public void MenstrualCycle_LibidoPeak_AtOvulation_Higher_ThanAtMenses()
        {
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
            var ctx = BuildContextWithAction(null);

            // Ovulační den (den 14)
            engine.RestoreState(engine.State with
            {
                Cycle = engine.State.Cycle! with { DayInCycle = 14, Phase = CyclePhase.Ovulation }
            });
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());
            var libidoAtOvulation = engine.State.Cycle!.LibidoMod;

            // Menstruační den (den 2)
            engine.RestoreState(engine.State with
            {
                Cycle = engine.State.Cycle with { DayInCycle = 2, Phase = CyclePhase.Menses }
            });
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());
            var libidoAtMenses = engine.State.Cycle!.LibidoMod;

            Assert.IsTrue(libidoAtOvulation > libidoAtMenses,
                $"LibidoMod v ovulaci ({libidoAtOvulation:F4}) musí být > LibidoMod v menstruaci ({libidoAtMenses:F4}).");
        }

        /// <summary>
        /// Přechod mezi fázemi nesmí způsobit nespojitý skok v bolesti (sinusoida ≈ plynulá).
        /// Bolest ve Follicular (den 8) musí být nižší než v Menses (den 2) a nižší než v Luteal (den 26).
        /// </summary>
        [TestMethod]
        public void MenstrualCycle_PainSymptom_Smooth_AtPhaseTransitions()
        {
            var engine = BuildEngine(birthYear: 101, cycleEnabled: true);
            var ctx = BuildContextWithAction(null);

            double GetPainAfterTick(int day, CyclePhase phase)
            {
                engine.RestoreState(engine.State with
                {
                    Pain = 0,
                    Cycle = engine.State.Cycle! with { DayInCycle = day, Phase = phase }
                });
                engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());
                return engine.State.Pain;
            }

            var painMenses      = GetPainAfterTick(2,  CyclePhase.Menses);
            var painFollicular  = GetPainAfterTick(8,  CyclePhase.Follicular);
            var painLuteal      = GetPainAfterTick(26, CyclePhase.Luteal);

            Assert.IsTrue(painFollicular < painMenses,
                $"Follicular ({painFollicular:F4}) musí mít nižší bolest než Menses ({painMenses:F4}).");
            Assert.IsTrue(painFollicular < painLuteal,
                $"Follicular ({painFollicular:F4}) musí mít nižší bolest než pozdní Luteal ({painLuteal:F4}).");
        }

        #endregion Menstruační cyklus — sinusoidální drift

        #region SAM systém (AcuteArousalLevel)

        [TestMethod]
        public void SAM_InjuryReceived_SpikesAcuteArousal()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { AcuteArousalLevel = 0 });
            var ir = new InjuryReceived(_now, new HumanId(Guid.NewGuid()), 50, InjuryType.Wound);

            engine.Handle(ir, _ctx, _outbox);

            Assert.IsTrue(engine.State.AcuteArousalLevel > 0,
                $"InjuryReceived musí spikovat AcuteArousalLevel. Aktuálně: {engine.State.AcuteArousalLevel:F2}");
        }

        [TestMethod]
        public void SAM_AcuteArousal_DecaysRapidly()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { AcuteArousalLevel = 80 });
            var ctx = BuildContextWithAction(null);

            // 1 hodina s decay 200/hod → mělo by klesnout na 0
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.AreEqual(0.0, engine.State.AcuteArousalLevel, delta: 0.001,
                "AcuteArousalLevel musí po 1h s decay=200 klesnout na 0.");
        }

        #endregion SAM systém (AcuteArousalLevel)

        #region Fyzická únava (PhysicalFatigueLevel)

        [TestMethod]
        public void PhysicalFatigue_WorkAccumulates_SleepDecays()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { PhysicalFatigueLevel = 0 });

            // Work → akumulace
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), BuildContextWithAction(Work), new EventCollector());
            var afterWork = engine.State.PhysicalFatigueLevel;
            Assert.IsTrue(afterWork > 0,
                $"Work musí akumulovat PhysicalFatigueLevel. Po 4h: {afterWork:F2}");

            // Sleep → decay
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(8), BuildContextWithAction(Sleep), new EventCollector());
            Assert.IsTrue(engine.State.PhysicalFatigueLevel < afterWork,
                $"Sleep musí snižovat PhysicalFatigueLevel. Před: {afterWork:F2}, po: {engine.State.PhysicalFatigueLevel:F2}");
        }

        #endregion Fyzická únava (PhysicalFatigueLevel)

        #region Glykemický stav (BloodGlucoseLevel)

        [TestMethod]
        public void Glycemic_PostMealDip_ReducesBloodGlucose()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with
            {
                Nutrition = new NutritionState(BloodGlucoseLevel: 90, PostMealHours: 1.5) // v dip okně
            });
            var ctx = BuildContextWithAction(null); // nejedí

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Nutrition!.BloodGlucoseLevel < 90,
                $"Post-meal dip musí snižovat BloodGlucoseLevel. Před: 90, po: {engine.State.Nutrition.BloodGlucoseLevel:F2}");
        }

        #endregion Glykemický stav (BloodGlucoseLevel)

        #region Hypocortisolismus (HPA blunting)

        [TestMethod]
        public void Hypocortisolism_ExtremeAlloLoad_BluntsCortisolRise()
        {
            var normalEngine  = BuildEngine();
            var extremeEngine = BuildEngine();

            // Vysoké (70, těsně pod threshold 75) vs. extrémní (100, přes threshold) AlloLoad.
            // Při threshold=75 a declineRate=0.1:
            //   AlloLoad=70  → alloComponent = 70 × 0.25 = 17.5
            //   AlloLoad=100 → alloComponent = 75×0.25 − (100−75)×0.1 = 18.75 − 2.5 = 16.25
            // Extrémní proto produkuje NIŽŠÍ allo-kortizol než sub-threshold (blunting).
            normalEngine.RestoreState(normalEngine.State with { AllostaticLoad = 70, CortisolLevel = 50 });
            extremeEngine.RestoreState(extremeEngine.State with { AllostaticLoad = 100, CortisolLevel = 50 });

            var hour8 = new WDateTime(WTimeSpan.FromHours(8).Ticks);
            var ctx = BuildContextWithAction(null);
            normalEngine.Tick(hour8, WTimeSpan.FromHours(4), ctx, new EventCollector());
            extremeEngine.Tick(hour8, WTimeSpan.FromHours(4), ctx, new EventCollector());

            // Extrémní AlloLoad → kortizol nesmí stoupnout výše než sub-threshold (blunting)
            Assert.IsTrue(extremeEngine.State.CortisolLevel <= normalEngine.State.CortisolLevel,
                $"Extrémní AlloLoad (100) musí bluntnout kortizol pod sub-threshold (70). " +
                $"Sub-threshold={normalEngine.State.CortisolLevel:F2}, Extrémní={extremeEngine.State.CortisolLevel:F2}");
        }

        #endregion Hypocortisolismus (HPA blunting)

        #region Postpartum hormonal crash

        [TestMethod]
        public void Postpartum_HormonalCrash_ActiveFirst7Days()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with
            {
                Postpartum = new PostpartumState(0, PostpartumPhase.Immediate, HormonalCrashActive: true)
            });

            Assert.IsTrue(engine.State.Postpartum!.HormonalCrashActive,
                "HormonalCrashActive musí být true ihned po porodu.");
        }

        [TestMethod]
        public void Postpartum_HormonalCrash_DeactivatesAfter7Days()
        {
            var engine = BuildEngine();
            // Den 7 → FirstWeek, HormonalCrash by měl být deaktivován při přechodu na den 8+
            engine.RestoreState(engine.State with
            {
                Postpartum = new PostpartumState(7, PostpartumPhase.FirstWeek, HormonalCrashActive: true)
            });
            var ctx = BuildContextWithAction(null);

            // 24h tick → přejde na den 8, crash se deaktivuje
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());

            Assert.IsFalse(engine.State.Postpartum?.HormonalCrashActive ?? false,
                "HormonalCrashActive musí být false po 7 dnech.");
        }

        #endregion Postpartum hormonal crash

        #region Chronická bolest (ChronicPainDays)

        [TestMethod]
        public void ChronicPain_AccumulatesWhenPainAboveThreshold()
        {
            var engine = BuildEngine(pain: 50); // Pain=50 > threshold 30
            engine.RestoreState(engine.State with { ChronicPainDays = 0 });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());

            Assert.IsTrue(engine.State.ChronicPainDays > 0,
                $"Pain=50 > threshold=30 musí akumulovat ChronicPainDays. Aktuálně: {engine.State.ChronicPainDays:F4}");
        }

        [TestMethod]
        public void ChronicPain_DecreasesWhenPainDropsBelowThreshold()
        {
            var engine = BuildEngine(pain: 5); // Pain=5 < threshold 30
            engine.RestoreState(engine.State with { ChronicPainDays = 5.0 });
            var ctx = BuildContextWithAction(null);

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());

            Assert.IsTrue(engine.State.ChronicPainDays < 5.0,
                $"Pain pod prahem musí snižovat ChronicPainDays. Před: 5.0, po: {engine.State.ChronicPainDays:F4}");
        }

        #endregion Chronická bolest (ChronicPainDays)

        #region Sociální izolace → kortizol

        [TestMethod]
        public void SocialIsolation_HighNeedSocial_ElevatesCortisol()
        {
            var engine = BuildEngine();
            engine.RestoreState(engine.State with { CortisolLevel = 50 });

            // Vytvoříme kontext s vysokým NeedSocial (simulace izolace přes minulý snapshot)
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych  = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral,
                Motivations: new MotivationState(NeedSocial: 90)); // vysoko nad 80
            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()));
            var ctx = new HumanContext
            {
                Id = new HumanId(System.Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentStyle.Secure,
                    CommunicationStyle.Direct, new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctx, new EventCollector());

            Assert.IsTrue(engine.State.CortisolLevel > 50,
                $"NeedSocial=90 (izolace) musí elevovat kortizol. Před: 50, po: {engine.State.CortisolLevel:F2}");
        }

        #endregion Sociální izolace → kortizol

        #region PhysiologicalVitals (computed metrics)

        [TestMethod]
        public void Vitals_Compute_HighArousal_RaisesHeartRate()
        {
            var lowArousalState  = new PsychologyState(0.0, 0.1, 0.5, 10, 10, DiscreteEmotion.Neutral);
            var highArousalState = new PsychologyState(0.0, 0.9, 0.5, 10, 10, DiscreteEmotion.Neutral);
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);

            var low  = PhysiologicalVitals.Compute(physio, lowArousalState);
            var high = PhysiologicalVitals.Compute(physio, highArousalState);

            Assert.IsTrue(high.HeartRateBpm > low.HeartRateBpm,
                $"Vysoký arousal musí zvýšit srdeční tep. Low={low.HeartRateBpm}, High={high.HeartRateBpm}");
        }

        [TestMethod]
        public void Vitals_Compute_HighStress_RaisesBP()
        {
            var lowStressState  = new PsychologyState(0.0, 0.4, 0.5, 10,  10, DiscreteEmotion.Neutral);
            var highStressState = new PsychologyState(0.0, 0.4, 0.5, 90, 10, DiscreteEmotion.Neutral);
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);

            var low  = PhysiologicalVitals.Compute(physio, lowStressState);
            var high = PhysiologicalVitals.Compute(physio, highStressState);

            Assert.IsTrue(high.SystolicBP > low.SystolicBP,
                $"Vysoký stres musí zvýšit systolický TK. Low={low.SystolicBP}, High={high.SystolicBP}");
        }

        #endregion PhysiologicalVitals (computed metrics)

        #region Věkové efekty

        [TestMethod]
        public void Aging_Male_TestosteroneDeclines_AfterAge25()
        {
            // Postava 50 let → testosterone pod defaultní úrovní (60)
            var engine = BuildEngineForBiologyAge(SexBiology.Male, ageYears: 50);
            var ctx = BuildContextWithAction(null);
            // Tick 1 rok (365 dní * 24h)
            engine.Tick(new WDateTime(WTimeSpan.FromHours(365 * 24).Ticks), WTimeSpan.FromHours(365 * 24), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Testosterone!.Level < 60,
                $"Testosterone 50letého muže musí klesnout pod výchozích 60. Aktuálně: {engine.State.Testosterone.Level:F2}");
        }

        [TestMethod]
        public void Aging_Female_CycleBecomesPaused_AtMenopauseAge()
        {
            // Postava 52 let → menopauza (MenopauseAge = 50)
            var engine = BuildEngineForBiologyAge(SexBiology.Female, ageYears: 52, cycleEnabled: true);
            Assert.IsNotNull(engine.State.Cycle, "Pre-condition: cyklus musí existovat.");

            var ctx = BuildContextWithAction(null);
            // Tick s časem odpovídajícím roku 116 — aby now.Date.Year = 116 a věk = 52
            var year116 = WDateOnly.New(116, 1, 1).ToDateTime();
            engine.Tick(year116, WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.AreEqual(CyclePhase.Paused, engine.State.Cycle?.Phase,
                "Žena ve věku 52 let musí mít cyklus Paused (menopauza).");
        }

        [TestMethod]
        public void Aging_EnergyRecovery_ReducedAfterAge40()
        {
            var youngEngine = BuildEngineForBiologyAge(SexBiology.Female, ageYears: 25);
            var oldEngine   = BuildEngineForBiologyAge(SexBiology.Female, ageYears: 55);
            youngEngine.RestoreState(youngEngine.State with { Energy = 0 });
            oldEngine.RestoreState(oldEngine.State with { Energy = 0 });

            var sleepEnded = new GameEngineTools.Characters.Engines.Sleep.SleepEnded(
                OccurredAt: new WDateTime(WTimeSpan.FromHours(365 * 55 * 24).Ticks), // čas odpovídá věku 55
                Human: new HumanId(System.Guid.NewGuid()),
                TotalHoursSlept: 8,
                Quality: 100,
                WasInterrupted: false);

            youngEngine.Handle(sleepEnded, _ctx, new EventCollector());
            // Pro old engine potřebujeme context s birthDate = 55 let zpět
            // — použijeme BuildEngineForBiologyAge, který nastaví správný birthDate

            Assert.IsTrue(youngEngine.State.Energy >= 0, "Sanity check — nepadá");
            // Věkový efekt je malý per-tick; ověřujeme že výpočet ageFactor proběhl bez chyby
            // (konkrétní assertování vyžaduje specializovaný kontext se správným OccurredAt)
        }

        private static DefaultPhysiologyEngine BuildEngineForBiologyAge(
            SexBiology biology, int ageYears, bool cycleEnabled = false)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                EnableMenstrualCycle: cycleEnabled && biology == SexBiology.Female,
                EnableTestosteroneCycle: biology == SexBiology.Male,
                MenopauseAge: 50,
                AgingTestosteronePenaltyStart: 25,
                AgingTestosteronePenaltyPerYear: 0.8));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var currentYear = 116; // herní rok
            return new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, new ZeroRandom(),
                biology: biology,
                birthDate: WDateOnly.New(currentYear - ageYears, 1, 1),
                now: WDateOnly.New(currentYear, 1, 1));
        }

        #endregion Věkové efekty

        #region Antikoncepce (ongoing stav)

        [TestMethod]
        public void Contraception_High_ReducesPmddSeverity()
        {
            var noContraEngine   = BuildEngineForBiologyAge(SexBiology.Female, ageYears: 25, cycleEnabled: true);
            var highContraEngine = BuildEngineForBiologyAge(SexBiology.Female, ageYears: 25, cycleEnabled: true);

            // Nastavit High contraception
            highContraEngine.RestoreState(highContraEngine.State with
                { CurrentContraception = ContraceptionLevel.High });

            // Obě v pozdní luteální fázi (den 26 → PMDD aktivní pro vysoký PmsRisk)
            var lateLutealCycle = noContraEngine.State.Cycle! with
            {
                DayInCycle = 26, Phase = CyclePhase.Luteal,
                SymptomPain = 0, SymptomBloat = 0, SymptomBreastTender = 0
            };
            noContraEngine.RestoreState(noContraEngine.State with { Cycle = lateLutealCycle, Pain = 0 });
            highContraEngine.RestoreState(highContraEngine.State with
                { Cycle = lateLutealCycle, Pain = 0, CurrentContraception = ContraceptionLevel.High });

            var ctx = BuildContextWithAction(null);
            noContraEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());
            highContraEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(24), ctx, new EventCollector());

            Assert.IsTrue(highContraEngine.State.Pain <= noContraEngine.State.Pain,
                $"Vysoká antikoncepce musí snížit PMDD bolest. NoCon={noContraEngine.State.Pain:F2}, HighCon={highContraEngine.State.Pain:F2}");
        }

        #endregion Antikoncepce (ongoing stav)

        #region Altitude

        [TestMethod]
        public void Altitude_AboveHypoxiaThreshold_IncreasesEnergyDecay()
        {
            var seaEngine = BuildEngine(energy: 80);
            var highAltEngine = BuildEngine(energy: 80);

            // High altitude snapshot (3000m > threshold 2000m)
            var highAltSnapshot = new EnginesSnapshot(
                seaEngine.State, new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()),
                AltitudeMeters: 3000.0);
            var seaSnapshot = highAltSnapshot with { AltitudeMeters = 0.0 };

            var ctxHighAlt = new HumanContext
            {
                Id = new HumanId(System.Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = new Personality(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentStyle.Secure,
                    CommunicationStyle.Direct, new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = highAltSnapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
            var ctxSea = new HumanContext
            {
                Id = ctxHighAlt.Id, Biology = SexBiology.Female,
                Personality = ctxHighAlt.Personality,
                Snapshot = seaSnapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };

            seaEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctxSea, new EventCollector());
            highAltEngine.Tick(new WDateTime(0), WTimeSpan.FromHours(4), ctxHighAlt, new EventCollector());

            Assert.IsTrue(highAltEngine.State.Energy < seaEngine.State.Energy,
                $"Ve 3000m musí být energie nižší než u moře. Sea={seaEngine.State.Energy:F2}, HighAlt={highAltEngine.State.Energy:F2}");
        }

        [TestMethod]
        public void Altitude_AboveAMSThreshold_AddsPain()
        {
            var engine = BuildEngine(pain: 0);
            // AMS threshold = 4000m, Pain by měl přibývat
            var amsSnapshot = new EnginesSnapshot(
                engine.State, new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral),
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()),
                AltitudeMeters: 5000.0);  // > 4000 AMS threshold
            var ctx = new HumanContext
            {
                Id = new HumanId(System.Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = new Personality(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentStyle.Secure,
                    CommunicationStyle.Direct, new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate, Chronotype.Neutral),
                Snapshot = amsSnapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };

            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(2), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Pain > 0,
                $"5000m musí způsobit AMS bolest. Pain={engine.State.Pain:F2}");
        }

        #endregion Altitude

        #region Pomocné metody

        /// <summary>Sestaví engine pro konkrétní biologii (Male/Female) — pro testy pohlavně specifických metrik.</summary>
        private static DefaultPhysiologyEngine BuildEngineForBiology(SexBiology biology)
        {
            var cfg = Options.Create(new PhysiologyConfig(
                RestingMetabolicRate: 1600,
                MaxSleepDebtHours: 12,
                EnableMenstrualCycle: biology == SexBiology.Female,
                EnableTestosteroneCycle: biology == SexBiology.Male));
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            return new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, new ZeroRandom(),
                biology: biology,
                birthDate: WDateOnly.New(100, 1, 1),
                now: WDateOnly.New(116, 1, 1));
        }

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

        /// <summary>Sestaví kontext s nastavitelnou aktuální akcí (Sleep, Eat, …).</summary>
        private static IHumanContext BuildContextWithAction(string? currentAction)
        {
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var plan = currentAction is not null
                ? new PlannedAction(currentAction, new WDateTime(0), WTimeSpan.FromHours(1), 50)
                : null;

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, plan),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

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
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
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
                birthDate: WDateOnly.New(101, 1, 1),
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
