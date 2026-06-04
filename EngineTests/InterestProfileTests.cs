// InterestProfileTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Interests;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for R5 interest profile: BigFive→RIASEC generation, sex priors, rewarding-episode
    /// drift, and the regression brake on the interest→salience→interest runaway loop.
    /// </summary>
    [TestClass]
    public class InterestProfileTests : TestBase
    {
        #region Test 1 — BigFive → RIASEC mapping signature (Larson 2002)

        [TestMethod]
        public void Interests_BigFiveMapping_DistributionMatchesLarson()
        {
            var rng = new Random(777);
            const int n = 2000;

            var openness = new double[n];
            var artistic = new double[n];
            var investigative = new double[n];
            var extraversion = new double[n];
            var enterprising = new double[n];
            var conscientiousness = new double[n];
            var conventional = new double[n];

            for (var i = 0; i < n; i++)
            {
                var o = rng.NextDouble();
                var c = rng.NextDouble();
                var e = rng.NextDouble();
                var a = rng.NextDouble();
                var nn = rng.NextDouble();
                var bf = new BigFive(o, c, e, a, nn);
                var p = InterestProfileGenerator.Generate(bf, SexBiology.Female, occupation: null, random: rng);

                openness[i] = o; artistic[i] = p.Artistic; investigative[i] = p.Investigative;
                extraversion[i] = e; enterprising[i] = p.Enterprising;
                conscientiousness[i] = c; conventional[i] = p.Conventional;
            }

            var rOArtistic = Pearson(openness, artistic);
            var rOInvestigative = Pearson(openness, investigative);
            var rEEnterprising = Pearson(extraversion, enterprising);
            var rCConventional = Pearson(conscientiousness, conventional);

            Assert.IsTrue(rOArtistic > 0.5, $"Openness→Artistic must be the strongest, positive. Got: {rOArtistic:F2}");
            Assert.IsTrue(rOArtistic > rOInvestigative, "Openness predicts Artistic more strongly than Investigative.");
            Assert.IsTrue(rEEnterprising > 0.4, $"Extraversion→Enterprising must be positive. Got: {rEEnterprising:F2}");
            Assert.IsTrue(rCConventional > 0.3, $"Conscientiousness→Conventional must be positive. Got: {rCConventional:F2}");
        }

        #endregion

        #region Test 2 — sex prior: Things–People gap with overlap

        [TestMethod]
        public void Interests_SexPrior_ThingsPeopleGap()
        {
            var rng = new Random(2024);
            const int n = 1000;

            var maleTP = new List<double>(n);
            var femaleTP = new List<double>(n);

            for (var i = 0; i < n; i++)
            {
                var bf = new BigFive(rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
                var male = InterestProfileGenerator.Generate(bf, SexBiology.Male, null, rng);
                var female = InterestProfileGenerator.Generate(bf, SexBiology.Female, null, rng);

                // Things–People proxy: Realistic − Social.
                maleTP.Add(male.Realistic - male.Social);
                femaleTP.Add(female.Realistic - female.Social);
            }

            var maleMean = maleTP.Average();
            var femaleMean = femaleTP.Average();

            // Men higher on Things–People at the population level.
            Assert.IsTrue(maleMean > femaleMean,
                $"Men must score higher on Things–People. male={maleMean:F3}, female={femaleMean:F3}");

            // …but with large within-sex overlap (some women exceed some men).
            var maleMin = maleTP.Min();
            var femaleMax = femaleTP.Max();
            Assert.IsTrue(femaleMax > maleMin,
                "Distributions must overlap substantially (no deterministic sex difference).");
        }

        #endregion

        #region Test 3 — rewarding episodes raise the matching dimension

        [TestMethod]
        public void Interests_RewardingEpisodes_RaiseDimension()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline, valence: 0.5); // rewarding context
            var before = engine.State.Current.Artistic;

            for (var i = 0; i < 10; i++)
                engine.Handle(new ActionCommitted(At(100), self, ActionNames.Create, WTimeSpan.FromHours(1)),
                    ctx, new EventCollector());

            var after = engine.State.Current.Artistic;
            Assert.IsTrue(after > before + 0.10,
                $"~10 rewarding Create episodes must push Artistic into the maintained band. before={before:F3}, after={after:F3}");
        }

        #endregion

        #region Test 4 — runaway guard: regression caps growth

        [TestMethod]
        public void Interests_RunawayGuard_RegressionCaps()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline, valence: 0.5);

            // Drive Artistic up with rewarding episodes.
            for (var i = 0; i < 10; i++)
                engine.Handle(new ActionCommitted(At(100), self, ActionNames.Create, WTimeSpan.FromHours(1)),
                    ctx, new EventCollector());
            var peak = engine.State.Current.Artistic;

            // Then stop. Over time it must regress back toward baseline (no runaway to 1.0).
            engine.Tick(At(100), WTimeSpan.FromDays(300), ctx, new EventCollector());
            var afterRest = engine.State.Current.Artistic;

            Assert.IsTrue(afterRest < peak,
                $"Interest must regress after rewards stop. peak={peak:F3}, afterRest={afterRest:F3}");
            Assert.IsTrue(afterRest < 1.0, "Interest must never run away to the ceiling.");
            Assert.IsTrue(afterRest > baseline.Artistic - 0.01,
                "Regression pulls toward baseline, not below it.");
        }

        #endregion

        #region Helpers

        private static InterestProfile Neutral()
            => new InterestProfile(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private (DefaultInterestEngine engine, IHumanContext ctx, HumanId self) MakeEngine(
            InterestProfile baseline, double valence, int birthYear = 70)
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = new DefaultInterestEngine(Options.Create(new InterestConfig()));
            engine.SeedFromBaseline(baseline);
            return (engine, BuildContext(self, valence, birthYear), self);
        }

        private static IHumanContext BuildContext(HumanId self, double valence, int birthYear)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure, CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);

            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(valence, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "T", Familiar = new[] { "T" } },
                    new Surname { Male = "H", Female = "H" },
                    WDateOnly.New(birthYear, 1, 1)),
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
                cov += dx * dy; vx += dx * dx; vy += dy * dy;
            }
            if (vx == 0 || vy == 0) return 0;
            return cov / (Math.Sqrt(vx) * Math.Sqrt(vy));
        }

        #endregion
    }
}
