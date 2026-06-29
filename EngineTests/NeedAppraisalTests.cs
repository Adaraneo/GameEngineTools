// NeedAppraisalTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.NeedAppraisal;
    using GameEngineTools.Characters.Engines.Goals;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Tests for Subsystem E (SDT basic needs): the derived <see cref="NeedAppraisalState"/> /
    /// <see cref="NeedChannel"/> appraisal layer over goals, relationships and regulatory focus
    /// (Competence / Relatedness / Autonomy, each with the asymmetric satisfaction/frustration coupling).
    /// </summary>
    [TestClass]
    public class NeedAppraisalTests : TestBase
    {
        #region Competence

        [TestMethod]
        public void Competence_HighGoalProgress_LowFrustration_YieldsHighSatisfaction()
        {
            var state = Appraise(goals: new[] { Goal(progress: 0.9, frustration: 0.0) });
            Assert.IsTrue(state.Competence.Satisfaction > 0.8,
                $"High progress + low frustration → high competence satisfaction. Got {state.Competence.Satisfaction:F3}.");
            Assert.IsTrue(state.Competence.Frustration < 0.1);
        }

        [TestMethod]
        public void Competence_StalledFrustratedGoals_YieldsLowSatisfaction_HighFrustration()
        {
            var state = Appraise(goals: new[] { Goal(progress: 0.1, frustration: 0.9) });
            Assert.IsTrue(state.Competence.Satisfaction < 0.5,
                $"Stalled, frustrated goals → low competence satisfaction. Got {state.Competence.Satisfaction:F3}.");
            Assert.IsTrue(state.Competence.Frustration > 0.5,
                $"Stalled, frustrated goals → high competence frustration. Got {state.Competence.Frustration:F3}.");
        }

        #endregion

        #region Relatedness

        [TestMethod]
        public void Relatedness_WarmCloseEdges_YieldsHighSatisfaction()
        {
            var state = Appraise(edges: new[] { Edge(closeness: 80, comfort: 80, trust: 80, respect: 50, positiveCount: 10) });
            Assert.IsTrue(state.Relatedness.Satisfaction > 0.7,
                $"Warm, close edges → high relatedness satisfaction. Got {state.Relatedness.Satisfaction:F3}.");
            Assert.AreEqual(0.0, state.Relatedness.Frustration, 0.001);
        }

        [TestMethod]
        public void Relatedness_ExcludesStatusSignals()
        {
            // Two characters with IDENTICAL Closeness/Comfort/Trust but very different status (Respect)
            // must yield IDENTICAL relatedness appraisal — proves status is not read.
            var lowStatus = Appraise(edges: new[] { Edge(closeness: 60, comfort: 60, trust: 60, respect: 10, positiveCount: 5) });
            var highStatus = Appraise(edges: new[] { Edge(closeness: 60, comfort: 60, trust: 60, respect: 95, positiveCount: 5) });

            Assert.AreEqual(lowStatus.Relatedness.Satisfaction, highStatus.Relatedness.Satisfaction, 1e-9,
                "Relatedness must ignore status/Respect — only warmth signals count.");
            Assert.AreEqual(lowStatus.Relatedness.Frustration, highStatus.Relatedness.Frustration, 1e-9);
        }

        [TestMethod]
        public void Relatedness_NoRelationships_MildBaselineDeficit_NotZero()
        {
            var state = Appraise(); // no edges
            Assert.AreEqual(0.4, state.Relatedness.Satisfaction, 1e-9,
                "No relationships → mild baseline deficit (0.4), not zero.");
            Assert.IsTrue(state.Relatedness.Frustration < 0.2,
                "Absence of relationships is a deficit, not active thwarting — frustration stays low.");
        }

        #endregion

        #region Autonomy

        [TestMethod]
        public void Autonomy_HighPromotion_SlightlyHigherSatisfaction_ThanLowPromotion()
        {
            // No goals → volition neutral; isolate the weak Promotion covariate.
            var high = Appraise(promotion: 0.9);
            var low = Appraise(promotion: 0.1);

            Assert.IsTrue(high.Autonomy.Satisfaction > low.Autonomy.Satisfaction,
                $"Higher Promotion slightly raises autonomy satisfaction. High={high.Autonomy.Satisfaction:F3}, Low={low.Autonomy.Satisfaction:F3}.");
        }

        [TestMethod]
        public void Autonomy_DoesNotCollapse_IntoRegulatoryFocus()
        {
            // Vary BOTH Promotion and goal-origin volition independently; Autonomy is driven mainly by
            // the (independent) volition signal, so it must NOT correlate r>0.6 with Promotion.
            const int n = 200;
            var rng = new Random(4242);
            var origins = new[] { GoalOrigin.Personality, GoalOrigin.Scripted, GoalOrigin.Event };

            var autonomy = new double[n];
            var promotion = new double[n];
            for (var i = 0; i < n; i++)
            {
                var p = rng.NextDouble();
                var goal = Goal(progress: 0.5, frustration: 0.0, origin: origins[rng.Next(origins.Length)]);
                var state = Appraise(promotion: p, goals: new[] { goal });
                autonomy[i] = state.Autonomy.Satisfaction;
                promotion[i] = p;
            }

            var r = Pearson(autonomy, promotion);
            Assert.IsTrue(r < 0.6,
                $"Autonomy must stay distinct from RegulatoryFocus.Promotion (r<0.6). Got r={r:F3}.");
        }

        #endregion

        #region Channel / balance math

        [TestMethod]
        public void NeedChannel_FrustrationAsymmetry_WeighsMoreThanSatisfaction()
        {
            // Equal satisfaction and frustration must yield a NEGATIVE balance (frustration weight 1.5 > 1).
            var channel = new NeedChannel(Satisfaction: 0.5, Frustration: 0.5);
            Assert.IsTrue(channel.Balance < 0.0,
                $"Frustration is weighted more heavily, so equal channels give a negative balance. Got {channel.Balance:F3}.");
            Assert.AreEqual(0.5 - 1.5 * 0.5, channel.Balance, 1e-9);
        }

        [TestMethod]
        public void GlobalBalance_AveragesThreeChannels_CorrectlyWeighted()
        {
            var s = new NeedAppraisalState(
                Competence: new NeedChannel(0.8, 0.0),  // balance 0.8
                Relatedness: new NeedChannel(0.6, 0.2), // balance 0.6 - 0.3 = 0.3
                Autonomy: new NeedChannel(0.5, 0.0));   // balance 0.5

            var expected = (0.8 + 0.3 + 0.5) / 3.0;
            Assert.AreEqual(expected, s.GlobalBalance, 1e-9);
        }

        [TestMethod]
        public void NullNeedAppraisal_DefaultsToEmpty_NoThrow()
        {
            // Backward compatibility: a snapshot field left null + the engine's initial state.
            var engine = new DefaultNeedAppraisalEngine();
            Assert.AreSame(NeedAppraisalState.Empty, engine.State,
                "A freshly constructed engine reports the Empty appraisal, never null.");

            NeedAppraisalState? persisted = null;
            var effective = persisted ?? NeedAppraisalState.Empty;
            Assert.AreEqual(0.5, effective.Competence.Satisfaction, 1e-9);
            Assert.AreEqual(0.5, effective.GlobalBalance, 1e-9, "Empty → all channels neutral (balance 0.5).");
        }

        #endregion

        #region Helpers

        private static NeedAppraisalState Appraise(
            IReadOnlyList<PersistentGoal>? goals = null,
            IReadOnlyList<(RelationshipEdge edge, HumanId other)>? edges = null,
            double? promotion = null)
        {
            var engine = new DefaultNeedAppraisalEngine();
            engine.Tick(new WDateTime(0), WTimeSpan.FromHours(1), BuildContext(goals, edges, promotion));
            return engine.State;
        }

        private static PersistentGoal Goal(double progress, double frustration, GoalOrigin origin = GoalOrigin.Personality)
            => new PersistentGoal(
                Guid.NewGuid(), PersistentGoalKind.FindMeaning, origin,
                Salience: 0.5, Progress: progress, Frustration: frustration,
                CreatedAt: new WDateTime(0), LastProgressAt: new WDateTime(0));

        private static (RelationshipEdge, HumanId) Edge(
            double closeness, double comfort, double trust, double respect, int positiveCount)
        {
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var edge = new RelationshipEdge(
                self, other,
                Like: 50, Trust: trust, Familiarity: 50,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 20, SexualInterest: 20,
                Closeness: closeness, Respect: respect, Comfort: comfort,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PositiveInteractionCount: positiveCount);
            return (edge, other);
        }

        private static double Pearson(double[] x, double[] y)
        {
            var n = x.Length;
            double mx = x.Average(), my = y.Average();
            double cov = 0, vx = 0, vy = 0;
            for (var i = 0; i < n; i++)
            {
                var dx = x[i] - mx;
                var dy = y[i] - my;
                cov += dx * dy;
                vx += dx * dx;
                vy += dy * dy;
            }
            var denom = Math.Sqrt(vx * vy);
            return denom <= 0 ? 0.0 : cov / denom;
        }

        private static IHumanContext BuildContext(
            IReadOnlyList<PersistentGoal>? goals,
            IReadOnlyList<(RelationshipEdge edge, HumanId other)>? edges,
            double? promotion)
        {
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                RegulatoryFocus: promotion is { } p ? new RegulatoryFocusProfile(Promotion: p, Prevention: 0.5) : null);

            var edgeDict = new Dictionary<HumanId, RelationshipEdge>();
            if (edges is not null)
                foreach (var (edge, other) in edges)
                    edgeDict[other] = edge;

            var goalState = goals is null ? GoalState.Empty : new GoalState(goals.ToList());

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                new RelationshipState(edgeDict),
                new MemoryIndex(new List<EpisodicMemory>()),
                Goals: goalState);

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("NeedAppraisal"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion
    }
}
