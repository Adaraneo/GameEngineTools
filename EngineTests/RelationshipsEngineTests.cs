// RelationshipsEngineTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Interests;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Engines.Values;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System.Collections.Generic;
    using System.Linq;

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
            DecayPerDay: 1.5,
            RepairGain: 6.0,
            RupturePenalty: 8.0);

        #endregion Soukromá pole

        #region Setup

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
        }

        #endregion Setup

        // ══════════════════════════════════════════════════════════════════════════════
        // Community reputation — newcomer trust prior at FirstImpressionFormed
        // ══════════════════════════════════════════════════════════════════════════════

        #region FirstImpression — community reputation trust prior

        /// <summary>
        /// A community trust prior carried on <see cref="FirstImpressionFormed"/> biases the seeded
        /// Trust: a well-regarded newcomer (prior 0.7) is trusted above the halo baseline, an
        /// ill-regarded one (prior 0.15) below it, and an unknown one (null) is unchanged.
        /// With Attraction = 40 the halo bonus is zero, so the halo-only baseline Trust is exactly 50
        /// (50 + (prior − 0.4) × ReputationTrustPriorWeight).
        /// </summary>
        [TestMethod]
        public void Handle_FirstImpression_TrustPrior_ShiftsSeededTrust()
        {
            // Arrange — three independent meetings, identical attraction, differing reputation prior.
            var self = new HumanId(Guid.NewGuid());
            var goodRep = new HumanId(Guid.NewGuid());
            var badRep = new HumanId(Guid.NewGuid());
            var unknown = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            var weight = DefaultCfg.ReputationTrustPriorWeight;

            // Act
            var goodEngine = BuildEngine();
            goodEngine.Handle(new FirstImpressionFormed(_now, self, goodRep, Like: 50, Attraction: 40, TrustPrior: 0.7), ctx, _outbox);

            var badEngine = BuildEngine();
            badEngine.Handle(new FirstImpressionFormed(_now, self, badRep, Like: 50, Attraction: 40, TrustPrior: 0.15), ctx, _outbox);

            var unknownEngine = BuildEngine();
            unknownEngine.Handle(new FirstImpressionFormed(_now, self, unknown, Like: 50, Attraction: 40, TrustPrior: null), ctx, _outbox);

            // Assert — exact halo baseline (50) shifted by (prior − 0.4) × weight.
            var goodTrust = goodEngine.State.Edges[goodRep].Trust;
            var badTrust = badEngine.State.Edges[badRep].Trust;
            var unknownTrust = unknownEngine.State.Edges[unknown].Trust;

            Assert.AreEqual(50.0 + (0.7 - 0.4) * weight, goodTrust, 0.001,
                $"Good local reputation must raise seeded Trust above the halo baseline. Got: {goodTrust:F2}");
            Assert.AreEqual(50.0 + (0.15 - 0.4) * weight, badTrust, 0.001,
                $"Bad local reputation must lower seeded Trust below the halo baseline. Got: {badTrust:F2}");
            Assert.AreEqual(50.0, unknownTrust, 0.001,
                $"An unknown newcomer (null prior) must seed the halo baseline unchanged. Got: {unknownTrust:F2}");

            Assert.IsTrue(goodTrust > unknownTrust && unknownTrust > badTrust,
                "Reputation must order newcomer trust: good > unknown > bad.");
        }

        #endregion FirstImpression — community reputation trust prior

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
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            // Nejdřív vytvoříme hranu se známou hodnotou Trust
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var trustBefore = engine.State.Edges[other].Trust;

            // Act
            engine.Handle(new RepairAttempt(_now, self, other, Accepted: true), ctx, _outbox);

            // Assert
            var trustAfter = engine.State.Edges[other].Trust;
            Assert.AreEqual(
                expected: Math.Min(100, trustBefore + DefaultCfg.RepairGain),
                actual: trustAfter,
                delta: 0.001,
                message: $"Přijatá oprava musí přidat přesně RepairGain={DefaultCfg.RepairGain} k Trust. Bylo: {trustBefore:F2}, je: {trustAfter:F2}");
        }

        /// <summary>
        /// Odmítnutý pokus o opravu musí snížit Trust přesně o Config.RupturePenalty.
        /// </summary>
        [TestMethod]
        public void Handle_RepairAttempt_Rejected_DecreaseTrustByRupturePenalty()
        {
            // Arrange
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            var trustBefore = engine.State.Edges[other].Trust;

            // Act
            engine.Handle(new RepairAttempt(_now, self, other, Accepted: false), ctx, _outbox);

            // Assert
            var trustAfter = engine.State.Edges[other].Trust;
            Assert.AreEqual(
                expected: Math.Max(0, trustBefore - DefaultCfg.RupturePenalty),
                actual: trustAfter,
                delta: 0.001,
                message: $"Odmítnutá oprava musí odečíst přesně RupturePenalty={DefaultCfg.RupturePenalty} z Trust.");
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
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

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
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

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
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            // Vytvoříme dvě čisté hrany se stejnou výchozí hodnotou Humor
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);

            var humorStart = engine.State.Edges[other].Breakdown.Humor;

            // Jedno přijetí — referenční boost
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok", Act: SpeechAct.Humor), ctx, _outbox);
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
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);

            var likeBefore = engine.State.Edges[other].Like;
            var comfortBefore = engine.State.Edges[other].Comfort;

            // Act — odmítnutá interakce
            engine.Handle(new InteractionOutcome(_now, self, other, Accepted: false, Reason: "declined", Act: SpeechAct.SmallTalk), ctx, _outbox);

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
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);

            var likeBefore = engine.State.Edges[other].Like;
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

        #region Relationship signals

        /// <summary>
        /// Accepted conversation should build familiarity first and only nudge sexual interest modestly.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_Accepted_IncreasesFamiliarityWithoutAutomaticSexualInterestSpike()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 55, Attraction: 45), ctx, _outbox);

            var before = engine.State.Edges[other];
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok", Act: SpeechAct.SmallTalk), ctx, _outbox);

            var after = engine.State.Edges[other];
            Assert.IsTrue(after.Familiarity > before.Familiarity);
            Assert.IsTrue(after.SexualInterest - before.SexualInterest < 2.0);
        }

        /// <summary>
        /// Repeated accepted low-stakes contact should gradually consolidate safety for sensitive characters.
        /// </summary>
        [TestMethod]
        public void Handle_RepeatedAcceptedLowStakesContact_ConsolidatesSensitiveRelationship()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                new BigFive(0.5, 0.25, 0.45, 0.65, 0.9),
                AttachmentProfile.Preoccupied,
                CommunicationStyle.Indirect,
                new MotivationWeights(0.8, 0.4, 0.2, 0.5, 0.4, 0.4, 0.5, 0.5, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);
            var ctx = BuildContext(self, personality);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 52, Attraction: 40), ctx, _outbox);
            var initial = engine.State.Edges[other];

            for (var i = 0; i < 8; i++)
            {
                engine.Handle(
                    new InteractionOutcome(_now + WTimeSpan.FromHours(i + 1), other, self, Accepted: true, Reason: "accepted", Act: SpeechAct.SmallTalk),
                    ctx,
                    _outbox);
            }

            var stabilized = engine.State.Edges[other];
            Assert.IsTrue(stabilized.PositiveInteractionCount >= 8);
            Assert.IsTrue(stabilized.Trust > initial.Trust, "Low-stakes accepted contact should now build some trust consolidation.");
            Assert.IsTrue(stabilized.Comfort > initial.Comfort + 6.4);
            Assert.IsTrue(stabilized.Closeness > initial.Closeness + 12.0);

            engine.Handle(
                new InteractionOutcome(_now + WTimeSpan.FromHours(12), self, other, Accepted: false, Reason: "declined", Act: SpeechAct.SmallTalk),
                ctx,
                _outbox);

            var afterSetback = engine.State.Edges[other];
            Assert.IsTrue(afterSetback.Comfort > initial.Comfort, "One later setback should not erase the whole positive trend.");
            Assert.IsTrue(afterSetback.Trust >= initial.Trust);
        }

        /// <summary>
        /// First impression should keep physical and aesthetic seeding as distinct signals.
        /// </summary>
        [TestMethod]
        public void Handle_FirstImpression_SeedsPhysicalAndAestheticAttractionSeparately()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40, BasePhysical: 40, PreferenceMatch: 0), ctx, _outbox);
            var first = engine.State.Edges[other];
            Assert.IsTrue(first.PhysicalAttraction > first.AestheticAttraction);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()));
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40, BasePhysical: 0, PreferenceMatch: 35), ctx, _outbox);
            var second = engine.State.Edges[other];
            Assert.IsTrue(second.AestheticAttraction > second.PhysicalAttraction);
        }

        /// <summary>
        /// Intimate touch should create a stronger sexual-interest increase than light touch.
        /// </summary>
        [TestMethod]
        public void Handle_TouchOutcome_IntimateTouchRaisesSexualInterestMoreThanLightTouch()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 55, Attraction: 45), ctx, _outbox);
            var start = engine.State.Edges[other].SexualInterest;

            engine.Handle(new TouchOutcome(_now, other, self, TouchLevel.Light, Accepted: true, Reason: "ok"), ctx, _outbox);
            var light = engine.State.Edges[other].SexualInterest;

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()));
            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 55, Attraction: 45), ctx, _outbox);
            engine.Handle(new TouchOutcome(_now, other, self, TouchLevel.Intimate, Accepted: true, Reason: "ok"), ctx, _outbox);
            var intimate = engine.State.Edges[other].SexualInterest;

            Assert.IsTrue((light - start) < (intimate - start));
        }

        /// <summary>
        /// Declined sexual encounters should reduce sexual interest toward the person involved.
        /// </summary>
        [TestMethod]
        public void Handle_SexualEncounterOutcome_Declined_DecreasesSexualInterest()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 70,
                    Familiarity: 50, AestheticAttraction: 65, PhysicalAttraction: 65, IntimateAffinity: 55, SexualInterest: 60,
                    Closeness: 70, Respect: 60, Comfort: 75,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60),
                    PositiveInteractionCount: 3)
            }));

            var before = engine.State.Edges[other].SexualInterest;

            engine.Handle(new SexualEncounterOutcome(_now, self, other, Accepted: false, Reason: "declined"), ctx, _outbox);

            var after = engine.State.Edges[other].SexualInterest;
            Assert.IsTrue(after < before);
        }

        [TestMethod]
        public void Tick_ReducedSocialFidelity_DefersDecayUntilCadence()
        {
            var engine = BuildEngine(socialFidelity: SocialFidelityLevel.Reduced);
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 60,
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 40, SexualInterest: 40,
                    Closeness: 60, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50))
            }));

            var before = engine.State.Edges[other];

            engine.Tick(_now, WTimeSpan.FromDays(0.25), ctx, _outbox);
            var afterFirstTick = engine.State.Edges[other];

            Assert.AreEqual(before.Closeness, afterFirstTick.Closeness, 0.0001);
            Assert.AreEqual(before.Trust, afterFirstTick.Trust, 0.0001);
            Assert.AreEqual(before.Familiarity, afterFirstTick.Familiarity, 0.0001);

            engine.Tick(_now + WTimeSpan.FromDays(0.25), WTimeSpan.FromDays(0.25), ctx, _outbox);
            var afterSecondTick = engine.State.Edges[other];

            Assert.IsTrue(afterSecondTick.Closeness < before.Closeness || afterSecondTick.Trust < before.Trust);
        }

        [TestMethod]
        public void Tick_MinimalSocialFidelity_DefersDecayUntilFullDay()
        {
            var engine = BuildEngine(socialFidelity: SocialFidelityLevel.Minimal);
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 60,
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 40, SexualInterest: 40,
                    Closeness: 60, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50))
            }));

            var before = engine.State.Edges[other];

            engine.Tick(_now, WTimeSpan.FromDays(0.5), ctx, _outbox);
            var afterFirstTick = engine.State.Edges[other];

            Assert.AreEqual(before.Closeness, afterFirstTick.Closeness, 0.0001);
            Assert.AreEqual(before.Trust, afterFirstTick.Trust, 0.0001);
            Assert.AreEqual(before.Familiarity, afterFirstTick.Familiarity, 0.0001);

            engine.Tick(_now + WTimeSpan.FromDays(0.5), WTimeSpan.FromDays(0.5), ctx, _outbox);
            var afterSecondTick = engine.State.Edges[other];

            Assert.IsTrue(afterSecondTick.Closeness < before.Closeness || afterSecondTick.Trust < before.Trust);
        }

        [TestMethod]
        public void Tick_FullSocialFidelity_AppliesDecayImmediately()
        {
            var engine = BuildEngine(socialFidelity: SocialFidelityLevel.Full);
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 60,
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 40, SexualInterest: 40,
                    Closeness: 60, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50))
            }));

            var before = engine.State.Edges[other];

            engine.Tick(_now, WTimeSpan.FromHours(6), ctx, _outbox);
            var after = engine.State.Edges[other];

            Assert.IsTrue(after.Closeness < before.Closeness || after.Trust < before.Trust);
        }

        /// <summary>
        /// Familiarity should decay much more slowly than closeness.
        /// </summary>
        [TestMethod]
        public void Tick_DecaysClosenessFasterThanFamiliarity()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 60,
                    Familiarity: 80, AestheticAttraction: 70, PhysicalAttraction: 70, IntimateAffinity: 65, SexualInterest: 60,
                    Closeness: 80, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 5)
            }));

            var before = engine.State.Edges[other];
            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, _outbox);
            var after = engine.State.Edges[other];

            Assert.IsTrue((before.Closeness - after.Closeness) > (before.Familiarity - after.Familiarity));
        }

        /// <summary>
        /// Behavior intimacy should be driven by explicit intimacy-relevant signals.
        /// </summary>
        [TestMethod]
        public void ComputeIntimacyNeed_UsesExplicitSignalsInsteadOfOnlyLegacyAttraction()
        {
            var self = new HumanId(Guid.NewGuid());
            var otherA = new HumanId(Guid.NewGuid());
            var otherB = new HumanId(Guid.NewGuid());

            var rel = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [otherA] = new RelationshipEdge(
                    self, otherA,
                    Like: 55, Trust: 55,
                    Familiarity: 40, AestheticAttraction: 35, PhysicalAttraction: 35, IntimateAffinity: 20, SexualInterest: 15,
                    Closeness: 25, Respect: 55, Comfort: 30,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 0),
                [otherB] = new RelationshipEdge(
                    self, otherB,
                    Like: 60, Trust: 70,
                    Familiarity: 55, AestheticAttraction: 60, PhysicalAttraction: 60, IntimateAffinity: 75, SexualInterest: 80,
                    Closeness: 70, Respect: 60, Comfort: 75,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 0)
            });

            var top = BehaviorMath.ComputeTopIntimacyPotential(rel);
            Assert.IsTrue(top > 60);
            Assert.IsTrue(top < 90);
        }

        /// <summary>
        /// Rejected invites must not create positive physical-domain evidence.
        /// </summary>
        [TestMethod]
        public void Handle_InteractionOutcome_RejectedInvite_DoesNotIncreasePhysicalDomain()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 55, Trust: 55,
                    Familiarity: 45, AestheticAttraction: 55, PhysicalAttraction: 55, IntimateAffinity: 45, SexualInterest: 45,
                    Closeness: 45, Respect: 55, Comfort: 50,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 55))
            }));

            var before = engine.State.Edges[other].Breakdown.Physical;
            engine.Handle(new InteractionOutcome(_now, self, other, Accepted: false, Reason: "declined", Act: SpeechAct.Invite), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].Breakdown.Physical <= before);
        }

        /// <summary>
        /// Familiarity decay target should be configurable instead of hardcoded.
        /// </summary>
        [TestMethod]
        public void Tick_FamiliarityMovesTowardConfiguredFloor()
        {
            var engine = BuildEngine(new RelationshipsConfig(DecayPerDay: 1.5, FamiliarityDecayFloor: 25.0));
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 55, Trust: 55,
                    Familiarity: 5, AestheticAttraction: 55, PhysicalAttraction: 55, IntimateAffinity: 45, SexualInterest: 45,
                    Closeness: 45, Respect: 55, Comfort: 50,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50))
            }));

            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].Familiarity > 5);
            Assert.IsTrue(engine.State.Edges[other].Familiarity <= 25.001);
        }

        /// <summary>
        /// Attraction should be relatively stable but still plastic under repeated safe or negative interaction outcomes.
        /// </summary>
        [TestMethod]
        public void Handle_RepeatedRelationshipOutcomes_NudgeSubjectiveAttraction()
        {
            var engine = BuildEngine(new RelationshipsConfig(AttractionPlasticityPerInteraction: 1.0));
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 75, Trust: 75,
                    Familiarity: 60, AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 45, SexualInterest: 45,
                    Closeness: 55, Respect: 70, Comfort: 78,
                    Breakdown: new DomainBreakdown(60, 60, 60, 60, 55),
                    PositiveInteractionCount: 2)
            }));

            var beforePositive = engine.State.Edges[other];
            for (var i = 0; i < 5; i++)
            {
                engine.Handle(new InteractionOutcome(_now + WTimeSpan.FromHours(i), other, self, Accepted: true, Reason: "accepted", Act: SpeechAct.Validation), ctx, _outbox);
            }

            var afterPositive = engine.State.Edges[other];
            Assert.IsTrue(afterPositive.AestheticAttraction > beforePositive.AestheticAttraction);
            Assert.IsTrue(afterPositive.PhysicalAttraction > beforePositive.PhysicalAttraction);
            Assert.IsTrue(afterPositive.AestheticAttraction - beforePositive.AestheticAttraction < 6.0);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = beforePositive with
                {
                    Like = 35,
                    Trust = 25,
                    Comfort = 25,
                    AestheticAttraction = 50,
                    PhysicalAttraction = 50,
                    PositiveInteractionCount = 0
                }
            }));

            for (var i = 0; i < 5; i++)
            {
                engine.Handle(new InteractionOutcome(_now + WTimeSpan.FromHours(i), self, other, Accepted: false, Reason: "declined", Act: SpeechAct.Invite), ctx, _outbox);
            }

            var afterNegative = engine.State.Edges[other];
            Assert.IsTrue(afterNegative.AestheticAttraction < 50);
            Assert.IsTrue(afterNegative.PhysicalAttraction < 50);
            Assert.IsTrue(50 - afterNegative.AestheticAttraction < 8.0);
        }

        #endregion Relationship signals

        #region Runtime wiring

        /// <summary>
        /// Recipient-generated interaction outcomes must reach both directional relationship snapshots.
        /// </summary>
        [TestMethod]
        public void RuntimeFlow_InteractionOutcome_UpdatesRelationshipSnapshotsOnBothSides()
        {
            var now = WDateTime.New(100, 1, 1);
            var dt = WTimeSpan.FromHours(0.5);
            var initiator = CreateRuntimeHuman(SexBiology.Male);
            var recipient = CreateRuntimeHuman(SexBiology.Female);

            SeedRelationship(recipient, initiator.Id, closeness: 80, comfort: 85, trust: 85, romanticInterest: 60, sexualInterest: 55);

            var recipientBefore = recipient.Snapshot.Relationships.Edges[initiator.Id];

            recipient.ReceiveEvent(new InteractionProposed(now, initiator.Id, recipient.Id, SpeechAct.SmallTalk, "Ahoj"));
            recipient.Tick(now, dt);

            Assert.IsTrue(
                recipient.LastOutbox.OfType<InteractionOutcome>().Any(o => o.From == initiator.Id && o.To == recipient.Id && o.Accepted),
                "Recipient should publish an accepted InteractionOutcome.");

            var outcome = recipient.LastOutbox.OfType<InteractionOutcome>().First(o => o.From == initiator.Id && o.To == recipient.Id);
            initiator.ReceiveEvent(outcome);
            initiator.Tick(now + dt, dt);

            var recipientAfter = recipient.Snapshot.Relationships.Edges[initiator.Id];
            var initiatorAfter = initiator.Snapshot.Relationships.Edges[recipient.Id];

            Assert.IsTrue(recipientAfter.PositiveInteractionCount > recipientBefore.PositiveInteractionCount);
            Assert.IsTrue(recipientAfter.Familiarity > recipientBefore.Familiarity);
            Assert.IsTrue(initiatorAfter.PositiveInteractionCount > 0);
            Assert.IsTrue(initiatorAfter.Familiarity > 0);
        }

        /// <summary>
        /// Recipient-generated touch outcomes must reach both directional relationship snapshots.
        /// </summary>
        [TestMethod]
        public void RuntimeFlow_TouchOutcome_UpdatesRelationshipSnapshotsOnBothSides()
        {
            var now = WDateTime.New(100, 1, 1);
            var dt = WTimeSpan.FromHours(0.5);
            var initiator = CreateRuntimeHuman(SexBiology.Male);
            var recipient = CreateRuntimeHuman(SexBiology.Female);

            SeedRelationship(recipient, initiator.Id, closeness: 85, comfort: 90, trust: 85, romanticInterest: 70, sexualInterest: 75);
            SeedRelationship(initiator, recipient.Id, closeness: 80, comfort: 85, trust: 80, romanticInterest: 65, sexualInterest: 70);

            var recipientBefore = recipient.Snapshot.Relationships.Edges[initiator.Id];
            var initiatorBefore = initiator.Snapshot.Relationships.Edges[recipient.Id];

            recipient.ReceiveEvent(new TouchAttempted(now, initiator.Id, recipient.Id, TouchLevel.Intimate));
            recipient.Tick(now, dt);

            Assert.IsTrue(
                recipient.LastOutbox.OfType<TouchOutcome>().Any(o => o.From == initiator.Id && o.To == recipient.Id && o.Accepted),
                "Recipient should publish an accepted TouchOutcome.");

            var outcome = recipient.LastOutbox.OfType<TouchOutcome>().First(o => o.From == initiator.Id && o.To == recipient.Id);
            initiator.ReceiveEvent(outcome);
            initiator.Tick(now + dt, dt);

            var recipientAfter = recipient.Snapshot.Relationships.Edges[initiator.Id];
            var initiatorAfter = initiator.Snapshot.Relationships.Edges[recipient.Id];

            Assert.IsTrue(recipientAfter.SexualInterest > recipientBefore.SexualInterest);
            Assert.IsTrue(recipientAfter.Comfort > recipientBefore.Comfort);
            Assert.IsTrue(initiatorAfter.SexualInterest > initiatorBefore.SexualInterest);
            Assert.IsTrue(initiatorAfter.Comfort > initiatorBefore.Comfort);
        }

        #endregion Runtime wiring

        #region AttachmentProfile testy

        [TestMethod]
        public void AttachmentProfile_HighAvoidance_CapsCloseness()
        {
            // Dismissing (high Avoidance = 0.8): closeness cap = 100 - 0.8 * 40 = 68
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var avoidance = 0.8;
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3),
                new AttachmentProfile(Anxiety: 0.2, Avoidance: avoidance),
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate, Chronotype.Neutral);
            var ctx = BuildContext(self, personality);

            // Seed high closeness so ceiling is actually tested
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 80, Trust: 85, Familiarity: 50,
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    IntimateAffinity: 50, SexualInterest: 40,
                    Closeness: 60, Respect: 60, Comfort: 70,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60))
            }));

            // Several accepted interactions should not push Closeness above the cap
            for (var i = 0; i < 10; i++)
            {
                engine.Handle(new InteractionOutcome(_now, self, other, Accepted: true, Reason: "ok",
                    Act: SpeechAct.SelfDisclosure), ctx, _outbox);
            }

            var expectedCap = 100.0 - avoidance * DefaultCfg.ClosenessAvoidanceCap;
            Assert.IsTrue(engine.State.Edges[other].Closeness <= expectedCap + 0.01,
                $"Closeness {engine.State.Edges[other].Closeness:F1} should not exceed cap {expectedCap:F1}");
        }

        [TestMethod]
        public void AttachmentProfile_HighAnxiety_AmplifiesRejectionSting()
        {
            // Preoccupied (high Anxiety): rejection sting should be larger than for Secure
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure = BuildEngine();
            var engineAnxious = BuildEngine();

            var ctxSecure = BuildContext(self, new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate, Chronotype.Neutral));

            var ctxAnxious = BuildContext(self, new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3),
                AttachmentProfile.Preoccupied,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate, Chronotype.Neutral));

            // Seed identical edge in both
            var edge = new RelationshipEdge(self, other, Like: 70, Trust: 65, Familiarity: 30,
                AestheticAttraction: 60, PhysicalAttraction: 60,
                IntimateAffinity: 45, SexualInterest: 35,
                Closeness: 50, Respect: 60, Comfort: 60,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));
            engineSecure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));
            engineAnxious.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));

            var rejection = new InteractionOutcome(_now, self, other, Accepted: false, Reason: "no",
                Act: SpeechAct.Invite);

            engineSecure.Handle(rejection, ctxSecure, _outbox);
            engineAnxious.Handle(rejection, ctxAnxious, _outbox);

            var likeDeltaSecure = edge.Like - engineSecure.State.Edges[other].Like;
            var likeDeltaAnxious = edge.Like - engineAnxious.State.Edges[other].Like;

            Assert.IsTrue(likeDeltaAnxious > likeDeltaSecure,
                $"Anxious sting ({likeDeltaAnxious:F2}) should exceed Secure sting ({likeDeltaSecure:F2})");
        }

        [TestMethod]
        public void AttachmentProfile_HighAvoidance_ReducesRepairGain()
        {
            // Dismissing (high Avoidance): RepairAttempt.Accepted gives less Trust
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure = BuildEngine();
            var engineDismissing = BuildEngine();

            var ctxSecure = BuildContext(self, new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3), AttachmentProfile.Secure,
                CommunicationStyle.Direct, new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate, Chronotype.Neutral));

            var ctxDismissing = BuildContext(self, new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.3), AttachmentProfile.Dismissing,
                CommunicationStyle.Direct, new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate, Chronotype.Neutral));

            var edge = new RelationshipEdge(self, other, Like: 60, Trust: 50, Familiarity: 30,
                AestheticAttraction: 60, PhysicalAttraction: 60,
                IntimateAffinity: 30, SexualInterest: 20,
                Closeness: 40, Respect: 55, Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));
            engineSecure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));
            engineDismissing.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));

            var repair = new RepairAttempt(_now, self, other, Accepted: true);
            engineSecure.Handle(repair, ctxSecure, _outbox);
            engineDismissing.Handle(repair, ctxDismissing, _outbox);

            var trustGainSecure = engineSecure.State.Edges[other].Trust - edge.Trust;
            var trustGainDismissing = engineDismissing.State.Edges[other].Trust - edge.Trust;

            Assert.IsTrue(trustGainDismissing < trustGainSecure,
                $"Dismissing repair gain ({trustGainDismissing:F2}) should be less than Secure ({trustGainSecure:F2})");
        }

        #endregion AttachmentProfile testy

        #region TransgressionResidue testy

        [TestMethod]
        public void TransgressionResidue_MicroNegativeAccumulates()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 55, Attraction: 40), ctx, _outbox);
            Assert.AreEqual(0, engine.State.Edges[other].TransgressionResidue, "Initial residue should be 0");

            // MicroNegative(A=victim=self, B=perpetrator=other): self's edge to other gains residue
            engine.Handle(new MicroNegative(_now, self, other, "snub"), ctx, _outbox);
            engine.Handle(new MicroNegative(_now, self, other, "snub"), ctx, _outbox);
            engine.Handle(new MicroNegative(_now, self, other, "snub"), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].TransgressionResidue > 0,
                "After 3 MicroNegatives, TransgressionResidue should be positive");
        }

        [TestMethod]
        public void TransgressionResidue_PowerLawDecay()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            // Seed edge with known TransgressionResidue
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 60, Trust: 60, Familiarity: 30,
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    IntimateAffinity: 30, SexualInterest: 20,
                    Closeness: 40, Respect: 55, Comfort: 50,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60),
                    TransgressionResidue: 30)
            }));

            // Tick 20 days
            engine.Tick(_now, WTimeSpan.FromDays(20), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].TransgressionResidue < 30,
                "TransgressionResidue should decay over 20 days");
        }

        [TestMethod]
        public void TransgressionResidue_RepairAccepted_Reduces()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 60, Trust: 60, Familiarity: 30,
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    IntimateAffinity: 30, SexualInterest: 20,
                    Closeness: 40, Respect: 55, Comfort: 50,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60),
                    TransgressionResidue: 20)
            }));

            engine.Handle(new RepairAttempt(_now, self, other, Accepted: true), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].TransgressionResidue < 20,
                "Accepted RepairAttempt should reduce TransgressionResidue");
        }

        #endregion TransgressionResidue testy

        #region Navarro gap rule testy

        [TestMethod]
        public void NavarrGap_TriggersAcceleratedDecay()
        {
            // Two identical edges: one with recent contact, one with contact 150 days ago.
            // The stale one (150 > 8 × 14 = 112 days) should decay faster.
            var self = new HumanId(Guid.NewGuid());
            var recent = new HumanId(Guid.NewGuid());
            var stale = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var engine = BuildEngine();

            var now = _now;
            var recentContact = now - WTimeSpan.FromDays(5);
            var staleContact = now - WTimeSpan.FromDays(150);

            var baseEdge = new RelationshipEdge(self, recent,
                Like: 70, Trust: 70, Familiarity: 50,
                AestheticAttraction: 60, PhysicalAttraction: 60,
                IntimateAffinity: 40, SexualInterest: 30,
                Closeness: 60, Respect: 60, Comfort: 65,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [recent] = baseEdge with { A = self, B = recent, LastContactTime = recentContact },
                [stale] = baseEdge with { A = self, B = stale, LastContactTime = staleContact }
            }));

            engine.Tick(now, WTimeSpan.FromDays(7), ctx, _outbox);

            var recentCloseness = engine.State.Edges[recent].Closeness;
            var staleCloseness = engine.State.Edges[stale].Closeness;

            Assert.IsTrue(staleCloseness < recentCloseness,
                $"Stale edge Closeness ({staleCloseness:F1}) should be lower than recent ({recentCloseness:F1}) due to Navarro gap");
        }

        #endregion Navarro gap rule testy

        #region CommunalStrength testy

        [TestMethod]
        public void CommunalStrength_GrowsFromIntimateTouch()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 50), ctx, _outbox);
            var before = engine.State.Edges[other].CommunalStrength;

            // Intimate touch (self = recipient)
            engine.Handle(new TouchOutcome(_now, other, self, TouchLevel.Intimate, Accepted: true, Reason: "ok"), ctx, _outbox);

            var after = engine.State.Edges[other].CommunalStrength;
            Assert.IsTrue(after > before,
                $"CommunalStrength should increase after accepted intimate touch (before={before:F1}, after={after:F1})");
        }

        #endregion CommunalStrength testy

        #region Familiarity non-monotonicity testy

        [TestMethod]
        public void FamiliarityDissonance_HighFamiliarityWithoutContact_ReducesLike()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            var now = _now;
            var lastContact = now - WTimeSpan.FromDays(40);  // 40 days since last contact

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 65, Trust: 60, Familiarity: 70,  // Familiarity > 55 ✓
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    IntimateAffinity: 30, SexualInterest: 20,
                    Closeness: 50, Respect: 55, Comfort: 55,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60),
                    LastContactTime: lastContact)
            }));

            var likeBefore = engine.State.Edges[other].Like;
            engine.Tick(now, WTimeSpan.FromDays(10), ctx, _outbox);
            var likeAfter = engine.State.Edges[other].Like;

            // Normal decay + familiarity dissonance penalty → Like should drop
            Assert.IsTrue(likeAfter < likeBefore,
                $"Like should decrease with Familiarity>55 and 40-day absence (before={likeBefore:F1}, after={likeAfter:F1})");
        }

        #endregion Familiarity non-monotonicity testy

        #region RejectionNeedsThreat testy

        [TestMethod]
        public void RejectionNeedsThreat_PublishedOnInviteRejected()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 50), ctx, outbox);

            // Self = initiator, other = recipient who rejects
            engine.Handle(new InteractionOutcome(_now, self, other, Accepted: false, Reason: "no",
                Act: SpeechAct.Invite), ctx, outbox);

            var events = outbox.Drain();
            var threat = events.OfType<RejectionNeedsThreat>().FirstOrDefault();
            Assert.IsNotNull(threat, "RejectionNeedsThreat should be published when InviteIntimacy is rejected");
            Assert.AreEqual(self, threat.Rejected, "Rejected should be the initiator");
            Assert.IsTrue(threat.IsIntimateAdvance, "IsIntimateAdvance should be true for Invite");
            Assert.IsTrue(threat.Intensity >= 0.72, "Intensity should be >= baseline 0.72");
        }

        #endregion RejectionNeedsThreat testy

        #region R1 — Third-party gossip testy

        [TestMethod]
        public void ThirdPartyGossip_PositiveAct_IncreasesObserverTrustOfActor()
        {
            // Observer B watches actor A do a MicroPositive to target C.
            // B's relationship engine receives ThirdPartyActionObserved → Trust/Like of A should rise.
            var engine = BuildEngine();
            var observer = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());

            // Observer context (observer's engine processes the event)
            var ctx = BuildContext(observer);

            // Seed a neutral edge from observer → actor
            engine.Handle(new FirstImpressionFormed(_now, observer, actor, Like: 50, Attraction: 30), ctx, _outbox);
            var trustBefore = engine.State.Edges[actor].Trust;

            engine.Handle(new ThirdPartyActionObserved(
                _now, Observer: observer, Actor: actor, Target: target,
                Valence: +1.0, Type: ThirdPartyObservationType.PositiveAct), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[actor].Trust > trustBefore,
                $"Trust of actor should rise after positive gossip (before={trustBefore:F1}, after={engine.State.Edges[actor].Trust:F1})");
        }

        [TestMethod]
        public void ThirdPartyGossip_NegativeAct_DecreasesObserverTrustOfActor()
        {
            var engine = BuildEngine();
            var observer = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(observer);

            engine.Handle(new FirstImpressionFormed(_now, observer, actor, Like: 60, Attraction: 30), ctx, _outbox);
            var trustBefore = engine.State.Edges[actor].Trust;

            engine.Handle(new ThirdPartyActionObserved(
                _now, Observer: observer, Actor: actor, Target: target,
                Valence: -1.0, Type: ThirdPartyObservationType.NegativeAct), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[actor].Trust < trustBefore,
                $"Trust should fall after negative gossip (before={trustBefore:F1}, after={engine.State.Edges[actor].Trust:F1})");
        }

        [TestMethod]
        public void MicroPositive_WithObservers_EmitsThirdPartyEvents()
        {
            // When MicroPositive is processed and Observers are in InteractionSurface,
            // ThirdPartyActionObserved events should appear in the outbox for each observer.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());   // mn.B = perpetrator
            var observer = new HumanId(Guid.NewGuid());
            var outbox = new EventCollector();

            // Context with an observer in InteractionSurface
            var ctx = BuildContextWithObservers(self, new[] { observer });
            engine.Handle(new FirstImpressionFormed(_now, self, actor, Like: 50, Attraction: 30), ctx, outbox);

            engine.Handle(new MicroPositive(_now, self, actor, "smile"), ctx, outbox);

            var events = outbox.Drain();
            var gossip = events.OfType<ThirdPartyActionObserved>().ToList();

            Assert.AreEqual(1, gossip.Count, "One ThirdPartyActionObserved should be emitted per observer");
            Assert.AreEqual(observer, gossip[0].Observer);
            Assert.AreEqual(actor, gossip[0].Actor);
            Assert.AreEqual(ThirdPartyObservationType.PositiveAct, gossip[0].Type);
        }

        #endregion R1 — Third-party gossip testy

        #region R2 — Contempt terminal marker testy

        [TestMethod]
        public void Contempt_SetsIsContemptuouslyDestroyed_Flag()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 70, Attraction: 40), ctx, _outbox);
            Assert.IsFalse(engine.State.Edges[other].IsContemptuouslyDestroyed,
                "Flag should be false before contempt");

            engine.Handle(new ContemptuousActPerformed(_now, From: self, To: other), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].IsContemptuouslyDestroyed,
                "Flag should be true after contempt");
        }

        [TestMethod]
        public void Contempt_CausesMajorTrustAndLikeDrop()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 70, Attraction: 40), ctx, _outbox);
            var trustBefore = engine.State.Edges[other].Trust;
            var likeBefore = engine.State.Edges[other].Like;

            engine.Handle(new ContemptuousActPerformed(_now, From: self, To: other), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].Trust < trustBefore - 20,
                $"Trust should drop by >20 after contempt (before={trustBefore:F1}, after={engine.State.Edges[other].Trust:F1})");
            Assert.IsTrue(engine.State.Edges[other].Like < likeBefore - 15,
                $"Like should drop by >15 after contempt (before={likeBefore:F1}, after={engine.State.Edges[other].Like:F1})");
        }

        [TestMethod]
        public void Contempt_RepairAttempt_CannotExceedCeiling()
        {
            // After contempt, RepairAttempt.Accepted cannot rebuild Trust above 30 or Closeness above 20.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 70, Attraction: 40), ctx, _outbox);
            engine.Handle(new ContemptuousActPerformed(_now, From: self, To: other), ctx, _outbox);

            // Apply many successful repairs
            for (var i = 0; i < 20; i++)
                engine.Handle(new RepairAttempt(_now, self, other, Accepted: true), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].Trust <= 30.0 + 0.01,
                $"Trust should not exceed ceiling of 30 after contempt (got={engine.State.Edges[other].Trust:F1})");
            Assert.IsTrue(engine.State.Edges[other].Closeness <= 20.0 + 0.01,
                $"Closeness should not exceed ceiling of 20 after contempt (got={engine.State.Edges[other].Closeness:F1})");
        }

        #endregion R2 — Contempt terminal marker testy

        #region R3 — ExchangeStrength testy

        [TestMethod]
        public void ExchangeStrength_GrowsFromMetaInteraction()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);
            var exchangeBefore = engine.State.Edges[other].ExchangeStrength;

            // Accepted Meta interaction → ExchangeStrength should increase
            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok",
                Act: SpeechAct.Meta), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[other].ExchangeStrength > exchangeBefore,
                $"ExchangeStrength should grow from Meta interaction (before={exchangeBefore:F1}, " +
                $"after={engine.State.Edges[other].ExchangeStrength:F1})");
        }

        [TestMethod]
        public void ExchangeStrength_DoesNotGrow_FromSmallTalk()
        {
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);
            var exchangeBefore = engine.State.Edges[other].ExchangeStrength;

            engine.Handle(new InteractionOutcome(_now, other, self, Accepted: true, Reason: "ok",
                Act: SpeechAct.SmallTalk), ctx, _outbox);

            Assert.AreEqual(exchangeBefore, engine.State.Edges[other].ExchangeStrength,
                "ExchangeStrength should NOT change from SmallTalk");
        }

        #endregion R3 — ExchangeStrength testy

        #region S2 — ResponsiveDesireLevel testy

        [TestMethod]
        public void ResponsiveDesireLevel_GrowsInLongTermCommunalRelationship()
        {
            // Edge with high CommunalStrength + high PositiveInteractionCount should
            // drift ResponsiveDesireLevel toward its target over many days.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 75, Trust: 80, Familiarity: 60,
                    AestheticAttraction: 65, PhysicalAttraction: 65,
                    IntimateAffinity: 55, SexualInterest: 45,
                    Closeness: 70, Respect: 65, Comfort: 75,
                    Breakdown: new DomainBreakdown(50, 60, 65, 65, 60),
                    PositiveInteractionCount: 50,  // > 30 threshold
                    CommunalStrength: 75)           // > 60 threshold
            }));

            var before = engine.State.Edges[other].ResponsiveDesireLevel;
            engine.Tick(_now, WTimeSpan.FromDays(90), ctx, _outbox);
            var after = engine.State.Edges[other].ResponsiveDesireLevel;

            Assert.IsTrue(after > before,
                $"ResponsiveDesireLevel should grow in communal long-term relationship (before={before:F1}, after={after:F1})");
        }

        [TestMethod]
        public void ResponsiveDesireLevel_StaysZero_InNewRelationship()
        {
            // Low CommunalStrength + low PositiveInteractionCount → ResponsiveDesireLevel stays at 0
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 40), ctx, _outbox);

            engine.Tick(_now, WTimeSpan.FromDays(60), ctx, _outbox);

            Assert.AreEqual(0, engine.State.Edges[other].ResponsiveDesireLevel, 0.01,
                "New relationship with low CommunalStrength should have ResponsiveDesireLevel = 0");
        }

        #endregion S2 — ResponsiveDesireLevel testy

        #region A2 — Halo efekt seeding testy

        [TestMethod]
        public void Halo_HighAttraction_SeedsHigherComfortAndRespect_ThanLowAttraction()
        {
            // High-attraction first impression should seed higher Comfort and Respect than low.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            var engineLow = BuildEngine();
            var engineHigh = BuildEngine();

            // Low attraction: Attraction=10
            engineLow.Handle(new FirstImpressionFormed(_now, self, other,
                Like: 45, Attraction: 10, BasePhysical: 5, PreferenceMatch: 5), ctx, _outbox);

            // High attraction: Attraction=90
            engineHigh.Handle(new FirstImpressionFormed(_now, self, other,
                Like: 70, Attraction: 90, BasePhysical: 38, PreferenceMatch: 32), ctx, _outbox);

            var lowEdge = engineLow.State.Edges[other];
            var highEdge = engineHigh.State.Edges[other];

            Assert.IsTrue(highEdge.Comfort > lowEdge.Comfort,
                $"High attraction should seed higher Comfort (high={highEdge.Comfort:F1}, low={lowEdge.Comfort:F1})");
            Assert.IsTrue(highEdge.Respect > lowEdge.Respect,
                $"High attraction should seed higher Respect (high={highEdge.Respect:F1}, low={lowEdge.Respect:F1})");
            Assert.IsTrue(highEdge.Trust >= lowEdge.Trust,
                $"High attraction should seed at least equal Trust (high={highEdge.Trust:F1}, low={lowEdge.Trust:F1})");
        }

        #endregion A2 — Halo efekt seeding testy

        #region Investment model (Rusbult) testy

        [TestMethod]
        public void ParameterlessConfig_MirrorsInvestmentModelDefaults()
        {
            // The parameterless ctor (DI options binding) must stay in sync with the positional defaults.
            var cfg = new RelationshipsConfig();
            Assert.AreEqual(45.0, cfg.ComparisonLevelBaseline, 0.0001);
            Assert.AreEqual(0.6, cfg.CommitmentInvestmentWeight, 0.0001);
            Assert.AreEqual(0.5, cfg.CommitmentAlternativeWeight, 0.0001);
            Assert.AreEqual(0.08, cfg.CommitmentDriftPerDay, 0.0001);
            Assert.AreEqual(0.02, cfg.InvestmentGrowthPerDay, 0.0001);
            Assert.AreEqual(30.0, cfg.RomanticEdgeIntimacyThreshold, 0.0001);
            Assert.AreEqual(0.6, cfg.CommitmentDecayResistance, 0.0001);
        }

        [TestMethod]
        public void NewEdge_HasZeroInvestmentModelFields()
        {
            // A freshly created edge must start with neutral investment-model state.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 60, Attraction: 50), ctx, _outbox);

            var edge = engine.State.Edges[other];
            Assert.AreEqual(0.0, edge.Commitment, 0.0001, "Commitment should start at 0");
            Assert.AreEqual(0.0, edge.InvestmentSize, 0.0001, "InvestmentSize should start at 0");
            Assert.AreEqual(0.0, edge.AlternativeQuality, 0.0001, "AlternativeQuality should start at 0");
        }

        [TestMethod]
        public void Commitment_GrowsTowardTarget_InSatisfyingHighInvestmentBond()
        {
            // High satisfaction (Like/Closeness/Comfort) + high InvestmentSize → Commitment rises from 0.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 80, Trust: 80, Familiarity: 60,
                    AestheticAttraction: 50, PhysicalAttraction: 50,
                    IntimateAffinity: 10, SexualInterest: 10,
                    Closeness: 80, Respect: 65, Comfort: 80,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    InvestmentSize: 50.0)
            }));

            var before = engine.State.Edges[other].Commitment;
            engine.Tick(_now, WTimeSpan.FromDays(3), ctx, _outbox);
            var after = engine.State.Edges[other].Commitment;

            Assert.IsTrue(after > before,
                $"Commitment should grow in a satisfying high-investment bond (before={before:F1}, after={after:F1})");
        }

        [TestMethod]
        public void AlternativeQuality_StaysZero_ForPlatonicEdge()
        {
            // IntimateAffinity below the romantic threshold + KinRole.None → CL_alt = 0.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 70, Trust: 70, Familiarity: 60,
                    AestheticAttraction: 80, PhysicalAttraction: 80,
                    IntimateAffinity: 10, SexualInterest: 10,
                    Closeness: 60, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 30)
            }));

            engine.Tick(_now, WTimeSpan.FromDays(5), ctx, _outbox);

            Assert.AreEqual(0.0, engine.State.Edges[other].AlternativeQuality, 0.0001,
                "Platonic (sub-threshold, non-partner) edge should have AlternativeQuality = 0");
        }

        [TestMethod]
        public void AlternativeQuality_RisesWhenAttractiveAlternativeExists()
        {
            // A partner edge plus a second, highly attractive non-partner edge → CL_alt > 0 on the partner edge.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var partner = new HumanId(Guid.NewGuid());
            var alternative = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [partner] = new RelationshipEdge(self, partner,
                    Like: 70, Trust: 70, Familiarity: 70,
                    AestheticAttraction: 40, PhysicalAttraction: 40,
                    IntimateAffinity: 70, SexualInterest: 60,
                    Closeness: 70, Respect: 65, Comfort: 70,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    KinRole: KinRole.Partner),
                [alternative] = new RelationshipEdge(self, alternative,
                    Like: 55, Trust: 50, Familiarity: 30,
                    AestheticAttraction: 80, PhysicalAttraction: 80,
                    IntimateAffinity: 20, SexualInterest: 20,
                    Closeness: 20, Respect: 55, Comfort: 40,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 5)
            }));

            engine.Tick(_now, WTimeSpan.FromDays(1), ctx, _outbox);

            Assert.IsTrue(engine.State.Edges[partner].AlternativeQuality > 0.0,
                $"Partner edge CL_alt should rise when an attractive alternative exists (got {engine.State.Edges[partner].AlternativeQuality:F1})");
        }

        [TestMethod]
        public void Commitment_ResistsClosenessDecay()
        {
            // Two otherwise-identical platonic edges; the high-investment (high-commitment) one
            // should retain more Closeness after the same decay tick (stickiness).
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            RelationshipEdge MakeEdge(double commitment, double investment) => new(self, other,
                Like: 70, Trust: 70, Familiarity: 60,
                AestheticAttraction: 40, PhysicalAttraction: 40,
                IntimateAffinity: 10, SexualInterest: 10,
                Closeness: 60, Respect: 60, Comfort: 70,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PositiveInteractionCount: 50,
                Commitment: commitment,
                InvestmentSize: investment);

            var committed = BuildEngine();
            var uncommitted = BuildEngine();
            var ctx = BuildContext(self);

            committed.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = MakeEdge(90, 80) }));
            uncommitted.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = MakeEdge(0, 0) }));

            committed.Tick(_now, WTimeSpan.FromDays(10), ctx, _outbox);
            uncommitted.Tick(_now, WTimeSpan.FromDays(10), ctx, _outbox);

            var committedCloseness = committed.State.Edges[other].Closeness;
            var uncommittedCloseness = uncommitted.State.Edges[other].Closeness;

            Assert.IsTrue(committedCloseness > uncommittedCloseness,
                $"High-commitment bond should resist Closeness decay (committed={committedCloseness:F2}, uncommitted={uncommittedCloseness:F2})");
        }

        [TestMethod]
        public void Commitment_DropsWhenAlternativeQualityHigh()
        {
            // Low satisfaction + a high-quality alternative drives Commitment down toward 0.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var partner = new HumanId(Guid.NewGuid());
            var alternative = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [partner] = new RelationshipEdge(self, partner,
                    Like: 30, Trust: 35, Familiarity: 60,
                    AestheticAttraction: 30, PhysicalAttraction: 30,
                    IntimateAffinity: 55, SexualInterest: 30,
                    Closeness: 30, Respect: 40, Comfort: 30,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    Commitment: 50.0,
                    InvestmentSize: 0.0,
                    KinRole: KinRole.Partner),
                [alternative] = new RelationshipEdge(self, alternative,
                    Like: 60, Trust: 55, Familiarity: 30,
                    AestheticAttraction: 85, PhysicalAttraction: 85,
                    IntimateAffinity: 25, SexualInterest: 25,
                    Closeness: 25, Respect: 55, Comfort: 45,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 5)
            }));

            var before = engine.State.Edges[partner].Commitment;
            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, _outbox);
            var after = engine.State.Edges[partner].Commitment;

            Assert.IsTrue(after < before,
                $"Commitment should drop under low satisfaction + high CL_alt (before={before:F1}, after={after:F1})");
        }

        [TestMethod]
        public void Dissolution_EmittedWhenPartnerCommitmentDropsBelowThreshold()
        {
            // Partner edge with low satisfaction + a strong alternative drives Commitment below the
            // dissolution threshold, emitting RelationshipDissolutionConsidered once.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var partner = new HumanId(Guid.NewGuid());
            var alternative = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [partner] = new RelationshipEdge(self, partner,
                    Like: 25, Trust: 30, Familiarity: 60,
                    AestheticAttraction: 25, PhysicalAttraction: 25,
                    IntimateAffinity: 50, SexualInterest: 25,
                    Closeness: 25, Respect: 35, Comfort: 25,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    Commitment: 40.0,
                    InvestmentSize: 0.0,
                    KinRole: KinRole.Partner),
                [alternative] = new RelationshipEdge(self, alternative,
                    Like: 60, Trust: 55, Familiarity: 30,
                    AestheticAttraction: 90, PhysicalAttraction: 90,
                    IntimateAffinity: 25, SexualInterest: 25,
                    Closeness: 25, Respect: 55, Comfort: 45,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 5)
            }));

            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, outbox);

            var events = outbox.Drain().OfType<RelationshipDissolutionConsidered>().ToList();
            Assert.AreEqual(1, events.Count, "Should emit exactly one dissolution event on the downward crossing");
            Assert.AreEqual(self, events[0].Self);
            Assert.AreEqual(partner, events[0].Partner);
            Assert.IsTrue(engine.State.Edges[partner].DissolutionConsidered, "Latch should be set after emission");
        }

        [TestMethod]
        public void Dissolution_NotEmittedTwice_WhileBelowThreshold()
        {
            // Once below threshold the latch suppresses re-emission on subsequent ticks.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var partner = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [partner] = new RelationshipEdge(self, partner,
                    Like: 20, Trust: 25, Familiarity: 60,
                    AestheticAttraction: 20, PhysicalAttraction: 20,
                    IntimateAffinity: 50, SexualInterest: 20,
                    Closeness: 20, Respect: 30, Comfort: 20,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    Commitment: 30.0,
                    InvestmentSize: 0.0,
                    KinRole: KinRole.Partner)
            }));

            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, outbox);
            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, outbox);

            var events = outbox.Drain().OfType<RelationshipDissolutionConsidered>().ToList();
            Assert.AreEqual(1, events.Count, "Latch must prevent a second emission while still below threshold");
        }

        [TestMethod]
        public void Dissolution_NotEmittedForNonPartnerEdge()
        {
            // A romantic-but-non-partner edge never emits dissolution, even at very low commitment.
            var engine = BuildEngine();
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);
            var outbox = new EventCollector();

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 20, Trust: 25, Familiarity: 60,
                    AestheticAttraction: 20, PhysicalAttraction: 20,
                    IntimateAffinity: 50, SexualInterest: 20,
                    Closeness: 20, Respect: 30, Comfort: 20,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 50,
                    Commitment: 10.0,
                    InvestmentSize: 0.0,
                    KinRole: KinRole.None)
            }));

            engine.Tick(_now, WTimeSpan.FromDays(10), ctx, outbox);

            Assert.IsFalse(outbox.Drain().OfType<RelationshipDissolutionConsidered>().Any(),
                "Non-partner edges must not emit dissolution events");
        }

        #endregion Investment model (Rusbult) testy

        #region Factory metody

        /// <summary>Sestaví engine s konfigurací dle <see cref="DefaultCfg"/>.</summary>
        private DefaultRelationshipsEngine BuildEngine(RelationshipsConfig? config = null, SocialFidelityLevel socialFidelity = SocialFidelityLevel.Full) => new DefaultRelationshipsEngine(
            Options.Create(config ?? DefaultCfg),
            LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
            new FixedSocialFidelityPolicy(socialFidelity));

        /// <summary>
        /// Creates a fully wired runtime human with deterministic acceptance for event-flow tests.
        /// </summary>
        private IHuman CreateRuntimeHuman(SexBiology biology)
        {
            var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var physioFactory = ServiceProvider.GetRequiredService<IPhysiologyEngineFactory>();
            var psychFactory = ServiceProvider.GetRequiredService<IPsychologyEngineFactory>();
            var random = new AlwaysTrueRandom();

            var id = new HumanId(Guid.NewGuid());
            var identity = new Identity(
                new Name { Original = "Test", Familiar = new[] { "Test" } },
                new Surname { Male = "Human", Female = "Human" },
                WDateOnly.New(80, 1, 1));

            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var rngFactory = ServiceProvider.GetRequiredService<IRandomSourceFactory>();
            var geneticBlueprint = new AppearanceGenerator(rngFactory).GenerateBlueprint(biology, seed: 42);

            var physio = physioFactory.Create(random, biology, identity.BirthDate, WDateOnly.New(100, 1, 1));
            var psych = psychFactory.Create(random);
            var behavior = ServiceProvider.GetRequiredService<IBehaviorEngine>();
            var interact = ServiceProvider.GetRequiredService<IInteractionEngine>();
            var relations = ServiceProvider.GetRequiredService<IRelationshipsEngine>();
            var memory = ServiceProvider.GetRequiredService<IMemoryEngine>();
            var semanticMemory = ServiceProvider.GetRequiredService<ISemanticMemoryEngine>();
            var goal = ServiceProvider.GetRequiredService<IGoalEngine>();
            var schedule = ServiceProvider.GetRequiredService<IDailyScheduleEngine>();
            var values = ServiceProvider.GetRequiredService<IValuesEngine>();
            var selfConcept = ServiceProvider.GetRequiredService<ISelfConceptEngine>();
            var interests = ServiceProvider.GetRequiredService<IInterestEngine>();

            var snapshot = new EnginesSnapshot(
                physio.State,
                psych.State,
                behavior.State,
                interact.State,
                relations.State,
                memory.State,
                semanticMemory.State,
                Goals: goal.State,
                Schedule: schedule.State,
                Values: values.State,
                SelfConcept: selfConcept.State,
                Interests: interests.State);

            return new OrchestratedHuman(
                id,
                identity,
                biology,
                personality,
                geneticBlueprint,
                attractionProfile: null,
                bus: new NullEventBus(),
                scheduler: new NullScheduler(),
                random: random,
                logger: loggerFactory.CreateLogger($"TestHuman[{id.Value}]"),
                physio: physio,
                psych: psych,
                behavior: behavior,
                interact: interact,
                relations: relations,
                memory: memory,
                semanticMemory: semanticMemory,
                goal: goal,
                schedule: schedule,
                values: values,
                selfConcept: selfConcept,
                interests: interests,
                initialSnapshot: snapshot,
                behaviorCadencePolicy: null);
        }

        /// <summary>
        /// Seeds one directed relationship edge into a runtime human snapshot.
        /// </summary>
        private static void SeedRelationship(
            IHuman owner,
            HumanId other,
            double closeness,
            double comfort,
            double trust,
            double romanticInterest,
            double sexualInterest)
        {
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    owner.Id,
                    other,
                    Like: 60,
                    Trust: trust,
                    Familiarity: 40,
                    AestheticAttraction: 65,
                    PhysicalAttraction: 65,
                    IntimateAffinity: romanticInterest,
                    SexualInterest: sexualInterest,
                    Closeness: closeness,
                    Respect: 60,
                    Comfort: comfort,
                    Breakdown: new DomainBreakdown(50, 50, 60, 55, 60),
                    PositiveInteractionCount: 2)
            });

            owner.RestoreSnapshot(owner.Snapshot with { Relationships = relationships });
        }

        /// <summary>
        /// Sestaví minimální kontext — RelationshipsEngine nepotřebuje v Handle() téměř nic
        /// kromě ctx.Id a ctx.Snapshot.Psychology (pro Tick).
        /// </summary>
        private IHumanContext BuildContext(HumanId id, Personality? personality = null)
            => BuildContextWithObservers(id, null, personality);

        private IHumanContext BuildContextWithObservers(
            HumanId id,
            IReadOnlyList<HumanId>? observers,
            Personality? personality = null)
        {
            personality ??= new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var psych = new PsychologyState(
                Valence: 0.0, Arousal: 0.5, Dominance: 0.5,
                Stress: 0, CognitiveLoad: 0, DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown, observers),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = id,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new AlwaysTrueRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        /// <summary>
        /// IRandomSource který vždy vrátí true pro Chance() — interakce jsou vždy přijaty
        /// pokud to test explicitně nevyžaduje jinak.
        /// </summary>
        private sealed class AlwaysTrueRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => true;
        }

        #endregion Factory metody

        // ══════════════════════════════════════════════════════════════════════════════
        // Periodický edge snapshot (EventId 2005) z decay průchodu
        // ══════════════════════════════════════════════════════════════════════════════

        #region Periodický edge snapshot (2005)

        /// <summary>
        /// Decay průchod musí periodicky emitovat snapshot hran (2005): okamžitě při
        /// prvním ticku po startu (logy mohly být rotovány), poté nejdřív po uplynutí
        /// <see cref="RelationshipsConfig.EdgeSnapshotIntervalDays"/> herního času.
        /// </summary>
        [TestMethod]
        public void Tick_PeriodicEdgeSnapshot_EmitsImmediatelyThenThrottles()
        {
            // Arrange — engine s capture loggerem (2005 je Debug level)
            var capture = new CapturingLoggerProvider();
            var engine = new DefaultRelationshipsEngine(
                Options.Create(DefaultCfg),
                LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Debug); b.AddProvider(capture); }),
                new FixedSocialFidelityPolicy(SocialFidelityLevel.Full));

            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildContext(self);

            engine.Handle(new FirstImpressionFormed(_now, self, other, Like: 50, Attraction: 40), ctx, _outbox);
            capture.Clear();   // Handle mutace loguje 2005 samo — zajímá nás jen decay cesta

            // Act + Assert 1 — první decay tick emituje snapshot okamžitě (saturovaný čítač)
            engine.Tick(_now, WTimeSpan.FromHours(1), ctx, _outbox);
            Assert.AreEqual(1, capture.CountByEventId(2005),
                "První decay tick po startu musí emitnout 2005 snapshot každé hrany.");

            // Act + Assert 2 — další tick hluboko pod intervalem nesmí emitovat znovu
            capture.Clear();
            engine.Tick(_now, WTimeSpan.FromHours(1), ctx, _outbox);
            Assert.AreEqual(0, capture.CountByEventId(2005),
                "Tick pod EdgeSnapshotIntervalDays nesmí emitovat další 2005 snapshot.");

            // Act + Assert 3 — po překročení intervalu se snapshot emituje znovu
            engine.Tick(_now, WTimeSpan.FromDays(2), ctx, _outbox);
            Assert.AreEqual(1, capture.CountByEventId(2005),
                "Po uplynutí EdgeSnapshotIntervalDays se musí 2005 snapshot emitovat znovu.");
        }

        /// <summary>Minimální capture provider — sbírá EventId všech zalogovaných zpráv.</summary>
        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            private readonly List<int> _eventIds = new();

            public ILogger CreateLogger(string categoryName) => new CapturingLogger(_eventIds);

            public void Dispose()
            {
            }

            public void Clear() => _eventIds.Clear();

            public int CountByEventId(int id) => _eventIds.Count(e => e == id);

            private sealed class CapturingLogger : ILogger
            {
                private readonly List<int> _eventIds;

                public CapturingLogger(List<int> eventIds) => _eventIds = eventIds;

                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                public bool IsEnabled(LogLevel logLevel) => true;

                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                    => _eventIds.Add(eventId.Id);
            }
        }

        #endregion Periodický edge snapshot (2005)
    }

    // =========================================================================
    // Dominance / Prestige — Cheng et al. 2013; Redhead et al. 2019
    // ThirdPartyActionObserved updates PerceivedDominance / PerceivedPrestige on
    // the observer's edge toward the actor.
    // =========================================================================

    [TestClass]
    public class DominancePrestigeTests : TestBase
    {
        private IEventCollector _outbox = default!;
        private WDateTime _now;

        private static readonly RelationshipsConfig Cfg = new RelationshipsConfig(
            DecayPerDay: 0.0);  // disable decay so only the event effect is visible

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
        }

        private DefaultRelationshipsEngine BuildEngine(RelationshipsConfig? cfg = null)
            => new DefaultRelationshipsEngine(
                Options.Create(cfg ?? Cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new FixedSocialFidelityPolicy(SocialFidelityLevel.Full));

        private IHumanContext BuildCtx(HumanId self)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.3, 0.3, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
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

        private IHumanContext BuildCtxWithNorm(HumanId self, RelationalModel model)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.3, 0.3, SurfaceKind.Unknown,
                    NormContext: new SocialNormContext(SocialNormKind.Greeting, 0.5, 0.5, model)),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
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

        // ------------------------------------------------------------------
        // Authority Ranking: deference + loyalty toward a perceived superior
        // ------------------------------------------------------------------

        [TestMethod]
        public void AuthorityRanking_InteractionWithSuperior_BuildsExtraDeferenceAndLoyalty()
        {
            var self = new HumanId(Guid.NewGuid());
            var superior = new HumanId(Guid.NewGuid());

            // self perceives `superior` as high-prestige — i.e. a superior in the hierarchy.
            RelationshipEdge Seed() => new RelationshipEdge(
                self, superior, Like: 50, Trust: 50, Familiarity: 40,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 20, SexualInterest: 20,
                Closeness: 50, Respect: 50, Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PerceivedPrestige: 90);

            var smallTalk = new InteractionOutcome(_now, superior, self, Accepted: true, Reason: "test", Act: SpeechAct.SmallTalk);

            // Baseline: no relational model on the surface.
            var baseEngine = BuildEngine();
            baseEngine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [superior] = Seed() }));
            baseEngine.Handle(smallTalk, BuildCtx(self), _outbox);
            var baseEdge = baseEngine.State.Edges[superior];

            // Authority-Ranking context.
            var arEngine = BuildEngine();
            arEngine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [superior] = Seed() }));
            arEngine.Handle(smallTalk, BuildCtxWithNorm(self, RelationalModel.AuthorityRanking), _outbox);
            var arEdge = arEngine.State.Edges[superior];

            Assert.IsTrue(arEdge.Respect > baseEdge.Respect,
                $"AR deference builds extra Respect toward a superior. base={baseEdge.Respect:F2}, AR={arEdge.Respect:F2}");
            Assert.IsTrue(arEdge.Trust > baseEdge.Trust,
                $"AR loyalty builds extra Trust toward a superior. base={baseEdge.Trust:F2}, AR={arEdge.Trust:F2}");
        }

        // ------------------------------------------------------------------
        // Test 1: PositiveAct → PerceivedPrestige roste
        // ------------------------------------------------------------------

        [TestMethod]
        public void ThirdPartyPositiveAct_IncreasesPerceivedPrestige()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var engine = BuildEngine();
            var ctx = BuildCtx(self);

            engine.Handle(new FirstImpressionFormed(_now, self, actor, Like: 50, Attraction: 40), ctx, _outbox);
            var prestigeBefore = engine.State.Edges[actor].PerceivedPrestige;

            // Act
            engine.Handle(new ThirdPartyActionObserved(
                OccurredAt: _now,
                Observer: self,
                Actor: actor,
                Target: target,
                Valence: 1.0,
                Type: ThirdPartyObservationType.PositiveAct), ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Edges[actor].PerceivedPrestige > prestigeBefore,
                $"PositiveAct musí zvýšit PerceivedPrestige. Před: {prestigeBefore:F1}, po: {engine.State.Edges[actor].PerceivedPrestige:F1}");
        }

        // ------------------------------------------------------------------
        // Test 2: NegativeAct → PerceivedDominance roste
        // ------------------------------------------------------------------

        [TestMethod]
        public void ThirdPartyNegativeAct_IncreasesPerceivedDominance()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var engine = BuildEngine();
            var ctx = BuildCtx(self);

            engine.Handle(new FirstImpressionFormed(_now, self, actor, Like: 50, Attraction: 40), ctx, _outbox);
            var dominanceBefore = engine.State.Edges[actor].PerceivedDominance;

            // Act
            engine.Handle(new ThirdPartyActionObserved(
                OccurredAt: _now,
                Observer: self,
                Actor: actor,
                Target: target,
                Valence: -1.0,
                Type: ThirdPartyObservationType.NegativeAct), ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Edges[actor].PerceivedDominance > dominanceBefore,
                $"NegativeAct musí zvýšit PerceivedDominance. Před: {dominanceBefore:F1}, po: {engine.State.Edges[actor].PerceivedDominance:F1}");
        }

        // ------------------------------------------------------------------
        // Test 3: Betrayal dává 2× větší nárůst PerceivedDominance než NegativeAct
        // ------------------------------------------------------------------

        [TestMethod]
        public void ThirdPartyBetrayal_IncreasesPerceivedDominance_MoreThanNegativeAct()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actorNeg = new HumanId(Guid.NewGuid());
            var actorBetrayal = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var ctx = BuildCtx(self);

            var engineNeg = BuildEngine();
            var engineBetrayal = BuildEngine();

            engineNeg.Handle(new FirstImpressionFormed(_now, self, actorNeg, Like: 50, Attraction: 40), ctx, _outbox);
            engineBetrayal.Handle(new FirstImpressionFormed(_now, self, actorBetrayal, Like: 50, Attraction: 40), ctx, _outbox);

            var negBefore = engineNeg.State.Edges[actorNeg].PerceivedDominance;
            var betrayalBefore = engineBetrayal.State.Edges[actorBetrayal].PerceivedDominance;

            // Act
            engineNeg.Handle(new ThirdPartyActionObserved(_now, self, actorNeg, target, -1.0, ThirdPartyObservationType.NegativeAct), ctx, _outbox);
            engineBetrayal.Handle(new ThirdPartyActionObserved(_now, self, actorBetrayal, target, -1.0, ThirdPartyObservationType.Betrayal), ctx, _outbox);

            // Assert — Betrayal dává DominanceGainPerNegativeAct × 2
            var negGain = engineNeg.State.Edges[actorNeg].PerceivedDominance - negBefore;
            var betrayalGain = engineBetrayal.State.Edges[actorBetrayal].PerceivedDominance - betrayalBefore;

            Assert.IsTrue(betrayalGain > negGain,
                $"Betrayal musí dávat 2× větší nárůst PerceivedDominance než NegativeAct. " +
                $"NegGain={negGain:F2}, BetrayalGain={betrayalGain:F2}");
        }

        // ------------------------------------------------------------------
        // Test 4: ContemptuousActPerformed → PerceivedDominance roste
        // ------------------------------------------------------------------

        [TestMethod]
        public void ContemptuousActReceived_IncreasesPerceivedDominance()
        {
            // Arrange
            var self = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var engine = BuildEngine();
            var ctx = BuildCtx(self);

            engine.Handle(new FirstImpressionFormed(_now, self, actor, Like: 50, Attraction: 40), ctx, _outbox);
            var dominanceBefore = engine.State.Edges[actor].PerceivedDominance;

            // Act — ContemptuousActPerformed: actor is contemptuous toward self
            engine.Handle(new ContemptuousActPerformed(_now, From: actor, To: self), ctx, _outbox);

            // Assert
            Assert.IsTrue(engine.State.Edges[actor].PerceivedDominance > dominanceBefore,
                $"ContemptuousActPerformed musí zvýšit PerceivedDominance. Před: {dominanceBefore:F1}, po: {engine.State.Edges[actor].PerceivedDominance:F1}");
        }

        // ------------------------------------------------------------------
        // Test 5: PerceivedDominance/Prestige decays toward neutral (50) over time
        // ------------------------------------------------------------------

        [TestMethod]
        public void PerceivedDominancePrestige_DecayTowardNeutral_OverTime()
        {
            // Arrange — engine se zapnutým decayem
            var decayCfg = new RelationshipsConfig(DecayPerDay: 0.5);
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var engine = BuildEngine(decayCfg);
            var ctx = BuildCtx(self);

            // Restore edge s extrémními hodnotami dominance/prestige
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(
                    self, other,
                    Like: 60, Trust: 60,
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50,
                    IntimateAffinity: 40, SexualInterest: 40,
                    Closeness: 60, Respect: 60, Comfort: 60,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PerceivedDominance: 80,
                    PerceivedPrestige: 20)
            }));

            // Act — 365 dní
            engine.Tick(_now, WTimeSpan.FromDays(365), ctx, _outbox);

            var edgeAfter = engine.State.Edges[other];

            // Assert — dominance klesá od 80 k 50, prestige roste od 20 k 50
            Assert.IsTrue(edgeAfter.PerceivedDominance < 80 && edgeAfter.PerceivedDominance > 50,
                $"PerceivedDominance (80) musí klesat k 50. Hodnota: {edgeAfter.PerceivedDominance:F1}");
            Assert.IsTrue(edgeAfter.PerceivedPrestige > 20 && edgeAfter.PerceivedPrestige < 50,
                $"PerceivedPrestige (20) musí růst k 50. Hodnota: {edgeAfter.PerceivedPrestige:F1}");
        }
    }

    // =========================================================================
    // Dunbar Finite Attention Budget — Saramaki et al. 2014, PNAS
    // Exceeding Tier-1 capacity accelerates decay of lower-tier edges.
    // =========================================================================

    [TestClass]
    public class DunbarAttentionBudgetTests : TestBase
    {
        private IEventCollector _outbox = default!;
        private WDateTime _now;

        [TestInitialize]
        public void Setup()
        {
            _now = new WDateTime(0);
            _outbox = new EventCollector();
        }

        private DefaultRelationshipsEngine BuildEngine(RelationshipsConfig cfg)
            => new DefaultRelationshipsEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new FixedSocialFidelityPolicy(SocialFidelityLevel.Full));

        private IHumanContext BuildCtx(HumanId self)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.0, 0.5, 0.5, 0, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.3, 0.3, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
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

        private static RelationshipEdge MakeEdge(HumanId self, HumanId other, double closeness, double like = 60)
            => new RelationshipEdge(
                self, other,
                Like: like, Trust: 60, Familiarity: 40,
                AestheticAttraction: 50, PhysicalAttraction: 50,
                IntimateAffinity: 40, SexualInterest: 40,
                Closeness: closeness, Respect: 55, Comfort: 55,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        // ------------------------------------------------------------------
        // Test: 3 Tier-1 hrany (kapacita = 2) → Tier-2 hrana rozpadá rychleji
        // ------------------------------------------------------------------

        [TestMethod]
        public void DunbarTier1Excess_AcceleratesDecayOfLowerTierEdges()
        {
            // Config: DunbarTier1Capacity = 2 (nízký práh pro test),
            //         DunbarTier1Threshold = 70 (closeness >= 70 = Tier-1)
            //         DunbarTier2Threshold = 40 (closeness 40–69 = Tier-2)
            // DecayPerDay záměrně nízké (0.1), aby Like nekleslo přímo na floor 50
            // za krátký testovací interval; AttentionBudgetPressure je velká (5.0)
            // aby efekt byl viditelný i za 1 den.
            var cfg = new RelationshipsConfig(
                DecayPerDay: 0.1,
                DunbarTier1Capacity: 2,
                DunbarTier2Capacity: 15,
                DunbarTier1Threshold: 70.0,
                DunbarTier2Threshold: 40.0,
                AttentionBudgetPressurePerExcessTier1: 10.0);  // obří tlak → zcela jiná rychlost

            var self = new HumanId(Guid.NewGuid());
            var ctx = BuildCtx(self);

            // 3 Tier-1 přátele (closeness=80, nad prahem 70) → 1 přebytek
            var tier1a = new HumanId(Guid.NewGuid());
            var tier1b = new HumanId(Guid.NewGuid());
            var tier1c = new HumanId(Guid.NewGuid());
            // 1 Tier-2 přítel (closeness=50, mezi 40 a 70)
            var tier2 = new HumanId(Guid.NewGuid());

            // Engine s tlakem (3 tier-1, kapacita=2 → 1 přebytek)
            var engineWithPressure = BuildEngine(cfg);
            engineWithPressure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [tier1a] = MakeEdge(self, tier1a, closeness: 80),
                [tier1b] = MakeEdge(self, tier1b, closeness: 80),
                [tier1c] = MakeEdge(self, tier1c, closeness: 80),
                [tier2] = MakeEdge(self, tier2, closeness: 50, like: 70)
            }));

            // Engine bez tlaku (jen 1 tier-1, pod kapacitou)
            var engineNoPressure = BuildEngine(cfg);
            engineNoPressure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [tier1a] = MakeEdge(self, tier1a, closeness: 80),
                [tier2] = MakeEdge(self, tier2, closeness: 50, like: 70)
            }));

            // Act — 1 den (krátce, aby Like nesáhlo na floor 50)
            engineWithPressure.Tick(_now, WTimeSpan.FromDays(1), ctx, _outbox);
            engineNoPressure.Tick(_now, WTimeSpan.FromDays(1), ctx, _outbox);

            // Tier-2 Like po 1 dni
            var tier2LikeWithPressure = engineWithPressure.State.Edges.TryGetValue(tier2, out var edgeP)
                ? edgeP.Like
                : 0.0;

            var tier2LikeNoPressure = engineNoPressure.State.Edges.TryGetValue(tier2, out var edgeNP)
                ? edgeNP.Like
                : 0.0;

            // Assert — s tlakem musí klesat více (nebo být nižší)
            Assert.IsTrue(tier2LikeWithPressure < tier2LikeNoPressure,
                $"Tier-2 hrana musí rozpadat rychleji při přebytku Tier-1 přítelů. " +
                $"S tlakem: {tier2LikeWithPressure:F2}, bez tlaku: {tier2LikeNoPressure:F2}");
        }
    }
}
