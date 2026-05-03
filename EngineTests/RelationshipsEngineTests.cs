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
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System.Linq;
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
                    Familiarity: 50, AestheticAttraction: 65, PhysicalAttraction: 65, RomanticInterest: 55, SexualInterest: 60,
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
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, RomanticInterest: 40, SexualInterest: 40,
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
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, RomanticInterest: 40, SexualInterest: 40,
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
                    Familiarity: 50, AestheticAttraction: 50, PhysicalAttraction: 50, RomanticInterest: 40, SexualInterest: 40,
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
                    Familiarity: 80, AestheticAttraction: 70, PhysicalAttraction: 70, RomanticInterest: 65, SexualInterest: 60,
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
                    Familiarity: 40, AestheticAttraction: 35, PhysicalAttraction: 35, RomanticInterest: 20, SexualInterest: 15,
                    Closeness: 25, Respect: 55, Comfort: 30,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                    PositiveInteractionCount: 0),
                [otherB] = new RelationshipEdge(
                    self, otherB,
                    Like: 60, Trust: 70,
                    Familiarity: 55, AestheticAttraction: 60, PhysicalAttraction: 60, RomanticInterest: 75, SexualInterest: 80,
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
                    Familiarity: 45, AestheticAttraction: 55, PhysicalAttraction: 55, RomanticInterest: 45, SexualInterest: 45,
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
                    Familiarity: 5, AestheticAttraction: 55, PhysicalAttraction: 55, RomanticInterest: 45, SexualInterest: 45,
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
                    Familiarity: 60, AestheticAttraction: 50, PhysicalAttraction: 50, RomanticInterest: 45, SexualInterest: 45,
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
            var now = new WDateTime(0);
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
            var now = new WDateTime(0);
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
            var self  = new HumanId(Guid.NewGuid());
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
                    RomanticInterest: 50, SexualInterest: 40,
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
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure   = BuildEngine();
            var engineAnxious  = BuildEngine();

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
                RomanticInterest: 45, SexualInterest: 35,
                Closeness: 50, Respect: 60, Comfort: 60,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));
            engineSecure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));
            engineAnxious.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));

            var rejection = new InteractionOutcome(_now, self, other, Accepted: false, Reason: "no",
                Act: SpeechAct.Invite);

            engineSecure.Handle(rejection, ctxSecure, _outbox);
            engineAnxious.Handle(rejection, ctxAnxious, _outbox);

            var likeDeltaSecure  = edge.Like - engineSecure.State.Edges[other].Like;
            var likeDeltaAnxious = edge.Like - engineAnxious.State.Edges[other].Like;

            Assert.IsTrue(likeDeltaAnxious > likeDeltaSecure,
                $"Anxious sting ({likeDeltaAnxious:F2}) should exceed Secure sting ({likeDeltaSecure:F2})");
        }

        [TestMethod]
        public void AttachmentProfile_HighAvoidance_ReducesRepairGain()
        {
            // Dismissing (high Avoidance): RepairAttempt.Accepted gives less Trust
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure    = BuildEngine();
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
                RomanticInterest: 30, SexualInterest: 20,
                Closeness: 40, Respect: 55, Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));
            engineSecure.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));
            engineDismissing.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = edge }));

            var repair = new RepairAttempt(_now, self, other, Accepted: true);
            engineSecure.Handle(repair, ctxSecure, _outbox);
            engineDismissing.Handle(repair, ctxDismissing, _outbox);

            var trustGainSecure    = engineSecure.State.Edges[other].Trust - edge.Trust;
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
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildContext(self);

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
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildContext(self);

            // Seed edge with known TransgressionResidue
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 60, Trust: 60, Familiarity: 30,
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    RomanticInterest: 30, SexualInterest: 20,
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
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildContext(self);

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 60, Trust: 60, Familiarity: 30,
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    RomanticInterest: 30, SexualInterest: 20,
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
            var self   = new HumanId(Guid.NewGuid());
            var recent = new HumanId(Guid.NewGuid());
            var stale  = new HumanId(Guid.NewGuid());
            var ctx    = BuildContext(self);
            var engine = BuildEngine();

            var now = _now;
            var recentContact = now - WTimeSpan.FromDays(5);
            var staleContact  = now - WTimeSpan.FromDays(150);

            var baseEdge = new RelationshipEdge(self, recent,
                Like: 70, Trust: 70, Familiarity: 50,
                AestheticAttraction: 60, PhysicalAttraction: 60,
                RomanticInterest: 40, SexualInterest: 30,
                Closeness: 60, Respect: 60, Comfort: 65,
                Breakdown: new DomainBreakdown(50, 50, 60, 55, 60));

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [recent] = baseEdge with { A = self, B = recent, LastContactTime = recentContact },
                [stale]  = baseEdge with { A = self, B = stale,  LastContactTime = staleContact }
            }));

            engine.Tick(now, WTimeSpan.FromDays(7), ctx, _outbox);

            var recentCloseness = engine.State.Edges[recent].Closeness;
            var staleCloseness  = engine.State.Edges[stale].Closeness;

            Assert.IsTrue(staleCloseness < recentCloseness,
                $"Stale edge Closeness ({staleCloseness:F1}) should be lower than recent ({recentCloseness:F1}) due to Navarro gap");
        }

        #endregion Navarro gap rule testy

        #region CommunalStrength testy

        [TestMethod]
        public void CommunalStrength_GrowsFromIntimateTouch()
        {
            var engine = BuildEngine();
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildContext(self);

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
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildContext(self);

            var now          = _now;
            var lastContact  = now - WTimeSpan.FromDays(40);  // 40 days since last contact

            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [other] = new RelationshipEdge(self, other,
                    Like: 65, Trust: 60, Familiarity: 70,  // Familiarity > 55 ✓
                    AestheticAttraction: 60, PhysicalAttraction: 60,
                    RomanticInterest: 30, SexualInterest: 20,
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
            var engine  = BuildEngine();
            var self    = new HumanId(Guid.NewGuid());
            var other   = new HumanId(Guid.NewGuid());
            var ctx     = BuildContext(self);
            var outbox  = new EventCollector();

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

            var appearance = TestAppearanceFactory.Build(
                heightCm: 170,
                frame: BodyFrame.Medium,
                skinTone: SkinTone.Medium,
                eyeColor: EyeColor.Brown,
                hairColor: HairColorNatural.Brown,
                hairType: HairType.Wavy,
                faceShape: FaceShape.Oval,
                shoulderBreadthCm: 40,
                hipBreadthCm: 38,
                noseProjection: 0.5,
                lipFullness: 0.5);

            var physio = physioFactory.Create(random, biology, identity.BirthDate, WDateOnly.New(100, 1, 1));
            var psych = psychFactory.Create(random);
            var behavior = ServiceProvider.GetRequiredService<IBehaviorEngine>();
            var interact = ServiceProvider.GetRequiredService<IInteractionEngine>();
            var relations = ServiceProvider.GetRequiredService<IRelationshipsEngine>();
            var memory = ServiceProvider.GetRequiredService<IMemoryEngine>();
            var semanticMemory = ServiceProvider.GetRequiredService<ISemanticMemoryEngine>();

            var snapshot = new EnginesSnapshot(
                physio.State,
                psych.State,
                behavior.State,
                interact.State,
                relations.State,
                memory.State,
                semanticMemory.State);

            return new OrchestratedHuman(
                id,
                identity,
                biology,
                personality,
                appearance,
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
                    RomanticInterest: romanticInterest,
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
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown),
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
    }
}
