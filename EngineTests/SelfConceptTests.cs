// SelfConceptTests.cs
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
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for R3 self-concept: <see cref="DefaultSelfConceptEngine"/> self-verification drift,
    /// self-discrepancy → BuildIdentity seeding, and self-esteem stability.
    /// </summary>
    [TestClass]
    public class SelfConceptTests : TestBase
    {
        #region Test 1 — disconfirming feedback heavily discounted

        [TestMethod]
        public void SelfConcept_DisconfirmingFeedback_HeavilyDiscounted()
        {
            var cfg = new SelfConceptConfig();
            var (engine, ctx, self) = MakeEngine(cfg);

            // Self-view: extraverted (0.8). Rejection is disconfirming evidence.
            engine.RestoreState(engine.State with { PerceivedExtraversion = 0.8 });
            var before = engine.State.PerceivedExtraversion;

            engine.Handle(Rejected(self), ctx, new EventCollector());

            var actualDelta = engine.State.PerceivedExtraversion - before;
            var nominalDelta = (0.0 - before) * cfg.PerceivedUpdateStep; // weight = 1.0 nominal
            var weightApplied = actualDelta / nominalDelta;

            Assert.IsTrue(Math.Abs(weightApplied - cfg.DisconfirmingWeight) < 0.02,
                $"Disconfirming feedback must apply ~{cfg.DisconfirmingWeight:F2}× nominal. Got: {weightApplied:F3}");
        }

        #endregion

        #region Test 2 — confirming feedback accepted

        [TestMethod]
        public void SelfConcept_ConfirmingFeedback_Accepted()
        {
            var cfg = new SelfConceptConfig();
            var (engine, ctx, self) = MakeEngine(cfg);

            // Self-view: extraverted (0.8). Acceptance is confirming evidence.
            engine.RestoreState(engine.State with { PerceivedExtraversion = 0.8 });
            var before = engine.State.PerceivedExtraversion;

            engine.Handle(Accepted(self), ctx, new EventCollector());

            var actualDelta = engine.State.PerceivedExtraversion - before;
            var nominalDelta = (1.0 - before) * cfg.PerceivedUpdateStep;
            var weightApplied = actualDelta / nominalDelta;

            Assert.IsTrue(Math.Abs(weightApplied - cfg.ConfirmingWeight) < 0.02,
                $"Confirming feedback must apply ~{cfg.ConfirmingWeight:F2}× nominal. Got: {weightApplied:F3}");

            // Asymmetry: confirming weight is ~4× the disconfirming weight.
            Assert.IsTrue(cfg.ConfirmingWeight > cfg.DisconfirmingWeight * 3.0,
                "Confirming feedback must dominate disconfirming feedback (self-verification asymmetry).");
        }

        #endregion

        #region Test 3 — high discrepancy seeds BuildIdentity (→ GoalActivated through GoalEngine)

        [TestMethod]
        public void SelfConcept_HighDiscrepancy_SeedsBuildIdentity()
        {
            var cfg = new SelfConceptConfig();
            var (engine, ctx, self) = MakeEngine(cfg);

            // Ideal far above perceived on all subset dims → discrepancy ≈ 0.7 > threshold.
            engine.RestoreState(engine.State with
            {
                IdealExtraversion = 0.9,
                IdealAgreeableness = 0.9,
                IdealConscientiousness = 0.9,
                PerceivedExtraversion = 0.2,
                PerceivedAgreeableness = 0.2,
                PerceivedConscientiousness = 0.2
            });

            var outbox = new EventCollector();
            engine.Tick(At(100), WTimeSpan.FromHours(1), ctx, outbox);

            var injected = outbox.Drain().OfType<GoalInjected>().FirstOrDefault();
            Assert.IsNotNull(injected, "High discrepancy must seed a goal.");
            Assert.AreEqual(PersistentGoalKind.BuildIdentity, injected!.Kind,
                "Seeded goal must be BuildIdentity.");

            // Feed the injection through a real GoalEngine → GoalActivated(BuildIdentity).
            var goalEngine = new DefaultGoalEngine(
                Options.Create(new GoalConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger<DefaultGoalEngine>());
            var goalOutbox = new EventCollector();
            goalEngine.Handle(injected, ctx, goalOutbox);

            var activated = goalOutbox.Drain().OfType<GoalActivated>().FirstOrDefault();
            Assert.IsNotNull(activated, "GoalEngine must activate the injected goal.");
            Assert.AreEqual(PersistentGoalKind.BuildIdentity, activated!.Kind);
        }

        #endregion

        #region Test 4 — self-esteem year-over-year stability

        [TestMethod]
        public void SelfEsteem_YearlyStability_InRange()
        {
            const int population = 60;
            const int eventsPerYear = 40;
            var rng = new Random(20260603);

            var initial = new double[population];
            var final = new double[population];

            var cfg = new SelfConceptConfig();

            for (var p = 0; p < population; p++)
            {
                var (engine, ctx, self) = MakeEngine(cfg);

                // Diverse starting esteem.
                var startEsteem = 0.2 + rng.NextDouble() * 0.6;
                engine.RestoreState(engine.State with { SelfEsteem = startEsteem });
                initial[p] = engine.State.SelfEsteem;

                // A year of individually-noisy social feedback (≈60% positive base rate).
                for (var e = 0; e < eventsPerYear; e++)
                {
                    var accepted = rng.NextDouble() < 0.6;
                    engine.Handle(accepted ? Accepted(self) : Rejected(self), ctx, new EventCollector());
                }

                final[p] = engine.State.SelfEsteem;
            }

            var r = Pearson(initial, final);
            var meanAbsChange = initial.Zip(final, (a, b) => Math.Abs(a - b)).Average();

            // High year-over-year stability (rank-order r ≈ .85–.95) but not frozen.
            Assert.IsTrue(r > 0.85, $"Self-esteem yearly stability must be high (r > 0.85). Got r={r:F3}");
            Assert.IsTrue(meanAbsChange > 0.005, $"Self-esteem must still change a little. Mean |Δ|={meanAbsChange:F4}");
        }

        #endregion

        #region Test 5 — acceptance: chronic rejection lowers perceived extraversion despite high actual,
        //                  and the effect is much smaller than equally-frequent confirming feedback.

        [TestMethod]
        public void SelfConcept_ChronicRejection_LowersPerceived_AsymmetricVsConfirming()
        {
            var cfg = new SelfConceptConfig();

            // (a) Emergent claim: a high-actual extravert, chronically rejected, still revises
            //     their perceived extraversion downward — self-view is not immutable.
            var (chronic, chronicCtx, chronicSelf) = MakeEngine(cfg);
            chronic.RestoreState(chronic.State with { PerceivedExtraversion = 0.85 });
            var start = chronic.State.PerceivedExtraversion;
            for (var i = 0; i < 60; i++)
                chronic.Handle(Rejected(chronicSelf), chronicCtx, new EventCollector());
            var drop = start - chronic.State.PerceivedExtraversion;
            Assert.IsTrue(drop > 0,
                $"Chronic rejection must lower perceived extraversion despite high actual. Drop: {drop:F3}");

            // (b) Asymmetry per unit of evidence: disconfirming feedback has far less impact per
            //     unit of distance than confirming feedback (self-verification discount).
            var (disc, discCtx, discSelf) = MakeEngine(cfg);
            disc.RestoreState(disc.State with { PerceivedExtraversion = 0.85 });
            disc.Handle(Rejected(discSelf), discCtx, new EventCollector());       // disconfirming
            var discMove = 0.85 - disc.State.PerceivedExtraversion;
            var discPerUnit = discMove / 0.85;                                    // distance to target (0)

            var (conf, confCtx, confSelf) = MakeEngine(cfg);
            conf.RestoreState(conf.State with { PerceivedExtraversion = 0.85 });
            conf.Handle(Accepted(confSelf), confCtx, new EventCollector());       // confirming
            var confMove = conf.State.PerceivedExtraversion - 0.85;
            var confPerUnit = confMove / 0.15;                                    // distance to target (1)

            Assert.IsTrue(confPerUnit > discPerUnit * 3.0,
                $"Confirming feedback must dominate per unit of evidence (~4×). " +
                $"confirming/unit={confPerUnit:F4}, disconfirming/unit={discPerUnit:F4}");
        }

        #endregion

        #region Helpers

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private InteractionOutcome Accepted(HumanId self)
            => new(At(100), self, new HumanId(Guid.NewGuid()), Accepted: true, "ok", SpeechAct.SmallTalk);

        private InteractionOutcome Rejected(HumanId self)
            => new(At(100), self, new HumanId(Guid.NewGuid()), Accepted: false, "no", SpeechAct.SmallTalk);

        private (DefaultSelfConceptEngine engine, IHumanContext ctx, HumanId self) MakeEngine(SelfConceptConfig cfg)
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = new DefaultSelfConceptEngine(Options.Create(cfg));
            engine.SeedFromPersonality(MakePersonality());
            return (engine, BuildContext(self), self);
        }

        private static IHumanContext BuildContext(HumanId self)
        {
            var personality = MakePersonality();
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "Test", Familiar = new[] { "Test" } },
                    new Surname { Male = "Human", Female = "Human" },
                    WDateOnly.New(80, 1, 1)),
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

        private static Personality MakePersonality()
            => new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        private static double Pearson(double[] x, double[] y)
        {
            var n = x.Length;
            var mx = x.Average();
            var my = y.Average();
            double cov = 0, vx = 0, vy = 0;
            for (var i = 0; i < n; i++)
            {
                var dx = x[i] - mx;
                var dy = y[i] - my;
                cov += dx * dy;
                vx += dx * dx;
                vy += dy * dy;
            }
            if (vx == 0 || vy == 0) return 0;
            return cov / (Math.Sqrt(vx) * Math.Sqrt(vy));
        }

        #endregion
    }
}
