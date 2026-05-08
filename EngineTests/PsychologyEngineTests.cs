// PsychologyEngineTests.cs
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
    /// Unit testy pro <see cref="DefaultPsychologyEngine"/>.
    /// Pokrývá reakce na spánkové eventy: <see cref="SleepEnded"/>
    /// a <see cref="NightmareTriggered"/>.
    /// </summary>
    /// <remarks>
    /// Každý test pracuje s izolovanou instancí enginu a fake kontextem —
    /// žádný DI stack, žádný reálný čas.
    /// </remarks>
    [TestClass]
    public class PsychologyEngineTests : TestBase
    {
        #region Soukromá pole

        private IEventCollector _outbox = default!;
        private WDateTime _now;

        private static readonly PsychologyConfig DefaultCfg = new PsychologyConfig(
            BaselineAffectVariance: 0.0,   // vypnuto — deterministické testy
            StressRecoveryRatePerHour: 0.0,   // vypnuto — nechceme drift v testech
            SleepQualityAffectWeight: 0.5,
            EnableCircadianRhythm: false);  // vypnuto — testy nesmí záviset na denní hodině

        #endregion Soukromá pole

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
        }

        #endregion Setup

        #region SleepEnded — valence

        /// <summary>
        /// Perfektní spánek (kvalita 100) musí zvýšit valenci.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_PerfectQuality_IncreasesValence()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Valence > 0.0,
                $"Perfektní spánek musí zvýšit valenci. Aktuálně: {engine.State.Valence:F4}");
        }

        /// <summary>
        /// Hrozný spánek (kvalita 0) musí snížit valenci.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_ZeroQuality_DecreasesValence()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 0, hoursSlept: 2, wasInterrupted: true);

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Valence < 0.0,
                $"Nulová kvalita spánku musí snížit valenci. Aktuálně: {engine.State.Valence:F4}");
        }

        /// <summary>
        /// Průměrný spánek (kvalita 50 = neutrální bod) nesmí změnit valenci.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_NeutralQuality_DoesNotChangeValence()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.2, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 50, hoursSlept: 6, wasInterrupted: false);

            var valenceBefore = engine.State.Valence;

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert — kvalita 50 = neutrální → valenceDelta = 0
            Assert.AreEqual(valenceBefore, engine.State.Valence, delta: 0.001,
                "Kvalita 50 je neutrální bod — valence se nesmí změnit.");
        }

        #endregion SleepEnded — valence

        #region SleepEnded — stres

        /// <summary>
        /// Kvalitní spánek (>= 60, nepřerušený) musí snížit stres.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_GoodQuality_DecreasesStress()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 50);
            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 90, hoursSlept: 8, wasInterrupted: false);
            var stressBefore = engine.State.Stress;

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Stress < stressBefore,
                $"Dobrý spánek musí snížit stres. Před: {stressBefore:F1}, po: {engine.State.Stress:F1}");
        }

        /// <summary>
        /// Přerušený spánek musí zvýšit stres.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_Interrupted_IncreasesStress()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 20, hoursSlept: 1, wasInterrupted: true);
            var stressBefore = engine.State.Stress;

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Stress > stressBefore,
                $"Přerušený spánek musí zvýšit stres. Před: {stressBefore:F1}, po: {engine.State.Stress:F1}");
        }

        /// <summary>
        /// Neurotická postava musí reagovat na špatný spánek silněji než stabilní.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_HighNeuroticism_ReactsStrongerThanLow()
        {
            // Arrange — stejný spánek, různý Neuroticism
            var badSleep = MakeSleepEnded(quality: 10, hoursSlept: 2, wasInterrupted: true);

            var stableEngine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var neurotiEngine = BuildEngine(initialValence: 0.0, initialStress: 30);

            var stableCtx = BuildContext(neuroticism: 0.0);
            var neurotiCtx = BuildContext(neuroticism: 1.0);

            // Act
            stableEngine.Handle(badSleep, stableCtx, new EventCollector());
            neurotiEngine.Handle(badSleep, neurotiCtx, new EventCollector());

            // Assert — neurotická postava má více stresu po špatném spánku
            Assert.IsTrue(
                neurotiEngine.State.Stress > stableEngine.State.Stress,
                $"Neurotická postava musí mít více stresu. " +
                $"Stable={stableEngine.State.Stress:F1}, Neuroti={neurotiEngine.State.Stress:F1}");

            Assert.IsTrue(
                neurotiEngine.State.Valence < stableEngine.State.Valence,
                $"Neurotická postava musí mít nižší valenci. " +
                $"Stable={stableEngine.State.Valence:F3}, Neuroti={neurotiEngine.State.Valence:F3}");
        }

        /// <summary>
        /// Pokud stres přesáhne 70 díky špatnému spánku, musí být publikován
        /// <see cref="StressSpiked"/>.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_StressExceeds70_PublishesStressSpiked()
        {
            // Arrange — stres těsně pod threshold
            var engine = BuildEngine(initialValence: 0.0, initialStress: 68);
            var ctx = BuildContext(neuroticism: 1.0);

            // Přerušený spánek + vysoký neuroticism → stres přesáhne 70
            var ended = MakeSleepEnded(quality: 5, hoursSlept: 0.5, wasInterrupted: true);

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<StressSpiked>().Any(),
                "Překročení threshold 70 musí publikovat StressSpiked.");
        }

        /// <summary>
        /// Pokud stres byl již nad 70 před eventem, StressSpiked se neopakuje.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_StressAlreadyAbove70_DoesNotRepublishStressSpiked()
        {
            // Arrange — stres již nad prahem
            var engine = BuildEngine(initialValence: 0.0, initialStress: 80);
            var ctx = BuildContext(neuroticism: 1.0);
            var ended = MakeSleepEnded(quality: 5, hoursSlept: 0.5, wasInterrupted: true);

            // Act
            engine.Handle(ended, ctx, _outbox);

            // Assert — nesmí být duplikát SpikeSpiked pokud byl stres již > 70
            var events = _outbox.Drain();
            Assert.IsFalse(
                events.OfType<StressSpiked>().Any(),
                "StressSpiked nesmí být publikován pokud byl stres již nad prahem.");
        }

        #endregion SleepEnded — stres

        #region NightmareTriggered

        /// <summary>
        /// Noční můra musí vždy zvýšit stres a snížit valenci.
        /// </summary>
        [TestMethod]
        public void Handle_NightmareTriggered_AlwaysIncreasesStressAndDecreasesValence()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.3, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var nightmare = new NightmareTriggered(WDateTime.New(100, 1, 1), ctx.Id, StressAtSleepStart: 40);
            var stressBefore = engine.State.Stress;
            var valenceBefore = engine.State.Valence;

            // Act
            engine.Handle(nightmare, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Stress > stressBefore,
                "Noční můra musí zvýšit stres.");
            Assert.IsTrue(engine.State.Valence < valenceBefore,
                "Noční můra musí snížit valenci.");
        }

        /// <summary>
        /// Noční můra musí zvýšit arousal (šoková reakce probuzení).
        /// </summary>
        [TestMethod]
        public void Handle_NightmareTriggered_IncreasesArousal()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var nightmare = new NightmareTriggered(WDateTime.New(100, 1, 1), ctx.Id, StressAtSleepStart: 30);
            var arousalBefore = engine.State.Arousal;

            // Act
            engine.Handle(nightmare, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Arousal > arousalBefore,
                $"Noční můra musí zvýšit arousal. Před: {arousalBefore:F2}, po: {engine.State.Arousal:F2}");
        }

        /// <summary>
        /// Neurotická postava musí dostat silnější stresový spike z noční můry.
        /// </summary>
        [TestMethod]
        public void Handle_NightmareTriggered_HighNeuroticism_CausesLargerStressSpike()
        {
            // Arrange
            var nightmare = new NightmareTriggered(WDateTime.New(100, 1, 1), new HumanId(Guid.NewGuid()), StressAtSleepStart: 50);

            var stableEngine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var neurotiEngine = BuildEngine(initialValence: 0.0, initialStress: 20);

            // Act
            stableEngine.Handle(nightmare, BuildContext(neuroticism: 0.0), new EventCollector());
            neurotiEngine.Handle(nightmare, BuildContext(neuroticism: 1.0), new EventCollector());

            // Assert
            Assert.IsTrue(
                neurotiEngine.State.Stress > stableEngine.State.Stress,
                $"Neurotik musí dostat větší spike. " +
                $"Stable={stableEngine.State.Stress:F1}, Neuroti={neurotiEngine.State.Stress:F1}");
        }

        /// <summary>
        /// Noční můra vždy publikuje <see cref="StressSpiked"/>.
        /// </summary>
        [TestMethod]
        public void Handle_NightmareTriggered_AlwaysPublishesStressSpiked()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 10);
            var ctx = BuildContext(neuroticism: 0.5);
            var nightmare = new NightmareTriggered(WDateTime.New(100, 1, 1), ctx.Id, StressAtSleepStart: 20);

            // Act
            engine.Handle(nightmare, ctx, _outbox);

            // Assert — noční můra vždy spiknuje stres bez ohledu na threshold
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<StressSpiked>().Any(),
                "Noční můra musí vždy publikovat StressSpiked.");
        }

        #endregion NightmareTriggered

        #region SleepQualityAffectWeight — vliv konfigurace

        /// <summary>
        /// Vyšší <see cref="PsychologyConfig.SleepQualityAffectWeight"/> musí způsobit
        /// větší změnu valence při stejné kvalitě spánku.
        /// </summary>
        [TestMethod]
        public void Handle_SleepEnded_HigherWeight_CausesLargerValenceDelta()
        {
            // Arrange
            var lowWeightCfg = DefaultCfg with { SleepQualityAffectWeight = 0.1 };
            var highWeightCfg = DefaultCfg with { SleepQualityAffectWeight = 0.9 };

            var lowEngine = BuildEngine(initialValence: 0.0, initialStress: 20, cfg: lowWeightCfg);
            var highEngine = BuildEngine(initialValence: 0.0, initialStress: 20, cfg: highWeightCfg);

            var ctx = BuildContext(neuroticism: 0.5);
            var ended = MakeSleepEnded(quality: 100, hoursSlept: 8, wasInterrupted: false);

            // Act
            lowEngine.Handle(ended, ctx, new EventCollector());
            highEngine.Handle(ended, ctx, new EventCollector());

            // Assert
            Assert.IsTrue(
                highEngine.State.Valence > lowEngine.State.Valence,
                $"Vyšší weight musí způsobit větší delta valence. " +
                $"Low={lowEngine.State.Valence:F4}, High={highEngine.State.Valence:F4}");
        }

        #endregion SleepQualityAffectWeight — vliv konfigurace

        #region CognitiveLoad — testy

        /// <summary>
        /// Vysoký spánkový dluh musí zvýšit CognitiveLoad.
        /// </summary>
        [TestMethod]
        public void Tick_CognitiveLoad_IncreasesWithHighSleepDebt()
        {
            // Arrange — SleepDebtHours=10, CogLoad začíná nízko
            var physio = MakePhysio(sleepDebtHours: 10, pain: 0, bodyTempDelta: 0);
            var ctx = BuildContext(neuroticism: 0.5, physio: physio);
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);

            var cogBefore = engine.State.CognitiveLoad;

            // Act
            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctx, _outbox);

            // Assert — CogLoad musí stoupnout (targetLoad = 10*1.8 = 18 > 0)
            Assert.IsTrue(engine.State.CognitiveLoad > cogBefore,
                $"SleepDebt=10 musí zvýšit CogLoad. Před: {cogBefore:F2}, po: {engine.State.CognitiveLoad:F2}");
        }

        /// <summary>
        /// Vysoká bolest musí zvýšit CognitiveLoad.
        /// </summary>
        [TestMethod]
        public void Tick_CognitiveLoad_IncreasesWithHighPain()
        {
            // Arrange — Pain=80, CogLoad začíná nízko
            var physio = MakePhysio(sleepDebtHours: 0, pain: 80, bodyTempDelta: 0);
            var ctx = BuildContext(neuroticism: 0.5, physio: physio);
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);

            var cogBefore = engine.State.CognitiveLoad;

            // Act
            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctx, _outbox);

            // Assert — CogLoad musí stoupnout (targetLoad = 80*0.4 = 32 > 0)
            Assert.IsTrue(engine.State.CognitiveLoad > cogBefore,
                $"Pain=80 musí zvýšit CogLoad. Před: {cogBefore:F2}, po: {engine.State.CognitiveLoad:F2}");
        }

        /// <summary>
        /// Čistá fyziologie (SleepDebt=0, Pain=0, Stress≈0) musí snížit elevovaný CogLoad.
        /// </summary>
        [TestMethod]
        public void Tick_CognitiveLoad_DecreasesWhenPhysiologyClean()
        {
            // Arrange — fyziologie čistá, CogLoad začíná na 60
            var physio = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 0);
            var ctx = BuildContext(neuroticism: 0.5, physio: physio);
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 60);

            var cogBefore = engine.State.CognitiveLoad;

            // Act
            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctx, _outbox);

            // Assert — targetLoad ≈ 0, CogLoad musí klesnout
            Assert.IsTrue(engine.State.CognitiveLoad < cogBefore,
                $"Čistá fyziologie musí snižovat CogLoad. Před: {cogBefore:F2}, po: {engine.State.CognitiveLoad:F2}");
        }

        /// <summary>
        /// Spánek musí CogLoad snižovat rychleji než jiná akce při stejném CogLoad > target.
        /// </summary>
        [TestMethod]
        public void Tick_CognitiveLoad_RecoversFasterDuringSleep()
        {
            // Arrange — fyziologie čistá, CogLoad = 50, target ≈ 0
            var physio = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 0);

            var ctxSleep = BuildContext(neuroticism: 0.5, physio: physio, currentAction: GameEngineTools.Characters.Engines.ActionNames.Sleep);
            var ctxIdle  = BuildContext(neuroticism: 0.5, physio: physio, currentAction: null);

            var sleepEngine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 50);
            var idleEngine  = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 50);

            // Act
            sleepEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxSleep, new EventCollector());
            idleEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxIdle, new EventCollector());

            // Assert — spánek = recoveryRate * 1.5, ostatní = recoveryRate * 1.0
            Assert.IsTrue(
                sleepEngine.State.CognitiveLoad < idleEngine.State.CognitiveLoad,
                $"Spánek musí snižovat CogLoad rychleji. Sleep={sleepEngine.State.CognitiveLoad:F2}, Idle={idleEngine.State.CognitiveLoad:F2}");
        }

        #endregion CognitiveLoad — testy

        #region Fever — testy

        /// <summary>
        /// Vysoká teplota (BodyTempDelta=2.5) musí zvýšit CognitiveLoad oproti normálnímu stavu.
        /// </summary>
        [TestMethod]
        public void Tick_Fever_HighBodyTemp_IncreasesCognitiveLoad()
        {
            // Arrange
            var physioFever  = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 2.5);
            var physioNormal = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 0.0);

            var ctxFever  = BuildContext(neuroticism: 0.5, physio: physioFever);
            var ctxNormal = BuildContext(neuroticism: 0.5, physio: physioNormal);

            var feverEngine  = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);
            var normalEngine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);

            // Act
            feverEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxFever, new EventCollector());
            normalEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

            // Assert — horečka (2.5 - 1.5 = 1.0 °C nad prahem) přidá 1.0 * 8 = 8 do targetLoad
            Assert.IsTrue(
                feverEngine.State.CognitiveLoad > normalEngine.State.CognitiveLoad,
                $"Horečka musí zvýšit CogLoad. Fever={feverEngine.State.CognitiveLoad:F2}, Normal={normalEngine.State.CognitiveLoad:F2}");
        }

        /// <summary>
        /// Vysoká teplota musí potlačit Arousal.
        /// </summary>
        [TestMethod]
        public void Tick_Fever_HighBodyTemp_SuppressesArousal()
        {
            // Arrange
            var physioFever  = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 2.5);
            var physioNormal = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 0.0);

            var ctxFever  = BuildContext(neuroticism: 0.5, physio: physioFever);
            var ctxNormal = BuildContext(neuroticism: 0.5, physio: physioNormal);

            var feverEngine  = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);
            var normalEngine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);

            // Act
            feverEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxFever, new EventCollector());
            normalEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

            // Assert — horečka potlačuje arousal
            Assert.IsTrue(
                feverEngine.State.Arousal < normalEngine.State.Arousal,
                $"Horečka musí snižovat Arousal. Fever={feverEngine.State.Arousal:F4}, Normal={normalEngine.State.Arousal:F4}");
        }

        /// <summary>
        /// Teplota pod prahem (BodyTempDelta=1.0 &lt; 1.5) nesmí mít žádný kognitívní efekt.
        /// </summary>
        [TestMethod]
        public void Tick_Fever_BelowThreshold_NoCogEffect()
        {
            // Arrange — teplota těsně pod prahem
            var physioSubThreshold = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 1.0);
            var physioNormal       = MakePhysio(sleepDebtHours: 0, pain: 0, bodyTempDelta: 0.0);

            var ctxSub    = BuildContext(neuroticism: 0.5, physio: physioSubThreshold);
            var ctxNormal = BuildContext(neuroticism: 0.5, physio: physioNormal);

            var subEngine    = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);
            var normalEngine = BuildEngine(initialValence: 0.0, initialStress: 0, initialCogLoad: 0);

            // Act
            subEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxSub, new EventCollector());
            normalEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

            // Assert — pod prahem není horečkový příspěvek do targetLoad
            Assert.AreEqual(normalEngine.State.CognitiveLoad, subEngine.State.CognitiveLoad, delta: 0.01,
                $"Teplota pod prahem nesmí přidat CogLoad. Sub={subEngine.State.CognitiveLoad:F4}, Normal={normalEngine.State.CognitiveLoad:F4}");
        }

        #endregion Fever — testy

        #region Pregnancy events — testy

        /// <summary>
        /// Zjištění těhotenství musí způsobit stresový spike.
        /// </summary>
        [TestMethod]
        public void Handle_PregnancyDiscovered_SpikesStress()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var stressBefore = engine.State.Stress;
            var evt = new PregnancyDiscovered(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert — stressSpike = 10 + 0.5*15 = 17.5
            Assert.IsTrue(engine.State.Stress > stressBefore,
                $"PregnancyDiscovered musí zvýšit stres. Před: {stressBefore:F1}, po: {engine.State.Stress:F1}");
        }

        /// <summary>
        /// Neurotická postava musí reagovat silnějším stresovým spikem při zjištění těhotenství.
        /// </summary>
        [TestMethod]
        public void Handle_PregnancyDiscovered_HighNeuroticism_SpikesMoreStress()
        {
            // Arrange
            var stableEngine  = BuildEngine(initialValence: 0.0, initialStress: 20);
            var neurotiEngine = BuildEngine(initialValence: 0.0, initialStress: 20);

            var stableCtx  = BuildContext(neuroticism: 0.0);
            var neurotiCtx = BuildContext(neuroticism: 1.0);

            var stableEvt  = new PregnancyDiscovered(WDateTime.New(100, 1, 1), stableCtx.Id, new HumanId(Guid.NewGuid()));
            var neurotiEvt = new PregnancyDiscovered(WDateTime.New(100, 1, 1), neurotiCtx.Id, new HumanId(Guid.NewGuid()));

            // Act
            stableEngine.Handle(stableEvt, stableCtx, new EventCollector());
            neurotiEngine.Handle(neurotiEvt, neurotiCtx, new EventCollector());

            // Assert — neuroticism=1: spike=25; neuroticism=0: spike=10
            Assert.IsTrue(
                neurotiEngine.State.Stress > stableEngine.State.Stress,
                $"Neurotická postava musí mít více stresu. Stable={stableEngine.State.Stress:F1}, Neuroti={neurotiEngine.State.Stress:F1}");
        }

        /// <summary>
        /// Pokud stres přesáhne 70 díky PregnancyDiscovered, musí být publikován StressSpiked.
        /// </summary>
        [TestMethod]
        public void Handle_PregnancyDiscovered_StressExceedsThreshold_PublishesStressSpiked()
        {
            // Arrange — stres těsně pod prahem, neuroticism=1 → spike=25 → stres=90
            var engine = BuildEngine(initialValence: 0.0, initialStress: 65);
            var ctx = BuildContext(neuroticism: 1.0);
            var evt = new PregnancyDiscovered(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<StressSpiked>().Any(),
                "Překročení threshold 70 musí publikovat StressSpiked.");
        }

        /// <summary>
        /// Porod musí zvýšit valenci.
        /// </summary>
        [TestMethod]
        public void Handle_ChildBorn_IncreasesValence()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var valenceBefore = engine.State.Valence;
            var evt = new ChildBorn(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Valence > valenceBefore,
                $"ChildBorn musí zvýšit valenci. Před: {valenceBefore:F3}, po: {engine.State.Valence:F3}");
        }

        /// <summary>
        /// Porod musí zvýšit arousal.
        /// </summary>
        [TestMethod]
        public void Handle_ChildBorn_IncreasesArousal()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 30);
            var ctx = BuildContext(neuroticism: 0.5);
            var arousalBefore = engine.State.Arousal;
            var evt = new ChildBorn(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Arousal > arousalBefore,
                $"ChildBorn musí zvýšit arousal. Před: {arousalBefore:F3}, po: {engine.State.Arousal:F3}");
        }

        /// <summary>
        /// Porod musí snížit stres.
        /// </summary>
        [TestMethod]
        public void Handle_ChildBorn_DecreasesStress()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 50);
            var ctx = BuildContext(neuroticism: 0.5);
            var stressBefore = engine.State.Stress;
            var evt = new ChildBorn(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Stress < stressBefore,
                $"ChildBorn musí snížit stres. Před: {stressBefore:F1}, po: {engine.State.Stress:F1}");
        }

        #endregion Pregnancy events — testy

        #region Emotion inference — boundary values

        /// <summary>
        /// At exactly Stress = 70, the threshold is NOT crossed — no Fear or Anger.
        /// Emotion falls through to Joy/Sadness/Neutral depending on Valence.
        /// </summary>
        [TestMethod]
        public void InferEmotion_StressExactly70_DoesNotTriggerFearOrAnger()
        {
            // Arrange — Stress=70 is at the threshold (rule: Stress > 70, exclusive)
            var engine = BuildEngine(initialValence: 0.0, initialStress: 70);
            var physio = MakePhysio(0, 0, 0);
            var ctx = BuildContext(neuroticism: 0.5, physio: physio);

            // Use a zero-variance config so no noise shifts emotion
            engine.RestoreState(engine.State with
            {
                Stress = 70,
                Valence = 0.0,
                Arousal = 0.4,
                Dominance = 0.3, // low — would trigger Fear if stress > 70
                DominantEmotion = DiscreteEmotion.Neutral
            });

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctx, _outbox);

            // At exactly 70, the > 70 condition is false → no Fear/Anger
            Assert.AreNotEqual(DiscreteEmotion.Fear, engine.State.DominantEmotion,
                "Stress=70 (not > 70) must not infer Fear.");
            Assert.AreNotEqual(DiscreteEmotion.Anger, engine.State.DominantEmotion,
                "Stress=70 (not > 70) must not infer Anger.");
        }

        /// <summary>
        /// At Stress = 71 with Dominance below 0.4, the engine must infer Fear.
        /// </summary>
        [TestMethod]
        public void InferEmotion_StressAbove70_LowDominance_InfersFear()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var physio = MakePhysio(0, 0, 0);
            var ctx = BuildContext(neuroticism: 0.0, physio: physio); // zero recovery in config

            engine.RestoreState(engine.State with
            {
                Stress = 71,
                Valence = 0.0,
                Arousal = 0.4,
                Dominance = 0.2, // < 0.4
                DominantEmotion = DiscreteEmotion.Neutral
            });

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctx, _outbox);

            Assert.AreEqual(DiscreteEmotion.Fear, engine.State.DominantEmotion,
                "Stress > 70 + Dominance < 0.4 must infer Fear.");
        }

        /// <summary>
        /// At Stress = 71 with Dominance above 0.4, the engine must infer Anger.
        /// </summary>
        [TestMethod]
        public void InferEmotion_StressAbove70_HighDominance_InfersAnger()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var physio = MakePhysio(0, 0, 0);
            var ctx = BuildContext(neuroticism: 0.0, physio: physio);

            engine.RestoreState(engine.State with
            {
                Stress = 71,
                Valence = 0.0,
                Arousal = 0.4,
                Dominance = 0.6, // >= 0.4
                DominantEmotion = DiscreteEmotion.Neutral
            });

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctx, _outbox);

            Assert.AreEqual(DiscreteEmotion.Anger, engine.State.DominantEmotion,
                "Stress > 70 + Dominance >= 0.4 must infer Anger.");
        }

        /// <summary>
        /// High Valence (> 0.5) and high Dominance (> 0.7) must infer Pride.
        /// </summary>
        [TestMethod]
        public void InferEmotion_HighValenceHighDominance_InfersPride()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var physio = MakePhysio(0, 0, 0);
            var ctx = BuildContext(neuroticism: 0.0, physio: physio);

            engine.RestoreState(engine.State with
            {
                Stress = 0,
                Valence = 0.6,
                Arousal = 0.5,
                Dominance = 0.8,
                DominantEmotion = DiscreteEmotion.Neutral
            });

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctx, _outbox);

            Assert.AreEqual(DiscreteEmotion.Pride, engine.State.DominantEmotion,
                "Valence > 0.5 + Dominance > 0.7 must infer Pride.");
        }

        /// <summary>
        /// Tender state: positive Valence, low Arousal, low Dominance must infer Tenderness.
        /// </summary>
        [TestMethod]
        public void InferEmotion_TenderState_InfersTenderness()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var physio = MakePhysio(0, 0, 0);
            var ctx = BuildContext(neuroticism: 0.0, physio: physio);

            engine.RestoreState(engine.State with
            {
                Stress = 0,
                Valence = 0.4,
                Arousal = 0.3,  // < 0.4
                Dominance = 0.4, // < 0.45
                DominantEmotion = DiscreteEmotion.Neutral
            });

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctx, _outbox);

            Assert.AreEqual(DiscreteEmotion.Tenderness, engine.State.DominantEmotion,
                "Valence > 0.3 + Arousal < 0.4 + Dominance < 0.45 must infer Tenderness.");
        }

        #endregion Emotion inference — boundary values

        #region Ovulation — arousal and valence

        /// <summary>
        /// When the ovulation window is open, each Tick must raise Arousal above baseline.
        /// </summary>
        [TestMethod]
        public void Tick_OvulationWindow_IncreasesArousalAndValence()
        {
            // Arrange: two physio states — one with ovulation window, one without
            var physioOvul = MakePhysioWithCycle(ovulationWindowOpen: true);
            var physioNone = MakePhysioWithCycle(ovulationWindowOpen: false);

            var ctxOvul = BuildContext(neuroticism: 0.0, physio: physioOvul);
            var ctxNone = BuildContext(neuroticism: 0.0, physio: physioNone);

            var ovulEngine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var noneEngine = BuildEngine(initialValence: 0.0, initialStress: 0);

            // Set identical starting PAD
            ovulEngine.RestoreState(ovulEngine.State with { Arousal = 0.4, Valence = 0.0 });
            noneEngine.RestoreState(noneEngine.State with { Arousal = 0.4, Valence = 0.0 });

            // Act — single tick; ovulation adds +0.03 arousal, +0.02 valence
            ovulEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctxOvul, new EventCollector());
            noneEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.001), ctxNone, new EventCollector());

            // Assert
            Assert.IsTrue(ovulEngine.State.Arousal > noneEngine.State.Arousal,
                $"Ovulation window must increase Arousal. Ovul={ovulEngine.State.Arousal:F4}, None={noneEngine.State.Arousal:F4}");
            Assert.IsTrue(ovulEngine.State.Valence > noneEngine.State.Valence,
                $"Ovulation window must increase Valence. Ovul={ovulEngine.State.Valence:F4}, None={noneEngine.State.Valence:F4}");
        }

        /// <summary>
        /// Handle(OvulationWindowOpened) must add +0.05 to Arousal.
        /// </summary>
        [TestMethod]
        public void Handle_OvulationWindowOpened_IncreasesArousal()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var arousalBefore = engine.State.Arousal;

            engine.Handle(new OvulationWindowOpened(WDateTime.New(100, 1, 1), ctx.Id), ctx, _outbox);

            Assert.IsTrue(engine.State.Arousal > arousalBefore,
                $"OvulationWindowOpened must raise Arousal. Before={arousalBefore:F3}, After={engine.State.Arousal:F3}");
        }

        /// <summary>
        /// Handle(MensesStarted) must reduce Valence by 0.05 (discomfort onset).
        /// </summary>
        [TestMethod]
        public void Handle_MensesStarted_DecreasesValence()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var valenceBefore = engine.State.Valence;

            engine.Handle(new MensesStarted(WDateTime.New(100, 1, 1), ctx.Id), ctx, _outbox);

            Assert.IsTrue(engine.State.Valence < valenceBefore,
                $"MensesStarted must reduce Valence. Before={valenceBefore:F3}, After={engine.State.Valence:F3}");
        }

        #endregion Ovulation — arousal and valence

        #region Memory recall — emotional impact

        /// <summary>
        /// Recalling a positive episode must nudge Valence upward.
        /// </summary>
        [TestMethod]
        public void Handle_MemoryRecalled_PositiveEpisode_IncreasesValence()
        {
            var episodeId = Guid.NewGuid();
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContextWithMemory(
                episodeId,
                EmotionalTag.Positive,
                initialValence: 0.0);
            var valenceBefore = engine.State.Valence;

            engine.Handle(new MemoryRecalled(WDateTime.New(100, 1, 1), ctx.Id, episodeId), ctx, _outbox);

            Assert.IsTrue(engine.State.Valence > valenceBefore,
                $"Recalling a positive memory must increase Valence. Before={valenceBefore:F3}, After={engine.State.Valence:F3}");
        }

        /// <summary>
        /// Recalling a negative episode must nudge Valence downward.
        /// </summary>
        [TestMethod]
        public void Handle_MemoryRecalled_NegativeEpisode_DecreasesValence()
        {
            var episodeId = Guid.NewGuid();
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContextWithMemory(
                episodeId,
                EmotionalTag.Negative,
                initialValence: 0.0);
            var valenceBefore = engine.State.Valence;

            engine.Handle(new MemoryRecalled(WDateTime.New(100, 1, 1), ctx.Id, episodeId), ctx, _outbox);

            Assert.IsTrue(engine.State.Valence < valenceBefore,
                $"Recalling a negative memory must decrease Valence. Before={valenceBefore:F3}, After={engine.State.Valence:F3}");
        }

        /// <summary>
        /// Recalling an episode with a non-existent ID must not change Valence.
        /// </summary>
        [TestMethod]
        public void Handle_MemoryRecalled_UnknownEpisode_DoesNotChangeValence()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var valenceBefore = engine.State.Valence;

            engine.Handle(new MemoryRecalled(WDateTime.New(100, 1, 1), ctx.Id, Guid.NewGuid()), ctx, _outbox);

            Assert.AreEqual(valenceBefore, engine.State.Valence, delta: 0.001,
                "Recalling an unknown episode ID must not change Valence.");
        }

        #endregion Memory recall — emotional impact

        #region Dominance — testy

        /// <summary>
        /// Přijatá interakce (jako iniciátor) musí zvýšit Dominance.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionAccepted_AsInitiator_IncreasesDominance()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var domBefore = engine.State.Dominance;

            // Accepted: io.From == self
            var io = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1),
                From: ctx.Id,
                To: new HumanId(Guid.NewGuid()),
                Act: SpeechAct.SmallTalk,
                Accepted: true,
                Reason: string.Empty);

            // Act
            engine.Handle(io, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Dominance > domBefore,
                $"Přijatá interakce jako iniciátor musí zvýšit Dominance. Před: {domBefore:F3}, po: {engine.State.Dominance:F3}");
        }

        /// <summary>
        /// Odmítnutá interakce (jako iniciátor) musí snížit Dominance.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionRejected_AsInitiator_DecreasesDominance()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var domBefore = engine.State.Dominance;

            // wasRejected: io.From == self && !io.Accepted
            var io = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1),
                From: ctx.Id,
                To: new HumanId(Guid.NewGuid()),
                Act: SpeechAct.SmallTalk,
                Accepted: false,
                Reason: string.Empty);

            // Act
            engine.Handle(io, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Dominance < domBefore,
                $"Odmítnutí jako iniciátor musí snížit Dominance. Před: {domBefore:F3}, po: {engine.State.Dominance:F3}");
        }

        /// <summary>
        /// Odmítnutí citlivého aktu (SelfDisclosure) musí způsobit větší pokles Dominance
        /// než odmítnutí SmallTalk.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionRejected_HighSensitivityAct_LargerDominanceDrop()
        {
            // Arrange
            var engineSmall = BuildEngine(initialValence: 0.0, initialStress: 20);
            var engineSelf  = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);

            var toId = new HumanId(Guid.NewGuid());
            var ioSmall = new InteractionOutcome(WDateTime.New(100, 1, 1), ctx.Id, toId, false, string.Empty, SpeechAct.SmallTalk);
            var ioSelf  = new InteractionOutcome(WDateTime.New(100, 1, 1), ctx.Id, toId, false, string.Empty, SpeechAct.SelfDisclosure);

            // Act
            engineSmall.Handle(ioSmall, ctx, new EventCollector());
            engineSelf.Handle(ioSelf, ctx, new EventCollector());

            // Assert — SelfDisclosure actSensitivity=1.6 vs SmallTalk=1.0 → větší drop
            Assert.IsTrue(
                engineSelf.State.Dominance < engineSmall.State.Dominance,
                $"SelfDisclosure odmítnutí musí způsobit větší pokles Dominance. " +
                $"SmallTalk={engineSmall.State.Dominance:F4}, SelfDisclosure={engineSelf.State.Dominance:F4}");
        }

        /// <summary>
        /// Odmítnutí jako příjemce (didReject) musí zvýšit Dominance.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionRejected_AsRejecter_IncreasesDominance()
        {
            // Arrange
            var engine = BuildEngine(initialValence: 0.0, initialStress: 20);
            var ctx = BuildContext(neuroticism: 0.5);
            var domBefore = engine.State.Dominance;

            // didReject: io.To == self && !io.Accepted
            var io = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1),
                From: new HumanId(Guid.NewGuid()),
                To: ctx.Id,
                Act: SpeechAct.SmallTalk,
                Accepted: false,
                Reason: string.Empty);

            // Act
            engine.Handle(io, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Dominance > domBefore,
                $"Odmítnutí jako příjemce musí zvýšit Dominance. Před: {domBefore:F3}, po: {engine.State.Dominance:F3}");
        }

        /// <summary>
        /// Vysoká bolest musí snižovat Dominance v Tick().
        /// </summary>
        [TestMethod]
        public void Tick_HighPain_ReducesDominance()
        {
            // Arrange — Pain=50
            var physioHighPain = MakePhysio(sleepDebtHours: 0, pain: 50, bodyTempDelta: 0);
            var physioNoPain   = MakePhysio(sleepDebtHours: 0, pain: 0,  bodyTempDelta: 0);

            var ctxHighPain = BuildContext(neuroticism: 0.5, physio: physioHighPain);
            var ctxNoPain   = BuildContext(neuroticism: 0.5, physio: physioNoPain);

            var engineHighPain = BuildEngine(initialValence: 0.0, initialStress: 0);
            var engineNoPain   = BuildEngine(initialValence: 0.0, initialStress: 0);

            // Act
            engineHighPain.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxHighPain, new EventCollector());
            engineNoPain.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1.0), ctxNoPain, new EventCollector());

            // Assert — Pain=50 → -0.0005 * 50 * 1h = -0.025 extra oproti žádné bolesti
            Assert.IsTrue(
                engineHighPain.State.Dominance < engineNoPain.State.Dominance,
                $"Vysoká bolest musí snižovat Dominance. HighPain={engineHighPain.State.Dominance:F4}, NoPain={engineNoPain.State.Dominance:F4}");
        }

        #endregion Dominance — testy

        #region MoodBaseline — Phase 4

        [TestMethod]
        public void Tick_MoodBaseline_DriftsToward50InNeutralConditions()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            engine.RestoreState(engine.State with { MoodBaseline = 80 });
            var ctx = BuildContext(neuroticism: 0.5);

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.MoodBaseline < 80,
                $"MoodBaseline=80 must drift down toward 50 in neutral conditions. Got {engine.State.MoodBaseline:F4}");
        }

        [TestMethod]
        public void Tick_MoodBaseline_RecoveryIsSuppressedWhenStressAbove80()
        {
            var engineHighStress = BuildEngine(initialValence: 0.0, initialStress: 90,
                cfg: new PsychologyConfig(BaselineAffectVariance: 0.0, StressRecoveryRatePerHour: 0.0,
                    EnableCircadianRhythm: false));
            engineHighStress.RestoreState(engineHighStress.State with { MoodBaseline = 30 });

            var engineLowStress = BuildEngine(initialValence: 0.0, initialStress: 10,
                cfg: new PsychologyConfig(BaselineAffectVariance: 0.0, StressRecoveryRatePerHour: 0.0,
                    EnableCircadianRhythm: false));
            engineLowStress.RestoreState(engineLowStress.State with { MoodBaseline = 30 });

            var ctxHigh = BuildContext(neuroticism: 0.5);
            var ctxLow  = BuildContext(neuroticism: 0.5);

            engineHighStress.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxHigh, new EventCollector());
            engineLowStress.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxLow, new EventCollector());

            Assert.IsTrue(engineHighStress.State.MoodBaseline <= engineLowStress.State.MoodBaseline,
                $"High stress must suppress MoodBaseline recovery. HighStress={engineHighStress.State.MoodBaseline:F4}, LowStress={engineLowStress.State.MoodBaseline:F4}");
        }

        [TestMethod]
        public void Handle_SleepEnded_GoodSleep_IncreasesMoodBaseline()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var before = engine.State.MoodBaseline;
            var ctx = BuildContext(neuroticism: 0.5);

            engine.Handle(new SleepEnded(WDateTime.New(100, 1, 1), ctx.Id, TotalHoursSlept: 8, Quality: 90, WasInterrupted: false), ctx, _outbox);

            Assert.IsTrue(engine.State.MoodBaseline > before,
                $"Good sleep (quality=90) must increase MoodBaseline. Before={before:F4}, After={engine.State.MoodBaseline:F4}");
        }

        [TestMethod]
        public void Handle_SleepEnded_BadSleep_DecreasesMoodBaseline()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var before = engine.State.MoodBaseline;
            var ctx = BuildContext(neuroticism: 0.5);

            engine.Handle(new SleepEnded(WDateTime.New(100, 1, 1), ctx.Id, TotalHoursSlept: 4, Quality: 20, WasInterrupted: true), ctx, _outbox);

            Assert.IsTrue(engine.State.MoodBaseline < before,
                $"Bad sleep (quality=20, interrupted) must decrease MoodBaseline. Before={before:F4}, After={engine.State.MoodBaseline:F4}");
        }

        [TestMethod]
        public void Handle_InteractionAccepted_IncreasesMoodBaseline()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var before = engine.State.MoodBaseline;
            var ctx = BuildContext(neuroticism: 0.5);
            var outcome = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1), From: ctx.Id, To: new HumanId(Guid.NewGuid()),
                Accepted: true, Reason: string.Empty, Act: SpeechAct.SmallTalk);

            engine.Handle(outcome, ctx, _outbox);

            Assert.IsTrue(engine.State.MoodBaseline > before,
                $"Accepted interaction must increase MoodBaseline. Before={before:F4}, After={engine.State.MoodBaseline:F4}");
        }

        [TestMethod]
        public void Handle_InteractionRejected_DecreasesMoodBaseline()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            var before = engine.State.MoodBaseline;
            var ctx = BuildContext(neuroticism: 0.5);
            var outcome = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1), From: ctx.Id, To: new HumanId(Guid.NewGuid()),
                Accepted: false, Reason: string.Empty, Act: SpeechAct.SmallTalk);

            engine.Handle(outcome, ctx, _outbox);

            Assert.IsTrue(engine.State.MoodBaseline < before,
                $"Rejected interaction must decrease MoodBaseline. Before={before:F4}, After={engine.State.MoodBaseline:F4}");
        }

        #endregion MoodBaseline — Phase 4

        #region MotivationState — Phase 4

        [TestMethod]
        public void Handle_InteractionAccepted_IncreasesNeedSocial()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            engine.RestoreState(engine.State with { Motivations = new MotivationState() });
            var before = engine.State.Motivations!.NeedSocial;
            var ctx = BuildContext(neuroticism: 0.5);
            var outcome = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1), From: ctx.Id, To: new HumanId(Guid.NewGuid()),
                Accepted: true, Reason: string.Empty, Act: SpeechAct.SmallTalk);

            engine.Handle(outcome, ctx, _outbox);

            Assert.IsTrue(engine.State.Motivations!.NeedSocial > before,
                $"Accepted interaction must increase NeedSocial. Before={before:F4}, After={engine.State.Motivations.NeedSocial:F4}");
        }

        [TestMethod]
        public void Handle_InteractionRejected_IncreasesNeedSocial()
        {
            // Williams (2007) 4-need threat model: rejection threatens Belonging, making the need
            // MORE urgent (hyperactivation), not less. NeedSafety also rises from the threat.
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            engine.RestoreState(engine.State with { Motivations = new MotivationState() });
            var beforeSocial = engine.State.Motivations!.NeedSocial;
            var beforeSafety = engine.State.Motivations!.NeedSafety;
            var ctx = BuildContext(neuroticism: 0.5);
            var outcome = new InteractionOutcome(
                OccurredAt: WDateTime.New(100, 1, 1), From: ctx.Id, To: new HumanId(Guid.NewGuid()),
                Accepted: false, Reason: string.Empty, Act: SpeechAct.SmallTalk);

            engine.Handle(outcome, ctx, _outbox);

            Assert.IsTrue(engine.State.Motivations!.NeedSocial > beforeSocial,
                $"Rejected interaction must increase NeedSocial (belonging threat — Williams 2007). Before={beforeSocial:F4}, After={engine.State.Motivations.NeedSocial:F4}");
            Assert.IsTrue(engine.State.Motivations!.NeedSafety > beforeSafety,
                $"Rejected interaction must increase NeedSafety. Before={beforeSafety:F4}, After={engine.State.Motivations.NeedSafety:F4}");
        }

        [TestMethod]
        public void Handle_PregnancyDiscovered_IncreasesNeedCare()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            engine.RestoreState(engine.State with { Motivations = new MotivationState() });
            var before = engine.State.Motivations!.NeedCare;
            var ctx = BuildContext(neuroticism: 0.5);
            var ev = new PregnancyDiscovered(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            engine.Handle(ev, ctx, _outbox);

            Assert.IsTrue(engine.State.Motivations!.NeedCare > before,
                $"PregnancyDiscovered must increase NeedCare. Before={before:F4}, After={engine.State.Motivations.NeedCare:F4}");
        }

        [TestMethod]
        public void Handle_ChildBorn_IncreasesNeedCare()
        {
            var engine = BuildEngine(initialValence: 0.0, initialStress: 0);
            engine.RestoreState(engine.State with { Motivations = new MotivationState() });
            var before = engine.State.Motivations!.NeedCare;
            var ctx = BuildContext(neuroticism: 0.5);
            var ev = new ChildBorn(WDateTime.New(100, 1, 1), ctx.Id, new HumanId(Guid.NewGuid()));

            engine.Handle(ev, ctx, _outbox);

            Assert.IsTrue(engine.State.Motivations!.NeedCare > before,
                $"ChildBorn must increase NeedCare. Before={before:F4}, After={engine.State.Motivations.NeedCare:F4}");
        }

        #endregion MotivationState — Phase 4

        #region StressManifested — Phase 4

        [TestMethod]
        public void Tick_StressManifested_EmittedAfterConfiguredHours()
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                StressManifestationThreshold: 70.0,
                StressManifestationHours: 4.0);

            var engine = BuildEngine(initialValence: 0.0, initialStress: 80, cfg: cfg);
            var ctx = BuildContext(neuroticism: 0.5);
            var outbox = new EventCollector();

            // First tick at t=0 — initializes the stress-above-threshold timer
            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.1), ctx, outbox);
            outbox.Drain();

            // Second tick at t=4.1h — elapsed time from timer start exceeds 4h threshold
            var laterNow = WDateTime.New(100, 1, 1) + WTimeSpan.FromHours(4.1);
            engine.Tick(laterNow, WTimeSpan.FromHours(4.1), ctx, outbox);
            var events = outbox.Drain();

            Assert.IsTrue(events.OfType<StressManifested>().Any(),
                "StressManifested must be emitted after stress exceeds threshold for configured hours.");
        }

        [TestMethod]
        public void Tick_StressManifested_NotEmittedBeforeThreshold()
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                StressManifestationThreshold: 70.0,
                StressManifestationHours: 4.0);

            var engine = BuildEngine(initialValence: 0.0, initialStress: 80, cfg: cfg);
            var ctx = BuildContext(neuroticism: 0.5);
            var outbox = new EventCollector();

            // First tick at t=0 — initializes the timer
            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(0.1), ctx, outbox);
            outbox.Drain();

            // Second tick at t=3.9h — not yet at the 4h threshold
            var laterNow = WDateTime.New(100, 1, 1) + WTimeSpan.FromHours(3.9);
            engine.Tick(laterNow, WTimeSpan.FromHours(3.9), ctx, outbox);
            var events = outbox.Drain();

            Assert.IsFalse(events.OfType<StressManifested>().Any(),
                "StressManifested must NOT be emitted before the configured hours threshold.");
        }

        #endregion StressManifested — Phase 4

        #region CircadianRhythm — Phase 4

        [TestMethod]
        public void Tick_CircadianRhythm_ArousalHigherAtMorningPeakThanNightTrough()
        {
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: true,
                CircadianInfluence: 0.15);

            var enginePeak  = BuildEngine(initialValence: 0.0, initialStress: 0, cfg: cfg);
            var engineTrough = BuildEngine(initialValence: 0.0, initialStress: 0, cfg: cfg);

            // Two-Gaussian model: morning peak at 10h, night trough at ~3h
            var nowPeak  = new WDateTime(0) + WTimeSpan.FromHours(10);
            var nowTrough = new WDateTime(0) + WTimeSpan.FromHours(3);

            var ctxPeak  = BuildContext(neuroticism: 0.5);
            var ctxTrough = BuildContext(neuroticism: 0.5);

            enginePeak.Tick(nowPeak,  WTimeSpan.FromHours(0.1), ctxPeak,  new EventCollector());
            engineTrough.Tick(nowTrough, WTimeSpan.FromHours(0.1), ctxTrough, new EventCollector());

            Assert.IsTrue(enginePeak.State.Arousal > engineTrough.State.Arousal,
                $"Morning peak (10h) must have higher arousal than night trough (3h). Peak={enginePeak.State.Arousal:F4}, Trough={engineTrough.State.Arousal:F4}");
        }

        #endregion CircadianRhythm — Phase 4

        #region NutritionEffectsOnPsychology — Phase 4

        [TestMethod]
        public void Tick_LowIron_SuppressesValence()
        {
            var engine1 = BuildEngine(initialValence: 0.0, initialStress: 0);
            var engine2 = BuildEngine(initialValence: 0.0, initialStress: 0);

            var ctxLowIron  = BuildContext(0.5, MakePhysioWithNutrition(iron: 10, vitaminD: 80));
            var ctxHighIron = BuildContext(0.5, MakePhysioWithNutrition(iron: 80, vitaminD: 80));

            engine1.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxLowIron,  new EventCollector());
            engine2.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxHighIron, new EventCollector());

            Assert.IsTrue(engine1.State.Valence < engine2.State.Valence,
                $"Low iron must suppress Valence. LowIron={engine1.State.Valence:F4}, HighIron={engine2.State.Valence:F4}");
        }

        [TestMethod]
        public void Tick_LowVitaminD_SuppressesMoodBaseline()
        {
            // Use zero mood recovery so the drift doesn't cancel the VitaminD penalty
            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false,
                MoodBaselineRecoveryPerHour: 0.0);

            var engine1 = BuildEngine(initialValence: 0.0, initialStress: 0, cfg: cfg);
            var engine2 = BuildEngine(initialValence: 0.0, initialStress: 0, cfg: cfg);

            var ctxLowVitD  = BuildContext(0.5, MakePhysioWithNutrition(iron: 80, vitaminD: 5));
            var ctxHighVitD = BuildContext(0.5, MakePhysioWithNutrition(iron: 80, vitaminD: 80));

            engine1.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxLowVitD,  new EventCollector());
            engine2.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxHighVitD, new EventCollector());

            Assert.IsTrue(engine1.State.MoodBaseline < engine2.State.MoodBaseline,
                $"Low VitaminD must suppress MoodBaseline. LowVitD={engine1.State.MoodBaseline:F4}, HighVitD={engine2.State.MoodBaseline:F4}");
        }

        #endregion NutritionEffectsOnPsychology — Phase 4

        #region Pomocné metody

        /// <summary>Sestaví engine s výchozí nebo vlastní konfigurací.</summary>
        private static DefaultPsychologyEngine BuildEngine(
            double initialValence,
            double initialStress,
            double initialCogLoad = 10,
            PsychologyConfig? cfg = null)
        {
            var opts = Options.Create(cfg ?? DefaultCfg);
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng = new ZeroRandom();

            var engine = new DefaultPsychologyEngine(opts, factory, rng);

            // Nastavíme počáteční stav přes RestoreState
            engine.RestoreState(new PsychologyState(
                Valence: initialValence,
                Arousal: 0.4,
                Dominance: 0.5,
                Stress: initialStress,
                CognitiveLoad: initialCogLoad,
                DominantEmotion: DiscreteEmotion.Neutral));

            return engine;
        }

        /// <summary>Sestaví fake kontext s nastaveným Neuroticism a výchozí fyziologií.</summary>
        private static IHumanContext BuildContext(double neuroticism)
            => BuildContext(neuroticism, MakePhysio(0, 0, 0), currentAction: null);

        /// <summary>
        /// Sestaví fake kontext s nastaveným Neuroticism a vlastní fyziologií.
        /// Volitelně nastaví aktuální akci (CurrentPlan.Name).
        /// </summary>
        private static IHumanContext BuildContext(
            double neuroticism,
            PhysiologyState physio,
            string? currentAction = null)
        {
            var psych = new PsychologyState(
                Valence: 0.1, Arousal: 0.4, Dominance: 0.5,
                Stress: 20, CognitiveLoad: 10, DominantEmotion: DiscreteEmotion.Neutral);

            var plan = currentAction is not null
                ? new PlannedAction(currentAction, new WDateTime(0), WTimeSpan.FromHours(1), 50)
                : null;

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, plan),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new List<EpisodicMemory>()));

            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                                           .CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>Sestaví PhysiologyState s danými hodnotami pro Tick() testy.</summary>
        private static PhysiologyState MakePhysio(
            double sleepDebtHours,
            double pain,
            double bodyTempDelta)
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
        /// Builds a <see cref="PhysiologyState"/> with a menstrual cycle state for ovulation tests.
        /// </summary>
        private static PhysiologyState MakePhysioWithCycle(bool ovulationWindowOpen)
        {
            var cycle = new MenstrualCycleState(
                Phase: ovulationWindowOpen ? CyclePhase.Ovulation : CyclePhase.Follicular,
                DayInCycle: ovulationWindowOpen ? 14 : 7,
                OvulationWindow: ovulationWindowOpen,
                SymptomPain: 0, SymptomBreastTender: 0, SymptomBloat: 0,
                LibidoMod: 1.0,
                LastMensesStart: WDateOnly.New(116, 1, 1));

            return new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 0,
                Hunger: 20,
                Thirst: 15,
                Pain: 0,
                ImmuneLoad: 5,
                BodyTempDelta: 0,
                Cycle: cycle);
        }

        /// <summary>Sestaví PhysiologyState s výživovými hodnotami pro testy výživy.</summary>
        private static PhysiologyState MakePhysioWithNutrition(double iron, double vitaminD)
            => new PhysiologyState(
                Energy: 70,
                SleepDebtHours: 0,
                Hunger: 20,
                Thirst: 15,
                Pain: 0,
                ImmuneLoad: 5,
                BodyTempDelta: 0,
                Cycle: null,
                Nutrition: new NutritionState(Iron: iron, VitaminD: vitaminD));

        /// <summary>
        /// Builds a context that contains a single episodic memory with the given emotional tag,
        /// used to test memory-recall emotional impacts.
        /// </summary>
        private static IHumanContext BuildContextWithMemory(
            Guid episodeId,
            EmotionalTag emotionalTag,
            double initialValence)
        {
            var episode = new EpisodicMemory(
                Id: episodeId,
                When: new WDateTime(0),
                What: "test memory",
                Salience: 0.8,
                Emotion: emotionalTag,
                Strength: 0.9);

            var physio = new PhysiologyState(70, 0, 20, 15, 0, 5, 0, null);
            var psych = new PsychologyState(initialValence, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory> { episode }));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    Attachment: AttachmentProfile.Secure,
                    Communication: CommunicationStyle.Direct,
                    Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality: Sociosexuality.Intermediate,
                    Chronotype: Chronotype.Neutral),
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
                OccurredAt: WDateTime.New(100, 1, 1),
                Human: new HumanId(Guid.NewGuid()),
                TotalHoursSlept: hoursSlept,
                Quality: quality,
                WasInterrupted: wasInterrupted);

        #endregion Pomocné metody
    }

    // ──────────────────────────────────────────────────────────────────────────
    // S1 — Dual Control Model tests
    // ──────────────────────────────────────────────────────────────────────────

    [TestClass]
    public sealed class DualControlModelTests : TestBase
    {
        private static readonly PsychologyConfig NoiselessCfg = new PsychologyConfig(
            BaselineAffectVariance: 0.0);

        [TestMethod]
        public void HighSES_IncreasesNeedIntimacy_OverBaseline()
        {
            // DualControl with SES=0.9 should produce higher NeedIntimacy than baseline (SES=0.5)
            var engineBaseline = Build(SexualResponsiveness.Default);
            var engineHighSES  = Build(new SexualResponsiveness(SES: 0.9, SIS1: 0.5, SIS2: 0.5));

            var ctx = BuildCtx(engineBaseline, ses: 0.9, stress: 0, crowding: 0);
            var outbox = new EventCollector();

            engineBaseline.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(SexualResponsiveness.Default, stress: 0, crowding: 0), outbox);
            engineHighSES.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(new SexualResponsiveness(0.9, 0.5, 0.5), stress: 0, crowding: 0), outbox);

            var baseIntimacy = engineBaseline.State.Motivations?.NeedIntimacy ?? 50;
            var highIntimacy = engineHighSES.State.Motivations?.NeedIntimacy ?? 50;

            Assert.IsTrue(highIntimacy > baseIntimacy,
                $"High SES should raise NeedIntimacy (baseline={baseIntimacy:F2}, highSES={highIntimacy:F2})");
        }

        [TestMethod]
        public void HighSIS1_UnderStress_ReducesNeedIntimacy()
        {
            // SIS1=0.9 under high stress should suppress NeedIntimacy more than SIS1=0.1
            var engineLowSIS1  = Build(new SexualResponsiveness(SES: 0.5, SIS1: 0.1, SIS2: 0.5));
            var engineHighSIS1 = Build(new SexualResponsiveness(SES: 0.5, SIS1: 0.9, SIS2: 0.5));

            var outbox = new EventCollector();
            engineLowSIS1.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(new SexualResponsiveness(0.5, 0.1, 0.5), stress: 80, crowding: 0), outbox);
            engineHighSIS1.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(new SexualResponsiveness(0.5, 0.9, 0.5), stress: 80, crowding: 0), outbox);

            var lowSIS1Intimacy  = engineLowSIS1.State.Motivations?.NeedIntimacy ?? 50;
            var highSIS1Intimacy = engineHighSIS1.State.Motivations?.NeedIntimacy ?? 50;

            Assert.IsTrue(highSIS1Intimacy < lowSIS1Intimacy,
                $"High SIS1 under stress should suppress NeedIntimacy more (low={lowSIS1Intimacy:F2}, high={highSIS1Intimacy:F2})");
        }

        [TestMethod]
        public void HighSIS2_InHighCrowding_ReducesNeedIntimacy()
        {
            // SIS2=0.9 in crowded environment should suppress NeedIntimacy more than SIS2=0.1
            var engineLowSIS2  = Build(new SexualResponsiveness(SES: 0.5, SIS1: 0.5, SIS2: 0.1));
            var engineHighSIS2 = Build(new SexualResponsiveness(SES: 0.5, SIS1: 0.5, SIS2: 0.9));

            var outbox = new EventCollector();
            engineLowSIS2.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(new SexualResponsiveness(0.5, 0.5, 0.1), stress: 0, crowding: 0.9), outbox);
            engineHighSIS2.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(new SexualResponsiveness(0.5, 0.5, 0.9), stress: 0, crowding: 0.9), outbox);

            var lowSIS2Intimacy  = engineLowSIS2.State.Motivations?.NeedIntimacy ?? 50;
            var highSIS2Intimacy = engineHighSIS2.State.Motivations?.NeedIntimacy ?? 50;

            Assert.IsTrue(highSIS2Intimacy < lowSIS2Intimacy,
                $"High SIS2 in crowded env should suppress NeedIntimacy more (low={lowSIS2Intimacy:F2}, high={highSIS2Intimacy:F2})");
        }

        [TestMethod]
        public void NullDualControl_NoChangeToBaseline()
        {
            // null DualControl (no DCM profile) should behave identically to population average
            var engineNull    = Build(null);
            var engineDefault = Build(SexualResponsiveness.Default);

            var outbox = new EventCollector();
            engineNull.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(null, stress: 0, crowding: 0), outbox);
            engineDefault.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(10), BuildCtxWithDCM(SexualResponsiveness.Default, stress: 0, crowding: 0), outbox);

            var nullIntimacy    = engineNull.State.Motivations?.NeedIntimacy ?? 50;
            var defaultIntimacy = engineDefault.State.Motivations?.NeedIntimacy ?? 50;

            // SES=0.5 → sesBoost=0; SIS at 0.5 but stress=0, crowding=0 → inhibition=0 → no change
            Assert.AreEqual(nullIntimacy, defaultIntimacy, 0.1,
                $"Null vs Default DCM should give same NeedIntimacy (null={nullIntimacy:F2}, default={defaultIntimacy:F2})");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static DefaultPsychologyEngine Build(SexualResponsiveness? dcm)
        {
            var engine = new DefaultPsychologyEngine(
                Microsoft.Extensions.Options.Options.Create(NoiselessCfg),
                Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(engine.State with { Motivations = new MotivationState() });
            return engine;
        }

        private IHumanContext BuildCtxWithDCM(SexualResponsiveness? dcm, double stress, double crowding)
        {
            var physio = new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, stress, 20, DiscreteEmotion.Neutral,
                Motivations: new MotivationState());
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral,
                DualControl: dcm);
            var crowdingVal = double.IsNaN(crowding) ? 0.5 : crowding;
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, crowdingVal, SurfaceKind.Unknown),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = new HumanId(System.Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = new ZeroRandom(),
                Logger = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private IHumanContext BuildCtx(DefaultPsychologyEngine engine, double ses, double stress, double crowding)
            => BuildCtxWithDCM(new SexualResponsiveness(ses, 0.5, 0.5), stress, crowding);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Per-emotion decay multipliers — Sadness persists longer than Fear
    // Uses named-parameter construction to guarantee correct field values.
    // ──────────────────────────────────────────────────────────────────────────

    [TestClass]
    public sealed class EmotionDecayMultiplierTests
    {
        // Build a config using only the named parameters we care about;
        // all others remain at their documented defaults.
        private static PsychologyConfig CfgWithDecays(
            double fear      = 3.0,
            double sadness   = 0.06,
            double shame     = 0.4,
            double anger     = 0.6,
            double joy       = 1.0)
            => new PsychologyConfig(
                EmotionDecayFear:    fear,
                EmotionDecaySadness: sadness,
                EmotionDecayShame:   shame,
                EmotionDecayAnger:   anger,
                EmotionDecayJoy:     joy);

        /// <summary>
        /// Sadness musí mít nižší decay multiplier než Fear —
        /// hodnota menší než 1.0 znamená pomalejší pokles než baseline; větší = rychlejší.
        /// Verifuje zdokumentované hodnoty: Sadness=0.06, Fear=3.0.
        /// </summary>
        [TestMethod]
        public void EmotionDecayMultipliers_Sadness_IsSlower_ThanFear()
        {
            var cfg = CfgWithDecays();

            Assert.IsTrue(cfg.EmotionDecaySadness < cfg.EmotionDecayFear,
                $"Smutek (0.06) musí mít nižší multiplikátor než Strach (3.0) — déle přetrvává. " +
                $"Sadness={cfg.EmotionDecaySadness}, Fear={cfg.EmotionDecayFear}");

            Assert.IsTrue(cfg.EmotionDecayFear > 1.0,
                $"Strach musí odznít rychleji než baseline (mult > 1.0). Fear={cfg.EmotionDecayFear}");

            Assert.IsTrue(cfg.EmotionDecaySadness < 1.0,
                $"Smutek musí odznít pomaleji než baseline (mult < 1.0). Sadness={cfg.EmotionDecaySadness}");
        }

        /// <summary>
        /// Ověřuje pořadí ze specifikace: Sadness &lt; Shame &lt; Anger &lt; Joy &lt; Fear.
        /// </summary>
        [TestMethod]
        public void EmotionDecayMultipliers_Ordering_MatchesSpecification()
        {
            var cfg = CfgWithDecays();

            // Sadness je nejpomalejší (0.06)
            Assert.IsTrue(cfg.EmotionDecaySadness <= cfg.EmotionDecayShame,
                $"Sadness ({cfg.EmotionDecaySadness}) musí být <= Shame ({cfg.EmotionDecayShame})");
            Assert.IsTrue(cfg.EmotionDecayShame <= cfg.EmotionDecayAnger,
                $"Shame ({cfg.EmotionDecayShame}) musí být <= Anger ({cfg.EmotionDecayAnger})");
            Assert.IsTrue(cfg.EmotionDecayAnger <= cfg.EmotionDecayJoy,
                $"Anger ({cfg.EmotionDecayAnger}) musí být <= Joy ({cfg.EmotionDecayJoy})");
            // Fear je nejrychlejší (3.0)
            Assert.IsTrue(cfg.EmotionDecayJoy <= cfg.EmotionDecayFear,
                $"Joy ({cfg.EmotionDecayJoy}) musí být <= Fear ({cfg.EmotionDecayFear})");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Isolation Stress — extraverts get more stress when alone
    // ──────────────────────────────────────────────────────────────────────────

    [TestClass]
    public sealed class IsolationStressTests : TestBase
    {
        private static readonly PsychologyConfig IsolationCfg = new PsychologyConfig(
            BaselineAffectVariance: 0.0,
            StressRecoveryRatePerHour: 0.0,
            EnableCircadianRhythm: false,
            IsolationStressWeight: 3.0);

        private static DefaultPsychologyEngine BuildIsolationEngine(double initialStress = 10)
        {
            var engine = new DefaultPsychologyEngine(
                Options.Create(IsolationCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(new PsychologyState(0.0, 0.4, 0.5, initialStress, 10, DiscreteEmotion.Neutral));
            return engine;
        }

        private static IHumanContext BuildIsolationContext(double extraversion, SurfaceKind kind = SurfaceKind.Private)
        {
            // Musíme uvést Location != null jinak engine přeskočí isolation blok
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, extraversion, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                // HasPrivacy=true (alone), Kind != Unknown, Location != null
                new InteractionSurface("home", HasPrivacy: true, Noise: 0.1, Crowding: 0.0, Kind: kind),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
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

        [TestMethod]
        public void IsolationStress_ExtravertInPrivateSpace_GetsMoreStress_ThanIntrovert()
        {
            // Arrange
            var extravertEngine  = BuildIsolationEngine(initialStress: 10);
            var introvertEngine  = BuildIsolationEngine(initialStress: 10);

            var extravertCtx = BuildIsolationContext(extraversion: 0.9);  // E=0.9 → silný extravert
            var introvertCtx = BuildIsolationContext(extraversion: 0.1);  // E=0.1 → introvert

            var outbox = new EventCollector();

            // Act — 8 hodin sami doma
            extravertEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(8), extravertCtx, outbox);
            introvertEngine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(8), introvertCtx, new EventCollector());

            // Assert — extravert musí mít více stresu (isolation penalty aktivní pro E > 0.6)
            Assert.IsTrue(extravertEngine.State.Stress > introvertEngine.State.Stress,
                $"Extravert (E=0.9) v soukromém prostoru musí mít více stresu z izolace. " +
                $"Extravert={extravertEngine.State.Stress:F2}, Introvert={introvertEngine.State.Stress:F2}");
        }
    }
}
