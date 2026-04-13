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
            SleepQualityAffectWeight: 0.5);

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
            var nightmare = new NightmareTriggered(_now, ctx.Id, StressAtSleepStart: 40);
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
            var nightmare = new NightmareTriggered(_now, ctx.Id, StressAtSleepStart: 30);
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
            var nightmare = new NightmareTriggered(_now, new HumanId(Guid.NewGuid()), StressAtSleepStart: 50);

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
            var nightmare = new NightmareTriggered(_now, ctx.Id, StressAtSleepStart: 20);

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
            engine.Tick(_now, WTimeSpan.FromHours(1.0), ctx, _outbox);

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
            engine.Tick(_now, WTimeSpan.FromHours(1.0), ctx, _outbox);

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
            engine.Tick(_now, WTimeSpan.FromHours(1.0), ctx, _outbox);

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
            sleepEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxSleep, new EventCollector());
            idleEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxIdle, new EventCollector());

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
            feverEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxFever, new EventCollector());
            normalEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

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
            feverEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxFever, new EventCollector());
            normalEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

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
            subEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxSub, new EventCollector());
            normalEngine.Tick(_now, WTimeSpan.FromHours(1.0), ctxNormal, new EventCollector());

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
            var evt = new PregnancyDiscovered(_now, ctx.Id, new HumanId(Guid.NewGuid()));

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

            var stableEvt  = new PregnancyDiscovered(_now, stableCtx.Id, new HumanId(Guid.NewGuid()));
            var neurotiEvt = new PregnancyDiscovered(_now, neurotiCtx.Id, new HumanId(Guid.NewGuid()));

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
            var evt = new PregnancyDiscovered(_now, ctx.Id, new HumanId(Guid.NewGuid()));

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
            var evt = new ChildBorn(_now, ctx.Id, new HumanId(Guid.NewGuid()));

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
            var evt = new ChildBorn(_now, ctx.Id, new HumanId(Guid.NewGuid()));

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
            var evt = new ChildBorn(_now, ctx.Id, new HumanId(Guid.NewGuid()));

            // Act
            engine.Handle(evt, ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Stress < stressBefore,
                $"ChildBorn musí snížit stres. Před: {stressBefore:F1}, po: {engine.State.Stress:F1}");
        }

        #endregion Pregnancy events — testy

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
                OccurredAt: _now,
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
                OccurredAt: _now,
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
            var ioSmall = new InteractionOutcome(_now, ctx.Id, toId, false, string.Empty, SpeechAct.SmallTalk);
            var ioSelf  = new InteractionOutcome(_now, ctx.Id, toId, false, string.Empty, SpeechAct.SelfDisclosure);

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
                OccurredAt: _now,
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
            engineHighPain.Tick(_now, WTimeSpan.FromHours(1.0), ctxHighPain, new EventCollector());
            engineNoPain.Tick(_now, WTimeSpan.FromHours(1.0), ctxNoPain, new EventCollector());

            // Assert — Pain=50 → -0.0005 * 50 * 1h = -0.025 extra oproti žádné bolesti
            Assert.IsTrue(
                engineHighPain.State.Dominance < engineNoPain.State.Dominance,
                $"Vysoká bolest musí snižovat Dominance. HighPain={engineHighPain.State.Dominance:F4}, NoPain={engineNoPain.State.Dominance:F4}");
        }

        #endregion Dominance — testy

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
                Attachment: AttachmentStyle.Secure,
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

        /// <summary>Vytvoří <see cref="SleepEnded"/> s danými parametry.</summary>
        private SleepEnded MakeSleepEnded(double quality, double hoursSlept, bool wasInterrupted)
            => new SleepEnded(
                OccurredAt: _now,
                Human: new HumanId(Guid.NewGuid()),
                TotalHoursSlept: hoursSlept,
                Quality: quality,
                WasInterrupted: wasInterrupted);

        #endregion Pomocné metody
    }
}
