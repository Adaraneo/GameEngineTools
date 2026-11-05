// InterestProfileTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Interests;
    using GameEngineTools.Characters.Engines.Schedule;
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

        #endregion Test 1 — BigFive → RIASEC mapping signature (Larson 2002)

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

        #endregion Test 2 — sex prior: Things–People gap with overlap

        #region Test 3 — rewarding episodes raise the matching dimension

        [TestMethod]
        public void Interests_RewardingEpisodes_RaiseDimension()
        {
            var (engine, ctx, self) = MakeEngine(Neutral(), valence: 0.5);
            var before = engine.State.Current.Artistic;

            DailyFor(engine, ctx, self, ActionNames.Create, days: 360);

            var gain = engine.State.Current.Artistic - before;
            Assert.IsTrue(gain is > 0.02 and < 0.07,
                $"A year of daily rewarding practice moves an adult's interest by a few hundredths (mean-level change is d≈0.1 per developmental period). gain={gain:F3}");
        }

        [TestMethod]
        public void Interests_AdolescentsDriftFasterThanAdults()
        {
            var (adult, adultCtx, adultSelf) = MakeEngine(Neutral(), valence: 0.5, birthYear: 70);
            var (teen, teenCtx, teenSelf) = MakeEngine(Neutral(), valence: 0.5, birthYear: 86);

            DailyFor(adult, adultCtx, adultSelf, ActionNames.Create, days: 360);
            DailyFor(teen, teenCtx, teenSelf, ActionNames.Create, days: 360);

            Assert.IsTrue(teen.State.Current.Artistic > adult.State.Current.Artistic + 0.02, "Interests are most fluid until the early twenties (Low 2005).");
        }

        [TestMethod]
        public void Interests_ReinforceOncePerDayPerDimension()
        {
            var (engine, ctx, self) = MakeEngine(Neutral(), valence: 0.5);

            for (var i = 0; i < 50; i++)
                engine.Handle(new ActionCommitted(At(100), self, ActionNames.Create, WTimeSpan.FromHours(1)), ctx, new EventCollector());
            var afterBurst = engine.State.Current.Artistic;
            engine.Handle(new ActionCommitted(At(100) + WTimeSpan.FromDays(1), self, ActionNames.Create, WTimeSpan.FromHours(1)), ctx, new EventCollector());

            Assert.AreEqual(0.5 + 0.0003 * 0.5, afterBurst, 1e-9, "Fifty rewarding commits within one day count once.");
            Assert.IsTrue(engine.State.Current.Artistic > afterBurst, "The next day reinforces again.");
        }

        [TestMethod]
        public void Interests_WorkReinforcesTheOccupationsTheme_NotAlwaysConventional()
        {
            double Gain(string? occupation, string action, Func<InterestProfile, double> dimension)
            {
                var (engine, ctx, self) = MakeEngine(Neutral(), valence: 0.5);
                ((HumanContext)ctx).Snapshot = ctx.Snapshot with { Schedule = new DailyScheduleState(Array.Empty<ScheduleSlot>(), null, -1, occupation) };
                engine.Handle(new ActionCommitted(At(100), self, action, WTimeSpan.FromHours(1)), ctx, new EventCollector());
                return dimension(engine.State.Current) - dimension(Neutral());
            }

            Assert.IsTrue(Gain("scholar", ActionNames.Work, p => p.Investigative) > 0, "A scholar's work is analytic.");
            Assert.AreEqual(0.0, Gain("scholar", ActionNames.Work, p => p.Conventional), 1e-12, "It is not clerical.");
            Assert.IsTrue(Gain("clerk", ActionNames.Work, p => p.Conventional) > 0);
            Assert.IsTrue(Gain("farmer", ActionNames.Work, p => p.Realistic) > 0);
            Assert.AreEqual(0.0, Gain(null, ActionNames.Work, p => p.Conventional), 1e-12, "Work without a known occupation teaches nothing.");
            Assert.IsTrue(Gain(null, ActionNames.Produce, p => p.Realistic) > 0, "Producing goods is hands-on.");
            Assert.IsTrue(Gain(null, ActionNames.Sell, p => p.Enterprising) > 0, "Selling is persuading.");
            Assert.AreEqual(0.0, Gain(null, ActionNames.InviteIntimacy, p => p.Social), 1e-12, "An intimate invitation is not a vocational interest.");
        }

        #endregion Test 3 — rewarding episodes raise the matching dimension

        #region Test 4 — runaway guard: regression caps growth

        [TestMethod]
        public void Interests_RunawayGuard_RegressionCaps()
        {
            var baseline = Neutral();
            var (engine, ctx, self) = MakeEngine(baseline, valence: 0.5);

            // Five years of daily reinforcement still settles a modest distance from baseline.
            DailyFor(engine, ctx, self, ActionNames.Create, days: 1800);
            var peak = engine.State.Current.Artistic;
            Assert.IsTrue(peak - baseline.Artistic < 0.12, $"The sustained drift is bounded by the regression brake. peak={peak:F3}");

            // Then stop. Over time it must regress back toward baseline (no runaway to 1.0).
            engine.Tick(At(100) + WTimeSpan.FromDays(1800), WTimeSpan.FromDays(300), ctx, new EventCollector());
            var afterRest = engine.State.Current.Artistic;

            Assert.IsTrue(afterRest < peak,
                $"Interest must regress after rewards stop. peak={peak:F3}, afterRest={afterRest:F3}");
            Assert.IsTrue(afterRest < 1.0, "Interest must never run away to the ceiling.");
            Assert.IsTrue(afterRest > baseline.Artistic - 0.01,
                "Regression pulls toward baseline, not below it.");
        }

        #endregion Test 4 — runaway guard: regression caps growth

        #region Test 5 — neutral-mood routine actions do not saturate Social (saturation fix)

        [TestMethod]
        public void InterestProfile_NeutralValenceRoutineActions_DoNotSaturateSocial()
        {
            var baseline = Neutral();
            // Documented baseline PAD valence is +0.1..+0.2: a contented, neutral mood — not a reward.
            var (engine, ctx, self) = MakeEngine(baseline, valence: 0.15);

            DailyFor(engine, ctx, self, ActionNames.ReachOut, days: 500);

            var social = engine.State.Current.Social;
            Assert.IsTrue(Math.Abs(social - baseline.Social) < 0.2,
                $"Baseline-mood routine social actions must not saturate Social. " +
                $"baseline={baseline.Social:F3}, social={social:F3}");
            Assert.IsTrue(social < 0.99, "Social must not reach the ceiling from mere baseline mood.");
        }

        #endregion Test 5 — neutral-mood routine actions do not saturate Social (saturation fix)

        #region Test 6 — a genuinely rewarding moment still raises interest (regression guard)

        [TestMethod]
        public void InterestProfile_GenuinelyRewardingMoment_StillRaisesInterest()
        {
            var baseline = Neutral();
            // Clearly above-baseline positive moment.
            var (engine, ctx, self) = MakeEngine(baseline, valence: 0.6);
            var before = engine.State.Current.Social;

            DailyFor(engine, ctx, self, ActionNames.ReachOut, days: 100);

            var after = engine.State.Current.Social;
            Assert.IsTrue(after > before + 0.005,
                $"A genuinely rewarding moment must still raise interest after the threshold change. " +
                $"before={before:F3}, after={after:F3}");
        }

        #endregion Test 6 — a genuinely rewarding moment still raises interest (regression guard)

        [TestMethod]
        public void InterestProfile_GainShrinksNearThePole_SoMoreFrequentRewardsEndHigherButNeverPin()
        {
            double StepFrom(double start)
            {
                var (engine, ctx, self) = MakeEngine(new InterestProfile(0.5, 0.5, 0.5, start, 0.5, 0.5), valence: 0.6);
                engine.Handle(new ActionCommitted(At(100), self, ActionNames.ReachOut, WTimeSpan.FromHours(1)), ctx, new EventCollector());
                return engine.State.Current.Social - start;
            }

            Assert.AreEqual(StepFrom(0.5) / 5.0, StepFrom(0.9), 1e-12, "The gain is proportional to the headroom (1 − interest).");
            Assert.IsTrue(StepFrom(1.0) == 0.0, "At the pole nothing moves.");
        }

        #region Helpers

        private static InterestProfile Neutral()
            => new InterestProfile(0.5, 0.5, 0.5, 0.5, 0.5, 0.5);

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        private static void DailyFor(DefaultInterestEngine engine, IHumanContext ctx, HumanId self, string action, int days)
        {
            for (var day = 0; day < days; day++)
            {
                var now = At(100) + WTimeSpan.FromDays(day);
                engine.Handle(new ActionCommitted(now, self, action, WTimeSpan.FromHours(1)), ctx, new EventCollector());
                engine.Tick(now, WTimeSpan.FromDays(1), ctx, new EventCollector());
            }
        }

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

        #endregion Helpers
    }
}
