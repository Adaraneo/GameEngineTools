// MemoryBehaviorTests.cs
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
    /// Integrační testy propojení Memory → Behavior.
    ///
    /// KLÍČOVÁ LEKCE — proč původní testy padaly:
    /// BehaviorEngine přepočítává potřeby VŽDY z aktuálního snapshotu (physiology/psychology),
    /// nikoliv z hodnot uložených v <see cref="BehaviorState"/>. Nastavení
    /// <c>NeedBelonging=80</c> v BehaviorState nemá žádný vliv na výsledek Tick().
    ///
    /// Vzorce enginu (musíme je znát pro kalibraci):
    /// <code>
    /// Util(need, weight)  = need * (0.5 + weight)
    /// needBel             = 70 - MeanCloseness(prázdné=50) + max(0,-valence*15)  // = 20 s Valence=0
    /// needComp            = 50 + (Competence-0.5)*80 - Stress*0.2
    /// needSelfCare        = Pain*0.7 + ImmuneLoad*0.3
    /// needInti            = 35*(0.5+Sexuality) + 0.6*topAttraction - stressPenalty
    /// </code>
    /// </summary>
    [TestClass]
    public class MemoryBehaviorTests : TestBase
    {
        #region Soukromá pole

        private WDateTime _now;

        private static readonly BehaviorConfig DefaultBehaviorCfg = new BehaviorConfig();

        /// <summary>
        /// Threshold=999 → Tick() vždy dojde k výběru akce z candidates,
        /// nikdy se nezablokuje v sleep-prompt čekání.
        /// </summary>
        private static readonly SleepConfig NoSleepCfg = new SleepConfig() with
        {
            SleepPromptThreshold = 999.0
        };

        #endregion Soukromá pole

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
        }

        #endregion Setup

        // ====================================================================
        // TEST 1 — Negativní vzpomínky na interakce penalizují ReachOut
        // ====================================================================
        //
        // Analytická kalibrace (prázdné vztahy → MeanCloseness=50, Valence=0, Stress=0):
        //
        //   needBel  = 70 - 50 = 20
        //   needComp = 50 + (0.25-0.5)*80 = 30
        //
        //   Affiliation=0.9 → ReachOut = 20 * (0.5+0.9) = 28.0   ← vítěz bez paměti
        //   Competence=0.25 → Work     = 30 * (0.5+0.25) = 22.5
        //   Curiosity=0.25  → Create   = 30 * (0.5+0.25) = 22.5
        //   Hunger=5        → Eat      = 5  * (0.5+1.2)  =  8.5   (neohrožuje)
        //
        //   4 negative → penalty = min(0.40, 4×0.10) = 0.40 (strop)
        //   ReachOut po penalizaci = 28.0 × 0.60 = 16.8 < Work=22.5 ✓
        //
        [TestMethod]
        public void Tick_WithNegativeInteractionMemories_SuppressesDirectReachOut()
        {
            // Arrange
            var cleanMemory = EmptyMemory();
            var traumaMemory = Memory(NegativeInteractions(count: 4, strength: 0.7));

            var ctxClean = BuildContext(cleanMemory, affiliation: 0.9, competence: 0.25, curiosity: 0.25);
            var ctxTrauma = BuildContext(traumaMemory, affiliation: 0.9, competence: 0.25, curiosity: 0.25);

            var outboxClean = new EventCollector();
            var outboxTrauma = new EventCollector();

            // Act
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxClean, outboxClean);
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxTrauma, outboxTrauma);

            // Assert
            var chosenClean = outboxClean.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenTrauma = outboxTrauma.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenClean, "Čistá postava musí vybrat akci.");
            Assert.IsNotNull(chosenTrauma, "Traumatizovaná postava musí vybrat akci.");

            Assert.IsTrue(
                chosenClean.ActionName is ReachOut or MoveToSocial,
                $"Bez negativní historie má sociální směr zůstat dostupný. Zvoleno: {chosenClean.ActionName}");

            Assert.AreNotEqual(ReachOut, chosenTrauma.ActionName,
                $"Po traumatu nesmí vyhrát přímý ReachOut. Zvoleno: {chosenTrauma.ActionName}");
        }

        // ====================================================================
        // TEST 2 — Pozitivní vzpomínky boostují ReachOut
        // ====================================================================
        //
        // Analytická kalibrace:
        //
        //   Affiliation=0.5 → ReachOut = 20 * (0.5+0.5) = 20.0   ← pod Work bez paměti
        //   Competence=0.25 → Work     = 30 * 0.75       = 22.5   ← vítěz bez paměti
        //   Curiosity=0.25  → Create   = 22.5
        //
        //   3 pozitivní → boost = min(0.25, 3×0.08) = 0.24
        //   ReachOut po boostu = 20.0 × 1.24 = 24.8 > Work=22.5 ✓
        //
        [TestMethod]
        public void Tick_WithPositiveInteractionMemories_WithoutConcreteTarget_DoesNotForceSocialChoice()
        {
            // Arrange
            var noMemory = EmptyMemory();
            var positiveMemory = Memory(PositiveInteractions(count: 3, strength: 0.7));

            // Záměrně nižší Affiliation=0.5 → ReachOut=20 NEVYHRAJE bez paměti
            var ctxNoMem = BuildContext(noMemory, affiliation: 0.5, competence: 0.25, curiosity: 0.25);
            var ctxPosMem = BuildContext(positiveMemory, affiliation: 0.5, competence: 0.25, curiosity: 0.25);

            var outboxNoMem = new EventCollector();
            var outboxPosMem = new EventCollector();

            // Act
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxNoMem, outboxNoMem);
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxPosMem, outboxPosMem);

            // Assert
            var chosenNoMem = outboxNoMem.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenPosMem = outboxPosMem.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenNoMem, "Postava bez paměti musí vybrat akci.");
            Assert.IsNotNull(chosenPosMem, "Postava s pozitivní pamětí musí vybrat akci.");

            Assert.AreNotEqual(ReachOut, chosenNoMem.ActionName,
                $"Bez pozitivní sociální historie nemá přímý ReachOut vyhrát. Zvoleno: {chosenNoMem.ActionName}");

            Assert.AreEqual(Work, chosenPosMem.ActionName,
                $"Bez konkrétního sociálního targetu samotná pozitivní paměť nemá vytvořit přímou sociální akci. Zvoleno: {chosenPosMem.ActionName}");
        }

        // ====================================================================
        // TEST 3 — Intimní odmítnutí penalizuje InviteIntimacy
        // ====================================================================
        //
        // Analytická kalibrace (prázdné vztahy → topAttraction=0):
        //
        //   Sexuality=0.8  → needInti       = 35*(0.5+0.8) = 45.5
        //                     InviteIntimacy = 45.5*(0.5+0.8) = 59.15  ← vítěz bez paměti
        //   Competence=0.5 → needComp=50, Work   = 50*(0.5+0.5) = 50.0
        //   Curiosity=0.4  → Create = 50*(0.5+0.4) = 45.0
        //
        //   2 odmítnutí → penalty = min(0.55, 2×0.20) = 0.40
        //   InviteIntimacy po penalizaci = 59.15 × 0.60 = 35.49 < Work=50 ✓
        //
        [TestMethod]
        public void Tick_WithRejectedIntimacyMemories_AvoidInviteIntimacy()
        {
            // Arrange
            var cleanMemory = EmptyMemory();
            var rejectedMemory = Memory(RejectedIntimacyEpisodes(count: 2, strength: 0.7));

            var ctxClean = BuildContext(cleanMemory, sexuality: 0.8, competence: 0.5, curiosity: 0.4);
            var ctxRejected = BuildContext(rejectedMemory, sexuality: 0.8, competence: 0.5, curiosity: 0.4);

            var outboxClean = new EventCollector();
            var outboxRejected = new EventCollector();

            // Act
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxClean, outboxClean);
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxRejected, outboxRejected);

            // Assert
            var chosenClean = outboxClean.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenRejected = outboxRejected.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenClean, "Čistá postava musí vybrat akci.");
            Assert.IsNotNull(chosenRejected, "Odmítnutá postava musí vybrat akci.");

            Assert.AreEqual(Work, chosenClean.ActionName,
                $"Bez konkrétního targetu samotná vysoká sexualita nemá nutit InviteIntimacy. Zvoleno: {chosenClean.ActionName}");

            Assert.AreNotEqual(InviteIntimacy, chosenRejected.ActionName,
                $"Po odmítnutí nesmí vyhrát InviteIntimacy. Zvoleno: {chosenRejected.ActionName}");
        }

        // ====================================================================
        // TEST 4 — Emocionální zátěž boostuje SelfCare
        // ====================================================================
        //
        // Analytická kalibrace (Pain=55, ImmuneLoad=10):
        //
        //   needSelfCare = 55*0.7 + 10*0.3 = 41.5
        //   SelfCare     = Util(41.5, 0.5) = 41.5*(0.5+0.5) = 41.5   ← pod Work=50 bez paměti
        //   needComp=50, Work=50, Create=Util(50,0.4)=45
        //
        //   5 negativních → negativeLoad = 5×0.7 = 3.5
        //   boost = min(0.35, 3.5×0.08) = min(0.35, 0.28) = 0.28
        //   SelfCare po boostu = 41.5 × 1.28 = 53.1 > Work=50 ✓
        //
        [TestMethod]
        public void Tick_WithHighNegativeEmotionalLoad_BoostsSelfCare()
        {
            // Arrange
            var noMemory = EmptyMemory();
            var heavyMemory = Memory(MixedNegativeEpisodes(count: 5, strength: 0.7));

            // Pain=55 → needSelfCare=41.5 → SelfCare=41.5 (záměrně POD Work=50 bez paměti)
            var ctxNoMem = BuildContext(noMemory, competence: 0.5, curiosity: 0.4, pain: 55, immuneLoad: 10);
            var ctxHeavy = BuildContext(heavyMemory, competence: 0.5, curiosity: 0.4, pain: 55, immuneLoad: 10);

            var outboxNoMem = new EventCollector();
            var outboxHeavy = new EventCollector();

            // Act
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxNoMem, outboxNoMem);
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctxHeavy, outboxHeavy);

            // Assert
            var chosenNoMem = outboxNoMem.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosenHeavy = outboxHeavy.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosenNoMem, "Postava bez paměti musí vybrat akci.");
            Assert.IsNotNull(chosenHeavy, "Postava s negativní zátěží musí vybrat akci.");

            // Work=50 > SelfCare=41.5 → SelfCare nevyhraje bez paměti
            Assert.AreNotEqual(SelfCare, chosenNoMem.ActionName,
                $"Bez paměti (SelfCare=41.5 < Work=50) nesmí vyhrát SelfCare. Zvoleno: {chosenNoMem.ActionName}");

            // SelfCare=53.1 > Work=50 → SelfCare vyhraje po boostu
            Assert.AreEqual(SelfCare, chosenHeavy.ActionName,
                $"S negativní zátěží (SelfCare=53.1 > Work=50) musí vyhrát SelfCare. Zvoleno: {chosenHeavy.ActionName}");
        }

        // ====================================================================
        // TEST 5 — Prázdná paměť je no-op (deterministický baseline)
        // ====================================================================

        [TestMethod]
        public void Tick_WithEmptyMemory_IsDetministicAndProducesSameChoiceTwice()
        {
            // Arrange — identické kontexty, identická prázdná paměť
            var ctx1 = BuildContext(EmptyMemory(), affiliation: 0.9, competence: 0.25, curiosity: 0.25);
            var ctx2 = BuildContext(EmptyMemory(), affiliation: 0.9, competence: 0.25, curiosity: 0.25);

            var outbox1 = new EventCollector();
            var outbox2 = new EventCollector();

            // Act
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctx1, outbox1);
            BuildEngine().Tick(_now, WTimeSpan.FromHours(1), ctx2, outbox2);

            // Assert
            var chosen1 = outbox1.Drain().OfType<ActionCommitted>().FirstOrDefault();
            var chosen2 = outbox2.Drain().OfType<ActionCommitted>().FirstOrDefault();

            Assert.IsNotNull(chosen1, "Engine 1 musí vybrat akci.");
            Assert.IsNotNull(chosen2, "Engine 2 musí vybrat akci.");
            Assert.AreEqual(chosen1.ActionName, chosen2.ActionName,
                "Identické vstupy musí dát identický výsledek — engine je deterministický.");
        }

        #region Factory metody

        private DefaultBehaviorEngine BuildEngine() => new DefaultBehaviorEngine(
            Options.Create(DefaultBehaviorCfg),
            Options.Create(NoSleepCfg),
            LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
            new EngineTests.Utils.PermissiveDevelopmentPolicy());

        /// <summary>
        /// Sestaví kontext s přesně kalibrovanou osobností.
        ///
        /// Všechny potřeby jsou přepočítány enginem ze snapshotu — proto:
        ///  · Hunger=5, Thirst=5  → Eat/Drink jsou slabí kandidáti (utility ≈ 8)
        ///  · Stress=0            → needComp = 50 + (Competence-0.5)*80 (bez penalizace)
        ///  · Valence=0           → needBel  = 70 - 50 = 20 (bez valence boostování)
        ///  · Prázdné vztahy      → MeanCloseness=50, topAttraction=0
        /// </summary>
        private IHumanContext BuildContext(
            MemoryIndex memory,
            double affiliation = 0.5,
            double competence = 0.5,
            double curiosity = 0.5,
            double sexuality = 0.3,
            double pain = 0,
            double immuneLoad = 0)
        {
            var physio = new PhysiologyState(
                Energy: 95,             // → needRest ≈ 22.5, daleko pod threshold 999
                SleepDebtHours: 0,
                Hunger: 5,             // → Eat = 5*1.7 = 8.5 (neohrožuje testované kandidáty)
                Thirst: 5,             // → Drink = 5*1.6 = 8.0
                Pain: pain,            // → vstup pro needSelfCare
                ImmuneLoad: immuneLoad,
                BodyTempDelta: 0,
                Cycle: null);

            var psych = new PsychologyState(
                Valence: 0.0,          // → needBel = 70-50+0 = 20 (bez valence složky)
                Arousal: 0.5,
                Dominance: 0.5,
                Stress: 0,             // → needComp bez penalizace Stress*0.2
                CognitiveLoad: 0,
                DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),  // BehaviorState se v Tick() ignoruje
                new InteractionSurface(null, false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                memory);

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    Attachment: AttachmentProfile.Secure,
                    Communication: CommunicationStyle.Direct,
                    Motivation: new MotivationWeights(
                        Affiliation: affiliation,    // → škáluje ReachOut utility
                        Achievement: 0.5,
                        Power: 0.3,
                        Altruism: 0.4,
                        Competence: competence,      // → škáluje needComp + Work utility
                        Autonomy: 0.5,
                        Curiosity: curiosity,        // → škáluje Create utility
                        Rest: 0.6,
                        Sexuality: sexuality),       // → škáluje InviteIntimacy utility
                    Sociosexuality: Sociosexuality.Intermediate,
                    Chronotype: Chronotype.Neutral),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static MemoryIndex EmptyMemory()
            => new MemoryIndex(new List<EpisodicMemory>());

        private static MemoryIndex Memory(List<EpisodicMemory> episodes)
            => new MemoryIndex(episodes);

        /// <summary>4 negativní interakce → penalty = min(0.40, 4×0.10) = 0.40 (cap)</summary>
        private static List<EpisodicMemory> NegativeInteractions(int count, double strength)
            => Enumerable.Range(0, count)
                .Select(i => new EpisodicMemory(Guid.NewGuid(), new WDateTime(0),
                    $"Interaction:A->B:declined_{i}", 0.7, EmotionalTag.Negative, strength))
                .ToList();

        /// <summary>3 pozitivní interakce → boost = min(0.25, 3×0.08) = 0.24</summary>
        private static List<EpisodicMemory> PositiveInteractions(int count, double strength)
            => Enumerable.Range(0, count)
                .Select(i => new EpisodicMemory(Guid.NewGuid(), new WDateTime(0),
                    $"Interaction:A->B:accepted_{i}", 0.7, EmotionalTag.Positive, strength))
                .ToList();

        /// <summary>2 odmítnutí intimity → penalty = min(0.55, 2×0.20) = 0.40</summary>
        private static List<EpisodicMemory> RejectedIntimacyEpisodes(int count, double strength)
            => Enumerable.Range(0, count)
                .Select(i => new EpisodicMemory(Guid.NewGuid(), new WDateTime(0),
                    $"Action:InviteIntimacy_{i}", 0.8, EmotionalTag.Negative, strength))
                .ToList();

        /// <summary>5 smíšených negativních → negativeLoad=3.5, boost=0.28, SelfCare×1.28</summary>
        private static List<EpisodicMemory> MixedNegativeEpisodes(int count, double strength)
            => Enumerable.Range(0, count)
                .Select(i => new EpisodicMemory(Guid.NewGuid(), new WDateTime(0),
                    $"Negative:MixedEvent_{i}", 0.6, EmotionalTag.Negative, strength))
                .ToList();

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

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n) => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
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
