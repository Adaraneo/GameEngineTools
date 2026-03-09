// SleepTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
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
    using GameTester;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Unit testy spánkového subsystému.
    /// Testuje <see cref="DefaultBehaviorEngine"/> a <see cref="DefaultSleepSession"/>
    /// izolovaně — bez plného DI stacku, s fake/stub objekty.
    /// </summary>
    /// <remarks>
    /// Klíčový princip: herní čas je řízen ručně přes <c>WDateTime</c> hodnoty.
    /// Žádné reálné čekání, žádný Timer — testy jsou vždy deterministické.
    /// </remarks>
    [TestClass]
    public class SleepTests : TestBase
    {
        #region Soukromá pole

        private IHumanContext _ctx = default!;
        private IEventCollector _outbox = default!;
        private WDateTime _now;

        // Výchozí konfigurace pro testy — nízký threshold pro snadné spouštění promptu
        private static readonly SleepConfig DefaultSleepCfg = new SleepConfig() with
        {
            SleepPromptThreshold        = 70.0,
            SleepGraceHours             = 4.0,
            MaxDeclineCount             = 3,
            DeclinePenaltyStressPerHour = 2.0,
            FallingDurationHours        = 0.25,
            LightDurationHours          = 0.75,
            DeepDurationHours           = 2.5,
            RemDurationHours            = 1.5,
            AmbushBaseChancePerHour     = 0.0,   // vypnuto — nechceme náhodné přerušení v testech
            CompanionGuardModifier      = 0.4,
            NightmareChanceHighStress   = 0.0,   // vypnuto — deterministické testy
            NightmareChanceNormal       = 0.0
        };

        private static readonly BehaviorConfig DefaultBehaviorCfg = new BehaviorConfig();

        #endregion

        #region Setup

        /// <summary>
        /// Připraví fake kontext a outbox před každým testem.
        /// Herní čas začíná na čase 0 (začátek světa).
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            _now    = new WDateTime(0);
            _outbox = new EventCollector();
            _ctx    = BuildFakeContext(sleepDebtHours: 0, stress: 0, energy: 70);
        }

        #endregion

        #region BehaviorEngine — Sleep Prompt

        /// <summary>
        /// Ověřuje, že BehaviorEngine vyšle <see cref="SleepEvents.SleepPromptRequested"/>
        /// když NeedRest překročí threshold.
        /// </summary>
        [TestMethod]
        public void Tick_WhenNeedRestAboveThreshold_PublishesSleepPromptRequested()
        {
            // Arrange — vysoký spánkový dluh → NeedRest bude > 70
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            // Act
            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<SleepPromptRequested>().Any(),
                "Očekáván event SleepPromptRequested při vysokém NeedRest.");
        }

        /// <summary>
        /// Ověřuje, že BehaviorEngine NEVYŠLE sleep prompt když NeedRest je pod threshold.
        /// </summary>
        [TestMethod]
        public void Tick_WhenNeedRestBelowThreshold_DoesNotPublishSleepPrompt()
        {
            // Arrange — malý spánkový dluh → NeedRest pod 70
            _ctx    = BuildFakeContext(sleepDebtHours: 0, stress: 0, energy: 90);
            var engine = BuildBehaviorEngine();

            // Act
            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsFalse(
                events.OfType<SleepPromptRequested>().Any(),
                "Sleep prompt nesmí být vyslán při nízkém NeedRest.");
        }

        /// <summary>
        /// Ověřuje, že engine čeká na odpověď hráče a neopakuje prompt v každém ticku.
        /// </summary>
        [TestMethod]
        public void Tick_WhenWaitingForConfirmation_DoesNotRepeatPrompt()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            // Act — první tick vyšle prompt
            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain(); // vyčistit

            // Act — druhý tick: engine čeká, nesmí opakovat
            engine.Tick(_now + WTimeSpan.FromMinutes(10), WTimeSpan.FromMinutes(10), _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsFalse(
                events.OfType<SleepPromptRequested>().Any(),
                "Engine nesmí opakovat sleep prompt — čeká na odpověď hráče.");
        }

        #endregion

        #region BehaviorEngine — Sleep Confirmed

        /// <summary>
        /// Ověřuje, že po <see cref="SleepEvents.SleepConfirmed"/> engine přestane
        /// generovat akce z candidates a session se spustí (<see cref="SleepEvents.SleepPhaseChanged"/>).
        /// </summary>
        [TestMethod]
        public void Handle_SleepConfirmed_StartsSessionAndPublishesPhaseChanged()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            // Vyslat prompt
            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain();

            // Act — hráč potvrdil
            var confirmed = new SleepConfirmed(
                OccurredAt:    _now,
                Human:         _ctx.Id,
                PlannedWakeUp: _now + WTimeSpan.FromHours(8));

            engine.Handle(confirmed, _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<SleepPhaseChanged>().Any(),
                "Po SleepConfirmed očekáváme SleepPhaseChanged (fáze Falling).");

            // Stav — WaitingForSleepConfirmation musí být false
            Assert.IsFalse(engine.State.WaitingForSleepConfirmation,
                "Po potvrzení nesmí být engine ve stavu čekání.");

            // DeclineCount musí být resetován
            Assert.AreEqual(0, engine.State.SleepDeclineCount,
                "DeclineCount se resetuje po zahájení spánku.");
        }

        /// <summary>
        /// Ověřuje, že po <see cref="SleepEvents.SleepConfirmed"/> engine v dalších tickách
        /// nevybírá nové akce z candidates (spánek má přednost).
        /// </summary>
        [TestMethod]
        public void Tick_DuringSleep_DoesNotSelectNewAction()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain();

            var confirmed = new SleepConfirmed(
                OccurredAt:    _now,
                Human:         _ctx.Id,
                PlannedWakeUp: _now + WTimeSpan.FromHours(8));
            engine.Handle(confirmed, _ctx, _outbox);
            _outbox.Drain();

            // Act — tick během spánku
            engine.Tick(_now + WTimeSpan.FromHours(1), WTimeSpan.FromHours(1), _ctx, _outbox);

            // Assert — nesmí být ActionCommitted pro jinou akci než Sleep
            var events = _outbox.Drain();
            var nonSleepActions = events
                .OfType<ActionCommitted>()
                .Where(e => e.ActionName != ActionNames.Sleep);

            Assert.IsFalse(nonSleepActions.Any(),
                "Během aktivní sleep session nesmí být vybírána jiná akce.");
        }

        #endregion

        #region BehaviorEngine — Sleep Declined

        /// <summary>
        /// Ověřuje, že po odmítnutí spánku engine nastaví grace periodu
        /// a počítadlo odmítnutí se zvýší.
        /// </summary>
        [TestMethod]
        public void Handle_SleepDeclined_SetsGracePeriodAndIncrementsDeclineCount()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain();

            // Act — hráč odmítl
            var declined = new SleepDeclined(_now, _ctx.Id, DeclineCount: 1);
            engine.Handle(declined, _ctx, _outbox);

            // Assert
            Assert.IsFalse(engine.State.WaitingForSleepConfirmation,
                "Po odmítnutí nesmí být engine ve stavu čekání.");
            Assert.AreEqual(1, engine.State.SleepDeclineCount,
                "DeclineCount musí být 1 po prvním odmítnutí.");
            Assert.IsNotNull(engine.State.SleepGraceExpiresAt,
                "Grace perioda musí být nastavena po odmítnutí.");
        }

        /// <summary>
        /// Ověřuje, že každé odmítnutí zkracuje grace periodu.
        /// </summary>
        [TestMethod]
        public void Handle_SleepDeclinedMultipleTimes_GracePeriodShortensWith_EachDecline()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            // Act — první odmítnutí
            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain();
            engine.Handle(new SleepDeclined(_now, _ctx.Id, 1), _ctx, _outbox);
            var firstGrace = engine.State.SleepGraceExpiresAt!.Value;

            // Act — druhé odmítnutí (simulujeme nový prompt po expiraci)
            var afterGrace = firstGrace + WTimeSpan.FromMinutes(1);
            engine.Handle(new SleepDeclined(afterGrace, _ctx.Id, 2), _ctx, _outbox);

            // Pozn: engine.State.SleepDeclineCount je nyní 2
            // Grace expiry druhého odmítnutí musí být kratší než první
            var secondGrace = engine.State.SleepGraceExpiresAt!.Value;
            var firstInterval  = (firstGrace  - _now).TotalHours;
            var secondInterval = (secondGrace - afterGrace).TotalHours;

            Assert.IsTrue(secondInterval < firstInterval,
                $"Grace perioda se musí zkracovat: první={firstInterval:F2}h, druhá={secondInterval:F2}h");
        }

        /// <summary>
        /// Ověřuje, že po expiraci grace periody engine znovu vyšle sleep prompt.
        /// </summary>
        [TestMethod]
        public void Tick_AfterGraceExpires_RepeatsSleepPrompt()
        {
            // Arrange
            _ctx    = BuildFakeContext(sleepDebtHours: 9, stress: 0, energy: 20);
            var engine = BuildBehaviorEngine();

            engine.Tick(_now, WTimeSpan.FromHours(1), _ctx, _outbox);
            _outbox.Drain();
            engine.Handle(new SleepDeclined(_now, _ctx.Id, 1), _ctx, _outbox);
            _outbox.Drain();

            // Act — tick po expiraci grace (grace = 4h / 1 = 4h → tick za 5h)
            var afterGrace = _now + WTimeSpan.FromHours(5);
            engine.Tick(afterGrace, WTimeSpan.FromHours(1), _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(
                events.OfType<SleepPromptRequested>().Any(),
                "Po expiraci grace periody musí být vyslán nový sleep prompt.");
        }

        #endregion

        #region SleepSession — fáze a přirozené probuzení

        /// <summary>
        /// Ověřuje, že session prochází fázemi ve správném pořadí:
        /// Falling → Light → Deep → REM.
        /// </summary>
        [TestMethod]
        public void Session_Phases_ProgressInCorrectOrder()
        {
            // Arrange
            var session = BuildSession();
            var plannedWakeUp = _now + WTimeSpan.FromHours(10);
            session.Begin(_now, plannedWakeUp, _ctx, _outbox);
            _outbox.Drain();

            var phases = new List<SleepPhase>();

            // Act — postupujeme po malých krocích, sbíráme přechody fází
            var time = _now;
            for (int i = 0; i < 200 && session.IsActive; i++)
            {
                var dt = WTimeSpan.FromMinutes(15);
                time += dt;
                session.Tick(time, dt, _ctx, _outbox);

                var phaseEvents = _outbox.Drain().OfType<SleepPhaseChanged>().ToList();
                phases.AddRange(phaseEvents.Select(e => e.Phase));
            }

            // Assert — první fáze musí být Falling, pak Light, pak Deep, pak REM
            Assert.IsTrue(phases.Count >= 3,
                $"Očekáváme alespoň 3 přechody fází, získáno: {phases.Count}");
            Assert.AreEqual(SleepPhase.Falling, phases[0], "První fáze musí být Falling.");
            Assert.AreEqual(SleepPhase.Light,   phases[1], "Druhá fáze musí být Light.");
            Assert.AreEqual(SleepPhase.Deep,     phases[2], "Třetí fáze musí být Deep.");
        }

        /// <summary>
        /// Ověřuje, že session se přirozeně ukončí po uplynutí plánované doby
        /// a vyšle <see cref="SleepEvents.SleepEnded"/> s <c>WasInterrupted = false</c>.
        /// </summary>
        [TestMethod]
        public void Session_AfterPlannedWakeUp_PublishesSleepEndedNotInterrupted()
        {
            // Arrange
            var session       = BuildSession();
            var plannedWakeUp = _now + WTimeSpan.FromHours(8);
            session.Begin(_now, plannedWakeUp, _ctx, _outbox);
            _outbox.Drain();

            // Act — jeden velký tick za plánovaný konec
            var afterWakeUp = plannedWakeUp + WTimeSpan.FromMinutes(1);
            session.Tick(afterWakeUp, WTimeSpan.FromHours(9), _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            var ended  = events.OfType<SleepEnded>().FirstOrDefault();

            Assert.IsNotNull(ended, "Očekáván event SleepEnded.");
            Assert.IsFalse(ended.WasInterrupted, "Přirozené probuzení nesmí mít WasInterrupted = true.");
            Assert.IsFalse(session.IsActive, "Session musí být neaktivní po přirozeném probuzení.");
        }

        /// <summary>
        /// Ověřuje, že kvalita spánku po plném dospání je vyšší než po přerušení.
        /// </summary>
        [TestMethod]
        public void Session_SleepQuality_IsHigherForUninterruptedSleep()
        {
            // Arrange — plný spánek
            var fullSession   = BuildSession();
            var fullOutbox    = new EventCollector();
            var plannedWakeUp = _now + WTimeSpan.FromHours(8);
            fullSession.Begin(_now, plannedWakeUp, _ctx, fullOutbox);
            fullOutbox.Drain();

            var afterWakeUp = plannedWakeUp + WTimeSpan.FromMinutes(1);
            fullSession.Tick(afterWakeUp, WTimeSpan.FromHours(9), _ctx, fullOutbox);
            var fullQuality = fullOutbox.Drain()
                .OfType<SleepEnded>()
                .First().Quality;

            // Arrange — přerušený spánek (po 30 minutách)
            var interruptedSession = BuildSession();
            var intOutbox          = new EventCollector();
            interruptedSession.Begin(_now, plannedWakeUp, _ctx, intOutbox);
            intOutbox.Drain();

            var interruptTime = _now + WTimeSpan.FromMinutes(30);
            interruptedSession.Tick(interruptTime, WTimeSpan.FromMinutes(30), _ctx, intOutbox);
            intOutbox.Drain();
            interruptedSession.Interrupt(interruptTime, InterruptCause.Ambush, _ctx, intOutbox);
            var interruptedQuality = intOutbox.Drain()
                .OfType<SleepEnded>()
                .First().Quality;

            // Assert
            Assert.IsTrue(fullQuality > interruptedQuality,
                $"Plný spánek musí mít vyšší kvalitu než přerušený. Plný={fullQuality:F1}, Přerušený={interruptedQuality:F1}");
        }

        #endregion

        #region SleepSession — přerušení

        /// <summary>
        /// Ověřuje, že <see cref="ISleepSession.Interrupt"/> vyšle
        /// <see cref="SleepEvents.SleepInterrupted"/> a <see cref="SleepEvents.SleepEnded"/>.
        /// </summary>
        [TestMethod]
        public void Session_Interrupt_PublishesBothInterruptedAndEnded()
        {
            // Arrange
            var session       = BuildSession();
            var plannedWakeUp = _now + WTimeSpan.FromHours(8);
            session.Begin(_now, plannedWakeUp, _ctx, _outbox);
            _outbox.Drain();

            // Act
            session.Interrupt(_now + WTimeSpan.FromHours(1), InterruptCause.Ambush, _ctx, _outbox);

            // Assert
            var events = _outbox.Drain();
            Assert.IsTrue(events.OfType<SleepInterrupted>().Any(),
                "Očekáván event SleepInterrupted.");
            Assert.IsTrue(events.OfType<SleepEnded>().Any(),
                "Očekáván event SleepEnded po přerušení.");
            Assert.IsFalse(session.IsActive,
                "Session musí být neaktivní po přerušení.");
        }

        /// <summary>
        /// Ověřuje, že přerušení po skončení session (IsActive = false) je ignorováno.
        /// </summary>
        [TestMethod]
        public void Session_InterruptAfterEnded_IsIgnored()
        {
            // Arrange — session nejdříve přirozeně skončí
            var session       = BuildSession();
            var plannedWakeUp = _now + WTimeSpan.FromHours(8);
            session.Begin(_now, plannedWakeUp, _ctx, _outbox);
            _outbox.Drain();

            session.Tick(plannedWakeUp + WTimeSpan.FromMinutes(1), WTimeSpan.FromHours(9), _ctx, _outbox);
            _outbox.Drain(); // vyčistit SleepEnded

            // Act — pokus o druhé přerušení
            session.Interrupt(_now + WTimeSpan.FromHours(10), InterruptCause.Ambush, _ctx, _outbox);

            // Assert — žádný nový event
            var events = _outbox.Drain();
            Assert.IsFalse(events.Any(),
                "Přerušení ukončené session nesmí vyslat žádné eventy.");
        }

        #endregion

        #region SleepSession — sdílený spánek

        /// <summary>
        /// Ověřuje, že zahájení sdíleného spánku vyšle <see cref="SleepEvents.SharedSleepBegan"/>.
        /// </summary>
        [TestMethod]
        public void Session_WithCompanion_PublishesSharedSleepBegan()
        {
            // Arrange
            var session       = BuildSession();
            var companionId   = new HumanId(Guid.NewGuid());
            var plannedWakeUp = _now + WTimeSpan.FromHours(8);

            // Act
            session.Begin(_now, plannedWakeUp, _ctx, _outbox,
                companion:   companionId,
                sharedType:  SharedSleepType.Camp);

            // Assert
            var events = _outbox.Drain();
            var shared = events.OfType<SharedSleepBegan>().FirstOrDefault();

            Assert.IsNotNull(shared, "Očekáván event SharedSleepBegan.");
            Assert.AreEqual(companionId,           shared.Companion, "Companion ID musí sedět.");
            Assert.AreEqual(SharedSleepType.Camp,  shared.Type,      "Typ sdíleného spánku musí být Camp.");
        }

        #endregion

        #region Pomocné metody (factory)

        /// <summary>
        /// Sestaví <see cref="DefaultBehaviorEngine"/> s testovací konfigurací.
        /// </summary>
        private DefaultBehaviorEngine BuildBehaviorEngine()
        {
            var behavOpts = Options.Create(DefaultBehaviorCfg);
            var sleepOpts = Options.Create(DefaultSleepCfg);
            var logFactory = BuildLoggerFactory();

            return new DefaultBehaviorEngine(behavOpts, sleepOpts, logFactory);
        }

        /// <summary>
        /// Sestaví <see cref="DefaultSleepSession"/> s testovací konfigurací.
        /// Ambush a Nightmare jsou vypnuty pro deterministické testy.
        /// </summary>
        private DefaultSleepSession BuildSession()
        {
            return new DefaultSleepSession(
                DefaultSleepCfg,
                BuildLoggerFactory(),
                new AlwaysFalseRandom());
        }

        /// <summary>
        /// Sestaví fake <see cref="IHumanContext"/> s nastavenými fyziologickými hodnotami.
        /// </summary>
        /// <param name="sleepDebtHours">Spánkový dluh — ovlivňuje NeedRest výpočet.</param>
        /// <param name="stress">Aktuální stres postavy.</param>
        /// <param name="energy">Aktuální energie postavy.</param>
        private static IHumanContext BuildFakeContext(double sleepDebtHours, double stress, double energy)
        {
            var physio = new PhysiologyState(
                Energy:          energy,
                SleepDebtHours:  sleepDebtHours,
                Hunger:          20,
                Thirst:          15,
                Pain:            0,
                ImmuneLoad:      5,
                BodyTempDelta:   0,
                Cycle:           null);

            var psych = new PsychologyState(
                Valence:          0.1,
                Arousal:          0.4,
                Dominance:        0.5,
                Stress:           stress,
                CognitiveLoad:    10,
                DominantEmotion:  DiscreteEmotion.Neutral);

            var behavior = new BehaviorState(
                NeedRest: 40, NeedFood: 20, NeedWater: 15,
                NeedBelonging: 40, NeedCompetence: 50, NeedIntimacy: 30,
                CurrentPlan: null);

            var relationships = new RelationshipState(
                new Dictionary<HumanId, RelationshipEdge>());

            var memory = new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>());

            var snapshot = new EnginesSnapshot(
                physio, psych, behavior,
                new InteractionSurface(null, false, double.NaN, double.NaN),
                relationships,
                memory);

            var personality = new Personality(
                BigFive:       new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment:    AttachmentStyle.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation:    new MotivationWeights(
                    Affiliation: 0.5, Achievement: 0.5, Power: 0.3,
                    Altruism: 0.4, Competence: 0.5, Autonomy: 0.5,
                    Curiosity: 0.5, Rest: 0.6, Sexuality: 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype:     Chronotype.Neutral);

            return new HumanContext
            {
                Id          = new HumanId(Guid.NewGuid()),
                Biology     = SexBiology.Female,
                Personality = personality,
                Snapshot    = snapshot,
                Random      = new AlwaysFalseRandom(),
                Logger      = BuildLoggerFactory().CreateLogger("Test"),
                EventBus    = new NullEventBus(),
                Scheduler   = new NullScheduler()
            };
        }

        private static ILoggerFactory BuildLoggerFactory()
            => LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        #endregion

        #region Fake / Stub implementace

        /// <summary>
        /// IRandomSource který vždy vrátí false pro Chance() — vypíná náhodné eventy.
        /// Použit v testech kde nechceme přepad ani noční můru.
        /// </summary>
        private sealed class AlwaysFalseRandom : IRandomSource
        {
            public int    Next(int min, int max) => min;
            public double NextUnit()             => 0.0;
            public bool   Chance(double p)       => false; // nikdy nenastane náhodný event
        }

        /// <summary>
        /// IEventBus který nic nepublikuje — izolace od globálního event systému.
        /// </summary>
        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event) { }
            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
            public IDisposable SubscribeAll(Action<IDomainEvent> handler)
                => new NullDisposable();
        }

        /// <summary>IScheduler který nic neplánuje.</summary>
        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());
            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());
            public bool Cancel(ScheduledId id) => true;
            public IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }

        #endregion
    }
}
