// SocialHierarchyTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Status;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Tests for the social-hierarchy subsystem: the two-axis emergent <see cref="SocietalStatus"/>,
    /// the <see cref="StatusLedger"/> consensus + hierarchy stability, the status×stability×control
    /// stress term, and deference in target selection. Calibrated against Cheng et al. (2013),
    /// Anderson et al. (2015), Gesquiere (2011) and the Whitehall studies (Marmot).
    /// </summary>
    [TestClass]
    public class SocialHierarchyTests : TestBase
    {
        private static readonly StatusConfig Cfg = new();

        #region Consensus — two orthogonal axes, conferred by others

        [TestMethod]
        public void Consensus_AveragesObserverPerceptions_PerAxisIndependently()
        {
            var obs = new List<(double, double, double)>
            {
                (80, 40, 1.0),
                (90, 30, 1.0),
                (70, 50, 1.0),
            };

            var s = StatusMath.Consensus(obs);

            Assert.AreEqual(80.0, s.DominanceStatus, 1e-6, "Dominance axis is the weighted mean of dominance perceptions.");
            Assert.AreEqual(40.0, s.PrestigeStatus, 1e-6, "Prestige axis is aggregated independently of dominance.");
        }

        [TestMethod]
        public void Consensus_NoObservers_ReturnsNeutral()
        {
            var s = StatusMath.Consensus(Array.Empty<(double, double, double)>());
            Assert.AreEqual(SocietalStatus.Neutral, s);
        }

        #endregion

        #region StatusLedger — status is the network consensus

        [TestMethod]
        public void Ledger_Get_IsConsensusOfAcquaintedObservers()
        {
            var ledger = new StatusLedger(Cfg);
            var target = NewId();
            var a = NewId();
            var b = NewId();
            var c = NewId();

            // Three acquainted observers all see the target as highly prestigious, low-dominance.
            var graph = new List<(HumanId, IReadOnlyDictionary<HumanId, RelationshipEdge>)>
            {
                (a, Edges((target, Edge(a, target, dom: 45, pres: 85, familiarity: 60)))),
                (b, Edges((target, Edge(b, target, dom: 50, pres: 90, familiarity: 60)))),
                (c, Edges((target, Edge(c, target, dom: 40, pres: 80, familiarity: 60)))),
            };

            ledger.Fold(graph);
            var s = ledger.Get(target);

            Assert.IsTrue(s.PrestigeStatus > 80, $"Conferred prestige should be high. Got {s.PrestigeStatus:F1}.");
            Assert.IsTrue(s.DominanceStatus < 55, $"Dominance should stay near neutral. Got {s.DominanceStatus:F1}.");
        }

        [TestMethod]
        public void Ledger_IgnoresStrangers_BelowMinFamiliarity()
        {
            var ledger = new StatusLedger(Cfg);
            var target = NewId();
            var a = NewId();

            var graph = new List<(HumanId, IReadOnlyDictionary<HumanId, RelationshipEdge>)>
            {
                (a, Edges((target, Edge(a, target, dom: 95, pres: 95, familiarity: 1)))),
            };

            ledger.Fold(graph);
            Assert.IsFalse(ledger.Has(target), "A barely-acquainted observer must not confer status.");
            Assert.AreEqual(SocietalStatus.Neutral, ledger.Get(target));
        }

        [TestMethod]
        public void Ledger_Stability_DropsWhenHierarchyReshuffles()
        {
            var ledger = new StatusLedger(Cfg);
            var target = NewId();
            var a = NewId();

            IReadOnlyList<(HumanId, IReadOnlyDictionary<HumanId, RelationshipEdge>)> Stable() => new[]
            {
                (a, Edges((target, Edge(a, target, dom: 50, pres: 60, familiarity: 60)))),
            };

            // Settle on a stable hierarchy.
            for (var i = 0; i < 5; i++)
                ledger.Fold(Stable());
            var stable = ledger.HierarchyStability();
            Assert.IsTrue(stable > 0.9, $"A repeated identical hierarchy is stable. Got {stable:F2}.");

            // Now violently reshuffle the target's standing several folds in a row.
            ledger.Fold(new[] { (a, Edges((target, Edge(a, target, dom: 50, pres: 5, familiarity: 60)))) });
            ledger.Fold(new[] { (a, Edges((target, Edge(a, target, dom: 50, pres: 95, familiarity: 60)))) });
            ledger.Fold(new[] { (a, Edges((target, Edge(a, target, dom: 50, pres: 5, familiarity: 60)))) });

            Assert.IsTrue(ledger.HierarchyStability() < stable,
                $"Churn must lower hierarchy stability. before={stable:F2}, after={ledger.HierarchyStability():F2}");
        }

        #endregion

        #region Status × stability × control → stress

        [TestMethod]
        public void Stress_HighStatus_Unstable_RaisesStress_CostOfTheTop()
        {
            var top = new SocietalStatus(85, 85);
            var unstable = StatusMath.StatusStressPerHour(top, stability: 0.1, perceivedControl: 0.8, Cfg);
            Assert.IsTrue(unstable > 0, $"High status under instability raises stress (cost of the top). Got {unstable:F3}.");
        }

        [TestMethod]
        public void Stress_HighStatus_Stable_RelievesStress()
        {
            var top = new SocietalStatus(85, 85);
            var stable = StatusMath.StatusStressPerHour(top, stability: 1.0, perceivedControl: 0.8, Cfg);
            Assert.IsTrue(stable < 0, $"Secure rank in a stable hierarchy buffers stress. Got {stable:F3}.");
        }

        [TestMethod]
        public void Stress_LowStatus_LowControl_Stable_ChronicBurden()
        {
            var bottom = new SocietalStatus(15, 15);
            var burden = StatusMath.StatusStressPerHour(bottom, stability: 1.0, perceivedControl: 0.05, Cfg);
            var withControl = StatusMath.StatusStressPerHour(bottom, stability: 1.0, perceivedControl: 0.95, Cfg);
            Assert.IsTrue(burden > 0, $"Low status + low control + stable → chronic burden. Got {burden:F3}.");
            Assert.IsTrue(burden > withControl, "Having control attenuates the low-status burden (Whitehall).");
        }

        [TestMethod]
        public void Stress_IntegratesIntoPsychologyTick_WhenStatusInjected()
        {
            var id = NewId();
            var basePsych = BuildSnapshot(new Dictionary<HumanId, RelationshipEdge>());

            // Same character, two status contexts: neutral (no injection) vs high-status-in-unstable-hierarchy.
            var ctxNeutral = BuildContext(id, basePsych);
            var ctxThreatened = BuildContext(id, basePsych with
            {
                SocietalStatus = new SocietalStatus(90, 90),
                HierarchyStability = 0.05,
            });

            var engineA = new DefaultPsychologyEngine(Options.Create(new PsychologyConfig()), Loggers(), new ZeroRandomSource());
            var engineB = new DefaultPsychologyEngine(Options.Create(new PsychologyConfig()), Loggers(), new ZeroRandomSource());

            engineA.Tick(new WDateTime(0), WTimeSpan.FromHours(2), ctxNeutral, new EventCollector());
            engineB.Tick(new WDateTime(0), WTimeSpan.FromHours(2), ctxThreatened, new EventCollector());

            Assert.IsTrue(engineB.State.Stress > engineA.State.Stress,
                $"A threatened high-status character accrues more stress. neutral={engineA.State.Stress:F2}, threatened={engineB.State.Stress:F2}");
        }

        #endregion

        #region Deference

        [TestMethod]
        public void Deference_DrawsTowardPrestige_AwayFromDominance()
        {
            var self = new SocietalStatus(50, 50);
            var prestigious = new SocietalStatus(50, 90);
            var coercive = new SocietalStatus(90, 50);

            Assert.IsTrue(StatusMath.DeferenceBias(self, prestigious, Cfg) > 0, "People approach up the prestige ladder.");
            Assert.IsTrue(StatusMath.DeferenceBias(self, coercive, Cfg) < 0, "People avoid coercive dominants.");
        }

        #endregion

        #region Helpers

        private static HumanId NewId() => new(Guid.NewGuid());

        private static IReadOnlyDictionary<HumanId, RelationshipEdge> Edges(params (HumanId Id, RelationshipEdge Edge)[] edges)
            => edges.ToDictionary(e => e.Id, e => e.Edge);

        private static RelationshipEdge Edge(HumanId a, HumanId b, double dom, double pres, double familiarity)
            => new RelationshipEdge(
                a, b,
                Like: 50, Trust: 50, Familiarity: familiarity,
                AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 20, SexualInterest: 20,
                Closeness: 50, Respect: 50, Comfort: 50,
                Breakdown: new DomainBreakdown(50, 50, 50, 50, 50),
                PerceivedDominance: dom,
                PerceivedPrestige: pres);

        private static ILoggerFactory Loggers() => LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        private static EnginesSnapshot BuildSnapshot(Dictionary<HumanId, RelationshipEdge> edges)
            => new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null),
                new PsychologyState(0.1, 0.5, 0.5, 20, 20, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown, null),
                new RelationshipState(edges),
                new MemoryIndex(new List<EpisodicMemory>()));

        private static IHumanContext BuildContext(HumanId id, EnginesSnapshot snapshot)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            return new HumanContext
            {
                Id = id,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandomSource(),
                Logger = Loggers().CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private sealed class ZeroRandomSource : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => false;
        }

        #endregion
    }
}
