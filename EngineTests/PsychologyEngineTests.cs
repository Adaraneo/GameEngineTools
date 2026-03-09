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
    using GameTester;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
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

        #region Pomocné metody

        /// <summary>Sestaví engine s výchozí nebo vlastní konfigurací.</summary>
        private static DefaultPsychologyEngine BuildEngine(
            double initialValence,
            double initialStress,
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
                CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral));

            return engine;
        }

        /// <summary>Sestaví fake kontext s nastaveným Neuroticism.</summary>
        private static IHumanContext BuildContext(double neuroticism)
        {
            var physio = new PhysiologyState(
                Energy: 70, SleepDebtHours: 0, Hunger: 20, Thirst: 15,
                Pain: 0, ImmuneLoad: 5, BodyTempDelta: 0, Cycle: null);

            var psych = new PsychologyState(
                Valence: 0.1, Arousal: 0.4, Dominance: 0.5,
                Stress: 20, CognitiveLoad: 10, DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new List<EpisodicMemory>(),
                    new Dictionary<string, SemanticFact>()));

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

        /// <summary>Vytvoří <see cref="SleepEnded"/> s danými parametry.</summary>
        private SleepEnded MakeSleepEnded(double quality, double hoursSlept, bool wasInterrupted)
            => new SleepEnded(
                OccurredAt: _now,
                Human: new HumanId(Guid.NewGuid()),
                TotalHoursSlept: hoursSlept,
                Quality: quality,
                WasInterrupted: wasInterrupted);

        #endregion Pomocné metody

        #region Fake / Stub implementace

        /// <summary>
        /// IRandomSource vracející vždy 0 — eliminuje náhodný šum v Tick().
        /// Bez toho by <c>BaselineAffectVariance</c> způsobovala nedeterministické výsledky.
        /// </summary>
        private sealed class ZeroRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => false;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();

            public IDisposable SubscribeAll(Action<IDomainEvent> handler)
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
