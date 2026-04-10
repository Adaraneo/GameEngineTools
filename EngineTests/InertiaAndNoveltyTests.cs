// InertiaAndNoveltyTests.cs
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
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Unit testy setrvačnosti (InertiaWeight) a novinové penalizace (NoveltyPenalty).
    ///
    /// Testovací strategie — izolace efektů:
    /// Inertia a NoveltyPenalty jsou nezávislé mechanismy. Aby každý test ověřoval
    /// právě jeden z nich, používáme <c>BehaviorConfig with { InertiaWeight = 0.0 }</c>
    /// při testech penalizace a naopak. Tím se vyhneme situaci kde výsledek ovlivní
    /// oba mechanismy najednou a nevíme proč test prošel nebo selhal.
    ///
    /// Analytická kalibrace utility (vzorce enginu):
    /// <code>
    /// Util(need, weight)  = need * (0.5 + weight)
    /// needBel             = 70 - MeanCloseness(prázdné=50) + max(0, -Valence*15)
    /// needComp            = 50 + (Competence - 0.5) * 80 - Stress * 0.2
    /// needSelfCare        = Pain*0.7 + ImmuneLoad*0.3
    /// </code>
    /// </summary>
    [TestClass]
    public class InertiaAndNoveltyTests : TestBase
    {
        #region Soukromá pole

        /// <summary>
        /// Velká hodnota herního času — zaručuje, že jakýkoli CurrentPlan je vždy prošlý.
        /// elapsed = _now - plan.Start = 1_000_000 - 0 >> jakákoli délka plánu.
        /// </summary>
        private static readonly WDateTime FarFuture = new WDateTime(WTimeSpan.FromDays(2).Ticks);

        /// <summary>Základní konfigurace — výchozí InertiaWeight=0.25, NoveltyPenalty=0.1.</summary>
        private static readonly BehaviorConfig DefaultCfg = new BehaviorConfig();

        /// <summary>
        /// Konfigurace s vypnutou setrvačností — pro izolaci NoveltyPenalty.
        /// Bez InertiaWeight boost žádná akce nedostane výhodu jen proto,
        /// že ji postava dělala předtím — čistý test penalizace.
        /// </summary>
        private static readonly BehaviorConfig NoInertiaCfg =
            new BehaviorConfig() with { InertiaWeight = 0.0 };

        /// <summary>Sleep threshold=999 — Tick() nikdy nezablokuje v sleep-prompt čekání.</summary>
        private static readonly SleepConfig NoSleepCfg = new SleepConfig() with
        {
            SleepPromptThreshold = 999.0
        };

        #endregion Soukromá pole

        // ====================================================================
        // TEST 1 — Setrvačnost: opakování stejné produktivní akce dostane boost
        // ====================================================================
        //
        // Kalibrace (Competence=0.5, Curiosity=0.6, Stress=0, prázdné vztahy):
        //
        //   needComp = 50 + (0.5-0.5)*80 = 50
        //   Work   = 50 * (0.5+0.5) = 50.0
        //   Create = 50 * (0.5+0.6) = 55.0   ← bez inertia vyhraje Create
        //
        //   S CurrentPlan=Work a InertiaWeight=0.25:
        //   Work   = 50 * 1.25 = 62.5         ← s inertia vyhraje Work
        //   Create = 55.0 (žádná změna)
        //
        [TestMethod]
        public void SelectAction_WithExpiredWorkPlan_BoostsWorkUtilityAndWinsOverCreate()
        {
            // Arrange — dvě identické postavy, liší se jen přítomností prošlého plánu
            var ctxNoPlan = BuildContext(competence: 0.5, curiosity: 0.6);
            var ctxWithWorkPlan = BuildContext(competence: 0.5, curiosity: 0.6);

            // Engine bez plánu — Create=55 vyhraje bez inertia
            var engineNoPlan = BuildEngine(DefaultCfg);

            // Engine s prošlým Work plánem — Work dostane +25% boost
            var engineWithPlan = BuildEngine(DefaultCfg);
            engineWithPlan.RestoreState(
                engineWithPlan.State with
                {
                    CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0)
                });

            var outboxNoPlan = new EventCollector();
            var outboxWithPlan = new EventCollector();

            // Act
            engineNoPlan.Tick(FarFuture, WTimeSpan.FromHours(1), ctxNoPlan, outboxNoPlan);
            engineWithPlan.Tick(FarFuture, WTimeSpan.FromHours(1), ctxWithWorkPlan, outboxWithPlan);

            // Assert
            var chosenNoPlan = outboxNoPlan.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenWithPlan = outboxWithPlan.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenNoPlan, "Engine bez plánu musí vybrat akci.");
            Assert.IsNotNull(chosenWithPlan, "Engine s prošlým plánem musí vybrat akci.");

            // Bez inertia: Create=55 > Work=50 → Create vítězí
            Assert.AreEqual(Create, chosenNoPlan.ActionName,
                $"Bez inertia Create=55 musí porazit Work=50. Zvoleno: {chosenNoPlan.ActionName}");

            // S inertia: Work=62.5 > Create=55 → Work vítězí
            Assert.AreEqual(Work, chosenWithPlan.ActionName,
                $"S inertia Work=62.5 musí porazit Create=55. Zvoleno: {chosenWithPlan.ActionName}");
        }

        // ====================================================================
        // TEST 2 — NoveltyPenalty: přepnutí do jiné kategorie je penalizováno
        // ====================================================================
        //
        // Izolace: InertiaWeight=0.0 → Work nedostane boost, změnu způsobí POUZE penalizace.
        //
        // Kalibrace (Competence=0.5, Affiliation=1.0, Valence=-1.0, Stress=0):
        //
        //   needBel  = 70 - 50 + max(0, 1.0*15) = 35
        //   ReachOut = 35 * (0.5+1.0) = 52.5     ← bez penalty vyhraje ReachOut
        //   needComp = 50
        //   Work     = 50 * (0.5+0.5) = 50.0
        //
        //   S CurrentPlan=Work a NoveltyPenalty=0.10:
        //   ReachOut = 52.5 * 0.90 = 47.25        ← po penalizaci Work=50 vyhraje
        //   Work     = 50.0 (InertiaWeight=0 → žádný boost)
        //
        [TestMethod]
        public void SelectAction_WithExpiredWorkPlan_PenalizesReachOutAsCrossCategory()
        {
            // Arrange
            var ctx = BuildContext(competence: 0.5, affiliation: 1.0, valence: -1.0);

            // Engine BEZ NoveltyPenalty — ReachOut=52.5 vyhraje nad Work=50
            var engineNoPenalty = BuildEngine(NoInertiaCfg with { NoveltyPenalty = 0.0 });
            engineNoPenalty.RestoreState(
                engineNoPenalty.State with
                {
                    CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0)
                });

            // Engine S NoveltyPenalty=0.10 — ReachOut=47.25 prohraje s Work=50
            var engineWithPenalty = BuildEngine(NoInertiaCfg); // NoveltyPenalty=0.1 je default
            engineWithPenalty.RestoreState(
                engineWithPenalty.State with
                {
                    CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0)
                });

            var outboxNoPenalty = new EventCollector();
            var outboxWithPenalty = new EventCollector();

            // Act
            engineNoPenalty.Tick(FarFuture, WTimeSpan.FromHours(1), ctx, outboxNoPenalty);
            engineWithPenalty.Tick(FarFuture, WTimeSpan.FromHours(1), ctx, outboxWithPenalty);

            // Assert
            var chosenNoPenalty = outboxNoPenalty.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenWithPenalty = outboxWithPenalty.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenNoPenalty, "Engine bez penalty musí vybrat akci.");
            Assert.IsNotNull(chosenWithPenalty, "Engine s penaltou musí vybrat akci.");

            // Bez penalty: ReachOut=52.5 > Work=50 → ReachOut vítězí
            Assert.AreEqual(ReachOut, chosenNoPenalty.ActionName,
                $"Bez NoveltyPenalty ReachOut=52.5 musí porazit Work=50. Zvoleno: {chosenNoPenalty.ActionName}");

            // S penaltou: ReachOut=47.25 < Work=50 → Work vítězí
            Assert.AreEqual(Work, chosenWithPenalty.ActionName,
                $"S NoveltyPenalty ReachOut=47.25 musí prohrát s Work=50. Zvoleno: {chosenWithPenalty.ActionName}");
        }

        // ====================================================================
        // TEST 3 — Biologické potřeby nejsou penalizovány
        // ====================================================================
        //
        // Izolace: InertiaWeight=0.0 → Work nedostane boost.
        //
        // Kalibrace (Hunger=31, Competence=0.5, CurrentPlan=Work):
        //
        //   needSelfCare = 0 (Pain=0, ImmuneLoad=0) → SelfCare=0
        //   Eat          = 31 * (0.5+1.2) = 52.7   (Biological → EXEMPT od penalty)
        //   needComp=50, Work = 50*(0.5+0.5) = 50.0 (InertiaWeight=0 → žádný boost)
        //
        //   Eat=52.7 > Work=50 → Eat vítězí (není penalizován)
        //
        //   POKUD by Eat penalizaci dostal: 52.7 * 0.9 = 47.43 < Work=50 → Work by vyhrál.
        //   Výsledek "Eat vyhrál" tedy přímo DOKAZUJE, že penalizace nebyla aplikována.
        //
        [TestMethod]
        public void SelectAction_WithExpiredWorkPlan_DoesNotPenalizeBiologicalEat()
        {
            // Arrange — Hunger=31 → Eat=52.7, Work=50; pokud Eat penalizován → 47.43 < 50
            var ctx = BuildContext(competence: 0.5, hunger: 31);

            var engine = BuildEngine(NoInertiaCfg); // NoveltyPenalty=0.1, InertiaWeight=0
            engine.RestoreState(
                engine.State with
                {
                    CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0)
                });

            var outbox = new EventCollector();

            // Act
            engine.Tick(FarFuture, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosen, "Engine musí vybrat akci.");

            // Eat=52.7 > Work=50 → Eat musí vyhrát
            // Pokud by Eat byl penalizován: 47.43 < Work=50 → Work by vyhrál
            // Tedy "Eat vyhrál" přímo dokazuje exemption od penalizace.
            Assert.AreEqual(Eat, chosen.ActionName,
                $"Biologická akce Eat=52.7 nesmí být penalizována — musí porazit Work=50. " +
                $"Pokud by byla penalizována: 47.43 < 50 → Work by vyhrál. Zvoleno: {chosen.ActionName}");
        }

        // ====================================================================
        // TEST 4 — Stejná kategorie nemá penalizaci (Work→Create = oba Productive)
        // ====================================================================
        //
        // Izolace: InertiaWeight=0.0 → Work nedostane boost.
        //
        // Kalibrace (Competence=0.5, Curiosity=0.6, CurrentPlan=Work):
        //
        //   needComp=50
        //   Work   = 50 * (0.5+0.5) = 50.0  (aktuální plán, InertiaWeight=0 → žádný boost)
        //   Create = 50 * (0.5+0.6) = 55.0  (Productive = Productive → ŽÁDNÁ penalty)
        //
        //   Create=55 > Work=50 → Create vítězí.
        //
        //   POKUD by Create penalizaci dostal: 55 * 0.9 = 49.5 < Work=50 → Work by vyhrál.
        //   Výsledek "Create vyhrál" tedy přímo DOKAZUJE, že cross-category penalty
        //   nebyla aplikována na přepnutí uvnitř stejné kategorie.
        //
        [TestMethod]
        public void SelectAction_WithExpiredWorkPlan_DoesNotPenalizeSameCategoryCreate()
        {
            // Arrange — Curiosity=0.6 → Create=55, Work=50; pokud Create penalizován → 49.5 < 50
            var ctx = BuildContext(competence: 0.5, curiosity: 0.6);

            var engine = BuildEngine(NoInertiaCfg); // NoveltyPenalty=0.1, InertiaWeight=0
            engine.RestoreState(
                engine.State with
                {
                    CurrentPlan = new PlannedAction(Work, new WDateTime(0), WTimeSpan.FromMinutes(1), 50.0)
                });

            var outbox = new EventCollector();

            // Act
            engine.Tick(FarFuture, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosen, "Engine musí vybrat akci.");

            // Create=55 > Work=50 → Create musí vyhrát
            // Pokud by Create byl penalizován: 49.5 < Work=50 → Work by vyhrál.
            // Tedy "Create vyhrál" přímo dokazuje, že stejná kategorie (Productive) není penalizována.
            Assert.AreEqual(Create, chosen.ActionName,
                $"Create=55 (stejná kategorie Productive jako Work) nesmí být penalizován — musí porazit Work=50. " +
                $"Pokud by byl penalizován: 49.5 < 50 → Work by vyhrál. Zvoleno: {chosen.ActionName}");
        }

        #region Factory metody

        /// <summary>
        /// Sestaví <see cref="DefaultBehaviorEngine"/> s danou konfigurací.
        /// Sleep threshold je vždy 999 — garantuje průchod Tick() k výběru akce.
        /// </summary>
        private static DefaultBehaviorEngine BuildEngine(BehaviorConfig cfg)
            => new DefaultBehaviorEngine(
                Options.Create(cfg),
                Options.Create(NoSleepCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        /// <summary>
        /// Sestaví <see cref="IHumanContext"/> s kalibrovanou osobností a fyziologií.
        ///
        /// Výchozí hodnoty jsou záměrně neutrální:
        /// Hunger=5, Thirst=5 → Eat/Drink jsou slabí kandidáti (utility ≈ 8).
        /// Stress=0 → needComp bez penalizace.
        /// Valence=0 → needBel = 70-50 = 20 (bez valence složky).
        /// Prázdné vztahy → MeanCloseness=50, topAttraction=0.
        /// </summary>
        private static IHumanContext BuildContext(
            double competence = 0.5,
            double curiosity = 0.5,
            double affiliation = 0.5,
            double sexuality = 0.3,
            double valence = 0.0,
            double hunger = 5,
            double pain = 0,
            double immuneLoad = 0)
        {
            var physio = new PhysiologyState(
                Energy: 95,
                SleepDebtHours: 0,
                Hunger: hunger,       // Parametrizovaná — pro Test 3
                Thirst: 5,
                Pain: pain,
                ImmuneLoad: immuneLoad,
                BodyTempDelta: 0,
                Cycle: null);

            var psych = new PsychologyState(
                Valence: valence,     // Parametrizovaná — ovlivňuje needBel
                Arousal: 0.5,
                Dominance: 0.5,
                Stress: 0,            // Nulový stres — čistý needComp
                CognitiveLoad: 0,
                DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    Attachment: AttachmentStyle.Secure,
                    Communication: CommunicationStyle.Direct,
                    Motivation: new MotivationWeights(
                        Affiliation: affiliation,   // → ReachOut utility
                        Achievement: 0.5,
                        Power: 0.3,
                        Altruism: 0.4,
                        Competence: competence,     // → needComp + Work utility
                        Autonomy: 0.5,
                        Curiosity: curiosity,       // → Create utility
                        Rest: 0.6,
                        Sexuality: sexuality),      // → InviteIntimacy utility
                    Sociosexuality: Sociosexuality.Intermediate,
                    Chronotype: Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion Factory metody

        #region Fake implementace

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> h) where TEvent : class, IDomainEvent => new D();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime w, ScheduledAction a, string? t = null) => new(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime n, WTimeSpan d, ScheduledAction a, string? t = null) => new(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => true;

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class D : IDisposable
        {
            public void Dispose()
            {
            }
        }

        #endregion Fake implementace
    }
}
