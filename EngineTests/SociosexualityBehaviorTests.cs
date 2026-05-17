// SociosexualityBehaviorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class SociosexualityBehaviorTests : TestBase
    {
        [TestMethod]
        public void SemanticTargeting_RestrictedPrefersSaferIntimacyTarget()
        {
            var self = new HumanId(Guid.NewGuid());
            var safe = new HumanId(Guid.NewGuid());
            var attractiveRisky = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [safe] = Edge(self, safe, trust: 72, comfort: 75, closeness: 68, physical: 52, aesthetic: 54, romantic: 55, sexual: 48),
                [attractiveRisky] = Edge(self, attractiveRisky, trust: 34, comfort: 38, closeness: 34, physical: 92, aesthetic: 90, romantic: 45, sexual: 82)
            });
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [safe] = new(safe, new Dictionary<PersonBeliefKind, PersonBelief>
                {
                    [PersonBeliefKind.Warm] = new(safe, PersonBeliefKind.Warm, 0.75, 0.7, 3, new WDateTime(0)),
                    [PersonBeliefKind.EmotionallySafe] = new(safe, PersonBeliefKind.EmotionallySafe, 0.80, 0.7, 3, new WDateTime(0))
                }),
                [attractiveRisky] = new(attractiveRisky, new Dictionary<PersonBeliefKind, PersonBelief>
                {
                    [PersonBeliefKind.Rejecting] = new(attractiveRisky, PersonBeliefKind.Rejecting, 0.70, 0.7, 3, new WDateTime(0)),
                    [PersonBeliefKind.Critical] = new(attractiveRisky, PersonBeliefKind.Critical, 0.60, 0.7, 3, new WDateTime(0))
                })
            });
            var personality = Personality(Sociosexuality.Restricted, sexuality: 0.9);
            var context = BehaviorComponentTestFactory.Context(selfId: self, relationships: relationships, semanticMemory: semantic, personality: personality);

            var ranked = SemanticTargeting.RankTargets(context.HumanContext, new[] { safe, attractiveRisky }, SocialTargetMode.Intimacy, take: 2);

            Assert.AreEqual(safe, ranked[0].Target);
            Assert.IsTrue(ranked.Single(s => s.Target == attractiveRisky).PsychologicallyBlocked);
        }

        [TestMethod]
        public void SocialNeeds_UnrestrictedKeepsMarginalIntimacyTargetAvailable()
        {
            var self = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [target] = Edge(self, target, trust: 55, comfort: 55, closeness: 55, physical: 82, aesthetic: 80, romantic: 50, sexual: 78)
            });
            var restricted = BehaviorComponentTestFactory.Context(
                selfId: self,
                relationships: relationships,
                personality: Personality(Sociosexuality.Restricted, sexuality: 0.9));
            var unrestricted = BehaviorComponentTestFactory.Context(
                selfId: self,
                relationships: relationships,
                personality: Personality(Sociosexuality.Unrestricted, sexuality: 0.9));

            var restrictedInvite = new SocialNeedsEngine().Evaluate(restricted).Candidates.FirstOrDefault(c => c.Name == InviteIntimacy);
            var unrestrictedInvite = new SocialNeedsEngine().Evaluate(unrestricted).Candidates.FirstOrDefault(c => c.Name == InviteIntimacy);

            Assert.IsNull(restrictedInvite);
            Assert.IsNotNull(unrestrictedInvite);
            Assert.AreEqual(target, unrestrictedInvite!.SocialTargeting?.TargetHuman);
        }

        [TestMethod]
        public void InteractionInviteAcceptance_UnrestrictedHasLowerContextThresholdThanRestricted()
        {
            var from = new HumanId(Guid.NewGuid());
            var to = new HumanId(Guid.NewGuid());
            var relationship = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [from] = Edge(to, from, trust: 55, comfort: 55, closeness: 55, physical: 80, aesthetic: 78, romantic: 55, sexual: 70)
            });
            var restrictedCtx = BehaviorComponentTestFactory.Context(
                selfId: to,
                relationships: relationship,
                personality: Personality(Sociosexuality.Restricted, sexuality: 0.8),
                random: new ThresholdRandom(0.75)).HumanContext;
            var unrestrictedCtx = BehaviorComponentTestFactory.Context(
                selfId: to,
                relationships: relationship,
                personality: Personality(Sociosexuality.Unrestricted, sexuality: 0.8),
                random: new ThresholdRandom(0.75)).HumanContext;

            var restrictedOutbox = EvaluateInvite(restrictedCtx, from, to);
            var unrestrictedOutbox = EvaluateInvite(unrestrictedCtx, from, to);

            Assert.IsFalse(restrictedOutbox.OfType<InteractionOutcome>().Single().Accepted);
            Assert.IsTrue(unrestrictedOutbox.OfType<InteractionOutcome>().Single().Accepted);
        }

        [TestMethod]
        public void RelationshipInviteDelta_RestrictedEmphasizesRomanticSafetyOverSexualInterest()
        {
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var start = Edge(self, other, trust: 70, comfort: 72, closeness: 70, physical: 80, aesthetic: 78, romantic: 45, sexual: 45);
            var restricted = ApplyAcceptedInvite(self, other, start, Sociosexuality.Restricted);
            var unrestricted = ApplyAcceptedInvite(self, other, start, Sociosexuality.Unrestricted);

            Assert.IsTrue(restricted.IntimateAffinity - start.IntimateAffinity > unrestricted.IntimateAffinity - start.IntimateAffinity);
            Assert.IsTrue(unrestricted.SexualInterest - start.SexualInterest > restricted.SexualInterest - start.SexualInterest);
            Assert.IsTrue(restricted.Comfort > unrestricted.Comfort);
        }

        [TestMethod]
        public void RelationshipInviteDelta_SexualOrientationShapesRomanticAndSexualGrowth()
        {
            var self = new HumanId(Guid.NewGuid());
            var femaleTarget = new HumanId(Guid.NewGuid());
            var maleTarget = new HumanId(Guid.NewGuid());
            var profile = HeterosexualMaleProfile();
            var femaleStart = Edge(self, femaleTarget, trust: 72, comfort: 74, closeness: 72, physical: 82, aesthetic: 80, romantic: 35, sexual: 35)
                with
            { TargetBiology = SexBiology.Female };
            var maleStart = Edge(self, maleTarget, trust: 72, comfort: 74, closeness: 72, physical: 82, aesthetic: 80, romantic: 35, sexual: 35)
                with
            { TargetBiology = SexBiology.Male };

            var femaleAfter = ApplyAcceptedInvite(self, femaleTarget, femaleStart, Sociosexuality.Intermediate, profile, SexBiology.Male, SexBiology.Female);
            var maleAfter = ApplyAcceptedInvite(self, maleTarget, maleStart, Sociosexuality.Intermediate, profile, SexBiology.Male, SexBiology.Male);

            Assert.IsTrue(femaleAfter.IntimateAffinity - femaleStart.IntimateAffinity > maleAfter.IntimateAffinity - maleStart.IntimateAffinity);
            Assert.IsTrue(femaleAfter.SexualInterest - femaleStart.SexualInterest > maleAfter.SexualInterest - maleStart.SexualInterest);
        }

        [TestMethod]
        public void InteractionInviteAcceptance_SexualOrientationBiasesIntimacyInviteAcceptance()
        {
            var femaleFrom = new HumanId(Guid.NewGuid());
            var maleFrom = new HumanId(Guid.NewGuid());
            var to = new HumanId(Guid.NewGuid());
            var profile = HeterosexualMaleProfile();
            var femaleRelationship = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [femaleFrom] = Edge(to, femaleFrom, trust: 55, comfort: 55, closeness: 55, physical: 80, aesthetic: 78, romantic: 55, sexual: 70)
                    with
                { TargetBiology = SexBiology.Female }
            });
            var maleRelationship = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [maleFrom] = Edge(to, maleFrom, trust: 55, comfort: 55, closeness: 55, physical: 80, aesthetic: 78, romantic: 55, sexual: 70)
                    with
                { TargetBiology = SexBiology.Male }
            });
            var femaleCtx = BehaviorComponentTestFactory.Context(
                selfId: to,
                relationships: femaleRelationship,
                personality: Personality(Sociosexuality.Intermediate, sexuality: 0.8),
                random: new ThresholdRandom(0.76),
                biology: SexBiology.Male,
                attractionProfile: profile).HumanContext;
            var maleCtx = BehaviorComponentTestFactory.Context(
                selfId: to,
                relationships: maleRelationship,
                personality: Personality(Sociosexuality.Intermediate, sexuality: 0.8),
                random: new ThresholdRandom(0.76),
                biology: SexBiology.Male,
                attractionProfile: profile).HumanContext;

            var femaleOutbox = EvaluateInvite(femaleCtx, femaleFrom, to, SexBiology.Female);
            var maleOutbox = EvaluateInvite(maleCtx, maleFrom, to, SexBiology.Male);

            Assert.IsTrue(femaleOutbox.OfType<InteractionOutcome>().Single().Accepted);
            Assert.IsFalse(maleOutbox.OfType<InteractionOutcome>().Single().Accepted);
        }

        [TestMethod]
        public void SemanticTargeting_IntimacyModeUsesKnownTargetBiologyWhenAvailable()
        {
            var self = new HumanId(Guid.NewGuid());
            var femaleTarget = new HumanId(Guid.NewGuid());
            var maleTarget = new HumanId(Guid.NewGuid());
            var relationships = new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
            {
                [femaleTarget] = Edge(self, femaleTarget, trust: 56, comfort: 58, closeness: 42, physical: 58, aesthetic: 58, romantic: 28, sexual: 30)
                    with
                { TargetBiology = SexBiology.Female },
                [maleTarget] = Edge(self, maleTarget, trust: 56, comfort: 58, closeness: 42, physical: 58, aesthetic: 58, romantic: 28, sexual: 30)
                    with
                { TargetBiology = SexBiology.Male }
            });
            var semantic = new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [femaleTarget] = SafeBeliefs(femaleTarget),
                [maleTarget] = SafeBeliefs(maleTarget)
            });
            var context = BehaviorComponentTestFactory.Context(
                selfId: self,
                relationships: relationships,
                semanticMemory: semantic,
                personality: Personality(Sociosexuality.Intermediate, sexuality: 0.8),
                biology: SexBiology.Male,
                attractionProfile: HeterosexualMaleProfile()).HumanContext;

            var ranked = SemanticTargeting.RankTargets(context, new[] { maleTarget, femaleTarget }, SocialTargetMode.Intimacy, take: 2);

            Assert.AreEqual(femaleTarget, ranked[0].Target);
            Assert.IsTrue(ranked[0].Score > ranked[1].Score);
        }

        private static IReadOnlyList<IDomainEvent> EvaluateInvite(IHumanContext ctx, HumanId from, HumanId to, SexBiology? fromBiology = null)
        {
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            engine.RestoreState(new InteractionSurface("private", true, 0.1, 0.1, SurfaceKind.Private));
            var outbox = new EventCollector();
            engine.Handle(new InteractionProposed(new WDateTime(0), from, to, SpeechAct.Invite, null, fromBiology), ctx, outbox);
            return outbox.Drain();
        }

        private static RelationshipEdge ApplyAcceptedInvite(
            HumanId self,
            HumanId other,
            RelationshipEdge start,
            Sociosexuality sociosexuality,
            AttractionProfile? attractionProfile = null,
            SexBiology selfBiology = SexBiology.Female,
            SexBiology? targetBiology = null)
        {
            var engine = new DefaultRelationshipsEngine(
                Options.Create(new RelationshipsConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new FixedSocialFidelityPolicy(SocialFidelityLevel.Full));
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = start }));
            var context = BehaviorComponentTestFactory.Context(
                selfId: self,
                relationships: engine.State,
                personality: Personality(sociosexuality, sexuality: 0.8),
                biology: selfBiology,
                attractionProfile: attractionProfile).HumanContext;

            engine.Handle(new InteractionOutcome(new WDateTime(0), self, other, true, "accepted", SpeechAct.Invite, selfBiology, targetBiology), context, new EventCollector());
            return engine.State.Edges[other];
        }

        private static RelationshipEdge Edge(
            HumanId self,
            HumanId other,
            double trust,
            double comfort,
            double closeness,
            double physical,
            double aesthetic,
            double romantic,
            double sexual)
            => new(
                self,
                other,
                Like: 60,
                Trust: trust,
                Familiarity: 65,
                AestheticAttraction: aesthetic,
                PhysicalAttraction: physical,
                IntimateAffinity: romantic,
                SexualInterest: sexual,
                Closeness: closeness,
                Respect: 55,
                Comfort: comfort,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        private static Personality Personality(Sociosexuality sociosexuality, double sexuality)
            => new(
                new BigFive(0.5, 0.5, 0.6, 0.6, 0.3),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.6, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.5, sexuality),
                sociosexuality,
                Chronotype.Neutral);

        private static AttractionProfile HeterosexualMaleProfile()
            => new(
                PreferredHeightCm: 168,
                HeightToleranceCm: 20,
                FramePreference: BodyFramePreference.Medium,
                PreferredWhr: 0.72,
                SymmetryWeight: 0.5,
                Orientation: SexualOrientation.Heterosexual,
                FemaleTargetAttraction: 1.0,
                MaleTargetAttraction: 0.12,
                OtherTargetAttraction: 0.65);

        private static PersonBeliefSet SafeBeliefs(HumanId target)
            => new(target, new Dictionary<PersonBeliefKind, PersonBelief>
            {
                [PersonBeliefKind.Warm] = new(target, PersonBeliefKind.Warm, 0.45, 0.5, 2, new WDateTime(0)),
                [PersonBeliefKind.EmotionallySafe] = new(target, PersonBeliefKind.EmotionallySafe, 0.45, 0.5, 2, new WDateTime(0))
            });

        private sealed class ThresholdRandom : IRandomSource
        {
            private readonly double _threshold;

            public ThresholdRandom(double threshold)
            {
                _threshold = threshold;
            }

            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => p >= _threshold;
        }
    }
}
