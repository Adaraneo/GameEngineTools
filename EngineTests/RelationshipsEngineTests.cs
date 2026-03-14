// RelationshipsEngineTests.cs
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

    /// <summary>
    /// Unit testy pro <see cref="DefaultRelationshipsEngine"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pokrývá tři bugy opravené v rámci refaktoru:
    /// <list type="number">
    ///   <item>
    ///     <b>RepairGain / RupturePenalty</b> — dříve hardcoded +4/-4, nyní z Config.
    ///   </item>
    ///   <item>
    ///     <b>DomainBreakdown</b> — dříve se nikdy neměnil, nyní reaguje na <see cref="SpeechAct"/>.
    ///   </item>
    ///   <item>
    ///     <b>Odmítnutí InteractionOutcome</b> — dříve bez efektu, nyní pokles Like a Comfort.
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    [TestClass]
    public class RelationshipsEngineTests : TestBase
    {
        #region Soukromá pole

        private IEventCollector _outbox = default!;
        private WDateTime _now;

        private static readonly RelationshipsConfig DefaultCfg = new RelationshipsConfig(
            DecayPerDay:    1.5,
            RepairGain:     6.0,
            RupturePenalty: 8.0);

        #endregion Soukromá pole

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _now    = new WDateTime(0);
            _outbox = new EventCollector();
        }

        #endregion Setup

        // ══════════════════════════════════════════════════════════════════════════════
        // BUG 1 — RepairAttempt musí používat Config.RepairGain / RupturePenalty
        // ══════════════════════════════════════════════════════════════════════════════

        #region RepairAttempt — Config hodnoty

        /// <summary>
        /// Přijatý pokus o opravu musí zvýšit Trust přesně o Config.RepairGain.
        /// </summary>
        [TestMethod]
        public void Handle_RepairAttempt_Accepted_IncreaseTrustByRepairGain()
        {
            // Arrange
            var engine  = BuildEngine();
            var self    = new HumanId(Guid.NewGuid());
            var other   = new HumanId(Guid.NewGuid());
            var ctx     = BuildContext(self);

            // Nejdřív vytvoříme hranu se známou hodnotou Trust
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var trustBefore = engine.State.Edges[other].Trust;

            // Act
            engine.Handle(new RepairAttempt(_now, self, other, Accepted: true), ctx, _outbox);

            // Assert
            var trustAfter = engine.State.Edges[other].Trust;
            Assert.AreEqual(
                expected: Math.Min(100, trustBefore + DefaultCfg.RepairGain),
                actual:   trustAfter,
                delta:    0.001,
                message:  $"Přijatá oprava musí přidat přesně RepairGain={DefaultCfg.RepairGain} k Trust. Bylo: {trustBefore:F2}, je: {trustAfter:F2}");
        }

        /// <summary>
        /// Odmítnutý pokus o opravu musí snížit Trust přesně o Config.RupturePenalty.
        /// </summary>
        [TestMethod]
        public void Handle_RepairAttempt_Rejected_DecreaseTrustByRupturePenalty()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var trustBefore = engine.State.Edges[other].Trust;

            // Act
            engine.Handle(new RepairAttempt(_now, self, other, Accepted: false), ctx, _outbox);

            // Assert
            var trustAfter = engine.State.Edges[other].Trust;
            Assert.AreEqual(
                expected: Math.Max(0, trustBefore - DefaultCfg.RupturePenalty),
                actual:   trustAfter,
                delta:    0.001,
                message:  $"Odmítnutá oprava musí odečíst přesně RupturePenalty={DefaultCfg.RupturePenalty} z Trust.");
        }

        #endregion RepairAttempt — Config hodnoty

        // ══════════════════════════════════════════════════════════════════════════════
        // BUG 2 — DomainBreakdown musí reagovat na SpeechAct
        // ══════════════════════════════════════════════════════════════════════════════

        #region DomainBreakdown — aktualizace dle SpeechAct

        /// <summary>
        /// SmallTalk musí zvýšit Humor doménu, ostatní domény zůstávají nezměněné.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_SmallTalk_Accepted_BoostsHumor()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var humorBefore = engine.State.Edges[other].Breakdown.Humor;

            // Act — SmallTalk přijat
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok", Act: SpeechAct.SmallTalk), ctx, _outbox);

            // Assert
            var humorAfter = engine.State.Edges[other].Breakdown.Humor;
            Assert.IsTrue(
                humorAfter > humorBefore,
                $"SmallTalk musí zvýšit Humor doménu. Před: {humorBefore:F2}, po: {humorAfter:F2}");
        }

        /// <summary>
        /// Question musí zvýšit Intellect doménu.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_Question_Accepted_BoostsIntellect()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var intellectBefore = engine.State.Edges[other].Breakdown.Intellect;

            // Act
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok", Act: SpeechAct.Question), ctx, _outbox);

            // Assert
            Assert.IsTrue(
                engine.State.Edges[other].Breakdown.Intellect > intellectBefore,
                "Question musí zvýšit Intellect doménu.");
        }

        /// <summary>
        /// Odmítnutá interakce musí také aktualizovat DomainBreakdown — ale o polovinu.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_Rejected_AppliesHalfDomainBoost()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            // Vytvoříme dvě čisté hrany se stejnou výchozí hodnotou Humor
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);

            var humorStart = engine.State.Edges[other].Breakdown.Humor;

            // Jedno přijetí — referenční boost
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true,  Reason: "ok",      Act: SpeechAct.Humor), ctx, _outbox);
            var humorAccepted = engine.State.Edges[other].Breakdown.Humor;

            // Resetujeme hranu na výchozí hodnotu
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()));
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);

            // Jedno odmítnutí — half boost
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: false, Reason: "nope", Act: SpeechAct.Humor), ctx, _outbox);
            var humorRejected = engine.State.Edges[other].Breakdown.Humor;

            // Assert — přijetí musí dát větší boost než odmítnutí
            var boostAccepted = humorAccepted - humorStart;
            var boostRejected = humorRejected - humorStart;

            Assert.IsTrue(
                boostAccepted > boostRejected,
                $"Přijetí musí dát větší domain boost než odmítnutí. Přijato: +{boostAccepted:F2}, odmítnuto: +{boostRejected:F2}");

            Assert.IsTrue(
                boostRejected > 0,
                $"I odmítnutí musí mít nenulový domain boost (half boost). Boost byl: {boostRejected:F2}");
        }

        #endregion DomainBreakdown — aktualizace dle SpeechAct

        // ══════════════════════════════════════════════════════════════════════════════
        // BUG 3 — Odmítnutá InteractionOutcome musí snižovat Like a Comfort
        // ══════════════════════════════════════════════════════════════════════════════

        #region Odmítnutí — pokles Like a Comfort

        /// <summary>
        /// Odmítnutá interakce musí snížit Like a Comfort — sociální sting z odmítnutí.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_Rejected_DecreasesLikeAndComfort()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);

            var likeBefore    = engine.State.Edges[other].Like;
            var comfortBefore = engine.State.Edges[other].Comfort;

            // Act — odmítnutá interakce
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: false, Reason: "declined", Act: SpeechAct.SmallTalk), ctx, _outbox);

            // Assert
            Assert.IsTrue(
                engine.State.Edges[other].Like < likeBefore,
                $"Odmítnutí musí snížit Like. Před: {likeBefore:F2}, po: {engine.State.Edges[other].Like:F2}");

            Assert.IsTrue(
                engine.State.Edges[other].Comfort < comfortBefore,
                $"Odmítnutí musí snížit Comfort. Před: {comfortBefore:F2}, po: {engine.State.Edges[other].Comfort:F2}");
        }

        /// <summary>
        /// Přijatá interakce musí ZVÝŠIT Like a Comfort — opačný efekt než odmítnutí.
        /// Ověřuje konzistenci: přijetí a odmítnutí jdou opačnými směry.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_Accepted_IncreasesLikeAndComfort()
        {
            // Arrange
            var engine = BuildEngine();
            var self   = new HumanId(Guid.NewGuid());
            var other  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);

            var likeBefore    = engine.State.Edges[other].Like;
            var comfortBefore = engine.State.Edges[other].Comfort;

            // Act
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "accepted", Act: SpeechAct.SmallTalk), ctx, _outbox);

            // Assert
            Assert.IsTrue(
                engine.State.Edges[other].Like > likeBefore,
                $"Přijetí musí zvýšit Like. Před: {likeBefore:F2}, po: {engine.State.Edges[other].Like:F2}");

            Assert.IsTrue(
                engine.State.Edges[other].Comfort > comfortBefore,
                $"Přijetí musí zvýšit Comfort. Před: {comfortBefore:F2}, po: {engine.State.Edges[other].Comfort:F2}");
        }

        #endregion Odmítnutí — pokles Like a Comfort

        #region Factory metody

        /// <summary>Sestaví engine s konfigurací dle <see cref="DefaultCfg"/>.</summary>
        private DefaultRelationshipsEngine BuildEngine() => new DefaultRelationshipsEngine(
            Options.Create(DefaultCfg),
            LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        /// <summary>
        /// Sestaví minimální kontext — RelationshipsEngine nepotřebuje v Handle() téměř nic
        /// kromě ctx.Id a ctx.Snapshot.Psychology (pro Tick).
        /// </summary>
        private IHumanContext BuildContext(HumanId id)
        {
            var psych = new PsychologyState(
                Valence: 0.0, Arousal: 0.5, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 0, DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>(), new Dictionary<string, SemanticFact>()));

            return new HumanContext
            {
                Id          = id,
                Biology     = SexBiology.Female,
                Personality = new Personality(
                    new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    AttachmentStyle.Secure,
                    CommunicationStyle.Direct,
                    new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                    Sociosexuality.Intermediate,
                    Chronotype.Neutral),
                Snapshot    = snapshot,
                Random      = new AlwaysTrueRandom(),
                Logger      = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus    = new NullEventBus(),
                Scheduler   = new NullScheduler()
            };
        }

        /// <summary>
        /// IRandomSource který vždy vrátí true pro Chance() — interakce jsou vždy přijaty
        /// pokud to test explicitně nevyžaduje jinak.
        /// </summary>
        private sealed class AlwaysTrueRandom : IRandomSource
        {
            public int    Next(int min, int max) => min;
            public double NextUnit()             => 0.0;
            public bool   Chance(double p)       => true;
        }

        #endregion Factory metody
    }
}
