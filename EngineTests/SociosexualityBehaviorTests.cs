// SociosexualityBehaviorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
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
                [target] = Edge(self, target, trust: 45, comfort: 46, closeness: 52, physical: 82, aesthetic: 80, romantic: 50, sexual: 78)
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

            Assert.IsTrue(restricted.RomanticInterest - start.RomanticInterest > unrestricted.RomanticInterest - start.RomanticInterest);
            Assert.IsTrue(unrestricted.SexualInterest - start.SexualInterest > restricted.SexualInterest - start.SexualInterest);
            Assert.IsTrue(restricted.Comfort > unrestricted.Comfort);
        }

        private static IReadOnlyList<IDomainEvent> EvaluateInvite(IHumanContext ctx, HumanId from, HumanId to)
        {
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            engine.RestoreState(new InteractionSurface("private", true, 0.1, 0.1, SurfaceKind.Private));
            var outbox = new EventCollector();
            engine.Handle(new InteractionProposed(new WDateTime(0), from, to, SpeechAct.Invite, null), ctx, outbox);
            return outbox.Drain();
        }

        private static RelationshipEdge ApplyAcceptedInvite(
            HumanId self,
            HumanId other,
            RelationshipEdge start,
            Sociosexuality sociosexuality)
        {
            var engine = new DefaultRelationshipsEngine(
                Options.Create(new RelationshipsConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            engine.RestoreState(new RelationshipState(new Dictionary<HumanId, RelationshipEdge> { [other] = start }));
            var context = BehaviorComponentTestFactory.Context(
                selfId: self,
                relationships: engine.State,
                personality: Personality(sociosexuality, sexuality: 0.8)).HumanContext;

            engine.Handle(new InteractionOutcome(new WDateTime(0), self, other, true, "accepted", SpeechAct.Invite), context, new EventCollector());
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
                RomanticInterest: romantic,
                SexualInterest: sexual,
                Closeness: closeness,
                Respect: 55,
                Comfort: comfort,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));

        private static Personality Personality(Sociosexuality sociosexuality, double sexuality)
            => new(
                new BigFive(0.5, 0.5, 0.6, 0.6, 0.3),
                AttachmentStyle.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.6, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.5, sexuality),
                sociosexuality,
                Chronotype.Neutral);

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
