// PhysiologyEngineRegressionTests.cs
// Copyright (c) 50PSoftware
//
// Regresní testy pro tři bugy opravené v DefaultPhysiologyEngine:
//   BUG-1: Eat/Drink způsobovaly dvojí odpočet (Tick + Handle)
//   BUG-2: Energie klesala i během spánku
//   BUG-3: Hlad a žízeň rostly ve spánku stejnou rychlostí jako ve dne

namespace EngineTests
{
    using GameEngineTools;
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
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class PhysiologyEngineRegressionTests : TestBase
    {
        // ── Sdílená výchozí konfigurace ──────────────────────────────────────────────

        private static readonly PhysiologyConfig DefaultCfg = new PhysiologyConfig(
            RestingMetabolicRate: 1600,
            MaxSleepDebtHours: 12,
            EnableMenstrualCycle: false,
            MenstrualCycleBeginsInAge: 12,
            EnergyRecoveryPerSleepHour: 10.0);

        private WDateTime _now;
        private IHumanContext _ctx;
        private EventCollector _outbox;

        [TestInitialize]
        public void Setup()
        {
            _now    = new WDateTime(0);
            _ctx    = BuildContext(currentAction: null);
            _outbox = new EventCollector();
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BUG-1: Dvojí aplikace Eat — Tick() + Handle(ActionCommitted) = 2× efekt
        // ════════════════════════════════════════════════════════════════════════════
        //
        // Scénář: postava má CurrentPlan = "Eat" (snapshot z minulého ticku).
        //   - Tick() sníží hlad o -40/h   (průběžný drift)
        //   - Handle(ActionCommitted Eat) snížil hlad o dalších -40/h  ← byl bug
        //
        // Po opravě: Handle(ActionCommitted) pro Eat/Drink/SelfCare byl odstraněn.
        // Ticket: hlad se mění POUZE v Tick(), ne v Handle().

        [TestMethod]
        public void Tick_WhenCurrentPlanIsEat_HungerDecreaseMatchesSingleApplication()
        {
            // Arrange
            // Kontext říká enginu: "postava aktuálně jí" (CurrentPlan = Eat)
            var ctx    = BuildContext(currentAction: Eat);
            var engine = BuildEngine(hunger: 80, energy: 30);

            var hungerBefore = engine.State.Hunger;
            var tickStep     = WTimeSpan.FromHours(1.0);

            // Act
            engine.Tick(_now, tickStep, ctx, _outbox);

            // Assert
            // Očekávané snížení hladu: -40/h × 1h = -40
            // Tedy Hunger = 80 - 40 = 40
            // Kdyby byl aktivní i Handle, bylo by: 80 - 40 - 40 = 0 (bug)
            var expectedHunger = 80.0 - 40.0;
            Assert.AreEqual(expectedHunger, engine.State.Hunger, delta: 0.01,
                "BUG-1 REGRESSION: Hlad musí klesat pouze jednou (-40/h). " +
                "Dvojí aplikace by dala -80 za hodinu.");
        }

        [TestMethod]
        public void Tick_WhenCurrentPlanIsDrink_ThirstDecreaseMatchesSingleApplication()
        {
            // Arrange
            var ctx    = BuildContext(currentAction: Drink);
            var engine = BuildEngine(thirst: 80);

            var tickStep = WTimeSpan.FromHours(1.0);

            // Act
            engine.Tick(_now, tickStep, ctx, _outbox);

            // Assert
            // Očekávané snížení žízně: -50/h × 1h = -50
            // Tedy Thirst = 80 - 50 = 30
            var expectedThirst = 80.0 - 50.0;
            Assert.AreEqual(expectedThirst, engine.State.Thirst, delta: 0.01,
                "BUG-1 REGRESSION: Žízeň musí klesat pouze jednou (-50/h). " +
                "Dvojí aplikace by dala -100 za hodinu (Thirst = 0 po 48 min).");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BUG-2: Energie klesala i během spánku
        // ════════════════════════════════════════════════════════════════════════════
        //
        // Scénář: postava spí (CurrentPlan = "Sleep").
        //   - Tick() přidal Sleep do switch bez větve → spadl do default (-2/h)
        //   - Za 8h spánku: energie klesla o -16 v Tick(), pak Handle(SleepEnded) přidal +80
        //   - Čistý efekt byl +64 místo správného +80
        //
        // Po opravě: Sleep => 0 ve větvi energyDelta.
        // Energie při spánku v Tick() NEKLESÁ.

        [TestMethod]
        public void Tick_WhenCurrentPlanIsSleep_EnergyDoesNotDecrease()
        {
            // Arrange
            var ctx    = BuildContext(currentAction: Sleep);
            var engine = BuildEngine(energy: 50);

            var energyBefore = engine.State.Energy;
            var tickStep     = WTimeSpan.FromHours(1.0);

            // Act
            engine.Tick(_now, tickStep, ctx, _outbox);

            // Assert
            // Energie nesmí klesnout — obnova se provede až přes Handle(SleepEnded)
            Assert.AreEqual(energyBefore, engine.State.Energy, delta: 0.01,
                "BUG-2 REGRESSION: Energie nesmí klesat během spánku. " +
                "Obnova se aplikuje jednorázově přes Handle(SleepEnded).");
        }

        [TestMethod]
        public void Tick_WhenCurrentPlanIsSleep_EnergyLossIsLowerThanDuringWakefulness()
        {
            // Arrange — dva enginy ve stejném stavu, jeden spí, druhý je bdělý (Idle)
            var ctxSleep = BuildContext(currentAction: Sleep);
            var ctxIdle  = BuildContext(currentAction: Idle);

            var sleepEngine = BuildEngine(energy: 70);
            var idleEngine  = BuildEngine(energy: 70);

            var tickStep = WTimeSpan.FromHours(1.0);

            // Act
            sleepEngine.Tick(_now, tickStep, ctxSleep, _outbox);
            idleEngine.Tick(_now, tickStep, ctxIdle,  _outbox);

            // Assert
            // Spící postava nesmí ztratit více energie než bdělá
            // (ve skutečnosti nesmí ztratit vůbec žádnou)
            Assert.IsTrue(
                sleepEngine.State.Energy >= idleEngine.State.Energy,
                $"BUG-2 REGRESSION: Spánek nesmí snižovat energii více než bdění. " +
                $"Sleep energy={sleepEngine.State.Energy:F1}, Idle energy={idleEngine.State.Energy:F1}");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // BUG-3: Hlad a žízeň rostly ve spánku stejně rychle jako ve dne
        // ════════════════════════════════════════════════════════════════════════════
        //
        // Scénář: postava spí 8h.
        //   - Původní kód: hungerDelta = 6/h pro _jakýkoli_ stav → za 8h = +48 hladu
        //   - thirstDelta = 8/h → za 8h = +64 žízně
        //   - Postava se probudila extrémně hladová, i po plném nočním spánku
        //
        // Po opravě: Sleep => 2/h pro hlad, 2/h pro žízeň (zpomalený metabolismus).
        // Za 8h: hlad +16 (místo +48), žízeň +16 (místo +64).

        [TestMethod]
        public void Tick_WhenCurrentPlanIsSleep_HungerGrowsSlowerThanWhileAwake()
        {
            // Arrange
            var ctxSleep = BuildContext(currentAction: Sleep);
            var ctxIdle  = BuildContext(currentAction: Idle);

            var sleepEngine = BuildEngine(hunger: 20);
            var idleEngine  = BuildEngine(hunger: 20);

            var tickStep = WTimeSpan.FromHours(1.0);

            // Act
            sleepEngine.Tick(_now, tickStep, ctxSleep, _outbox);
            idleEngine.Tick(_now, tickStep, ctxIdle,  _outbox);

            // Assert
            // Hlad spící postavy musí růst pomaleji než bdělé
            Assert.IsTrue(
                sleepEngine.State.Hunger < idleEngine.State.Hunger,
                $"BUG-3 REGRESSION: Hlad ve spánku musí růst pomaleji než při bdění. " +
                $"Sleep hunger={sleepEngine.State.Hunger:F1}, Idle hunger={idleEngine.State.Hunger:F1}");
        }

        [TestMethod]
        public void Tick_WhenCurrentPlanIsSleep_ThirstGrowsSlowerThanWhileAwake()
        {
            // Arrange
            var ctxSleep = BuildContext(currentAction: Sleep);
            var ctxIdle  = BuildContext(currentAction: Idle);

            var sleepEngine = BuildEngine(thirst: 20);
            var idleEngine  = BuildEngine(thirst: 20);

            var tickStep = WTimeSpan.FromHours(1.0);

            // Act
            sleepEngine.Tick(_now, tickStep, ctxSleep, _outbox);
            idleEngine.Tick(_now, tickStep, ctxIdle,  _outbox);

            // Assert
            Assert.IsTrue(
                sleepEngine.State.Thirst < idleEngine.State.Thirst,
                $"BUG-3 REGRESSION: Žízeň ve spánku musí růst pomaleji než při bdění. " +
                $"Sleep thirst={sleepEngine.State.Thirst:F1}, Idle thirst={idleEngine.State.Thirst:F1}");
        }

        [TestMethod]
        public void Tick_WhenSleeping8Hours_HungerGrowsByExpectedAmount()
        {
            // Arrange — ověření konkrétní hodnoty po 8h spánku
            var ctx    = BuildContext(currentAction: Sleep);
            var engine = BuildEngine(hunger: 10);

            // Act — simulujeme 8 ticků po 1 hodině
            for (int i = 0; i < 8; i++)
                engine.Tick(_now, WTimeSpan.FromHours(1.0), ctx, _outbox);

            // Assert
            // Zpomalený metabolismus: 2/h × 8h = +16
            // Hlad by byl: 10 + 16 = 26
            // Bez opravy by byl: 10 + 48 = 58 (a postava by se hned probudila a šla jíst)
            Assert.AreEqual(26.0, engine.State.Hunger, delta: 0.5,
                "BUG-3 REGRESSION: Po 8h spánku musí hlad vzrůst pouze o 16 (2/h), ne o 48 (6/h).");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // Pomocné metody
        // ════════════════════════════════════════════════════════════════════════════

        #region Helpers

        /// <summary>
        /// Sestaví engine s nastavenými počátečními hodnotami.
        /// Menstruační cyklus je vždy vypnutý — testy ho nepotřebují.
        /// </summary>
        private static DefaultPhysiologyEngine BuildEngine(
            double energy     = 70,
            double hunger     = 25,
            double thirst     = 20,
            double pain       = 5,
            double immuneLoad = 10)
        {
            var cfg      = Options.Create(DefaultCfg);
            var cycleCfg = Options.Create(new MenstrualCycleConfig());
            var factory  = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rng      = new ZeroRandom();

            var engine = new DefaultPhysiologyEngine(
                cfg, cycleCfg, factory, rng,
                biology:   SexBiology.Female,
                birthDate: WDateOnly.New(100, 1, 1),
                now:       WDateOnly.New(116, 1, 1));

            engine.RestoreState(new PhysiologyState(
                Energy:        energy,
                SleepDebtHours: 2,
                Hunger:        hunger,
                Thirst:        thirst,
                Pain:          pain,
                ImmuneLoad:    immuneLoad,
                BodyTempDelta: 0,
                Cycle:         null));

            return engine;
        }

        /// <summary>
        /// Sestaví minimální fake kontext s nastavitelnou aktuální akcí postavy.
        /// PhysiologyEngine čte pouze <c>ctx.Snapshot.Behavior.CurrentPlan?.Name</c>.
        /// </summary>
        /// <param name="currentAction">
        /// Název akce která je právě prováděna, nebo <c>null</c> pokud postava nic nedělá.
        /// Použij konstanty z <see cref="ActionNames"/> (Sleep, Eat, Drink, SelfCare, Idle...).
        /// </param>
        private static IHumanContext BuildContext(string? currentAction)
        {
            var physio = new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null);
            var psych  = new PsychologyState(0.1, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral);

            // Aktuální plán je to jediné, na co se PhysiologyEngine v kontextu dívá
            var plan = currentAction is not null
                ? new PlannedAction(currentAction, new WDateTime(0), WTimeSpan.FromHours(1), 50)
                : null;

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(40, 20, 15, 40, 50, 30, plan),
                new InteractionSurface(null, false, double.NaN, double.NaN),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new List<EpisodicMemory>(),
                    new Dictionary<string, SemanticFact>()));

            return new HumanContext
            {
                Id       = new HumanId(Guid.NewGuid()),
                Biology  = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    AttachmentStyle.Secure,
                    CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate,
                    Chronotype.Neutral),
                Snapshot = snapshot
            };
        }

        #endregion Helpers
    }
}
