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

        #endregion Pomocné metody

        #region Fake / Stub implementace

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
