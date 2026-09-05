// CommunityReputationTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.World.Utils.Time;
    using System;

    /// <summary>
    /// Tests for R7 community reputation: aggregation of third-party observations, recency decay,
    /// newcomer trust priors, and Nowak–Sigmund cooperation stability.
    /// </summary>
    [TestClass]
    public class CommunityReputationTests : TestBase
    {
        #region Test 1 — aggregates from observations

        [TestMethod]
        public void Reputation_Aggregates_FromObservations()
        {
            var ledger = new CommunityReputationLedger();
            var subject = new HumanId(Guid.NewGuid());
            const string loc = "town-square";

            for (var i = 0; i < 25; i++)
                ledger.Observe(subject, loc, ThirdPartyObservationType.PositiveAct, At(100));

            var rep = ledger.Get(subject, loc);
            Assert.IsNotNull(rep, "Reputation must exist after observations.");
            Assert.IsTrue(rep!.Score > 0.7, $"Repeated positive acts must build a high score. Got: {rep.Score:F3}");
            Assert.IsTrue(rep.Spread > 0.7, $"Repeated observations must spread the reputation. Got: {rep.Spread:F3}");

            // A subject observed badly converges negative.
            var villain = new HumanId(Guid.NewGuid());
            for (var i = 0; i < 25; i++)
                ledger.Observe(villain, loc, ThirdPartyObservationType.NegativeAct, At(100));
            Assert.IsTrue(ledger.Get(villain, loc)!.Score < -0.7, "Repeated negative acts must build a low score.");
        }

        #endregion Test 1 — aggregates from observations

        #region Test 2 — recency decay half-life

        [TestMethod]
        public void Reputation_RecencyDecay_HalfLife()
        {
            const double halfLife = 7.0;

            var atHalfLife = ReputationMath.RecencyWeight(halfLife, halfLife);
            Assert.IsTrue(Math.Abs(atHalfLife - 0.5) < 1e-6,
                $"An observation one half-life ago must weigh ≈ 0.5. Got: {atHalfLife:F4}");

            var atTwoHalfLives = ReputationMath.RecencyWeight(2 * halfLife, halfLife);
            Assert.IsTrue(Math.Abs(atTwoHalfLives - 0.25) < 1e-6,
                $"Two half-lives ago must weigh ≈ 0.25. Got: {atTwoHalfLives:F4}");

            // Strictly decreasing with age.
            Assert.IsTrue(ReputationMath.RecencyWeight(1, halfLife) > ReputationMath.RecencyWeight(5, halfLife),
                "Older observations must weigh less.");
        }

        #endregion Test 2 — recency decay half-life

        #region Test 3 — initial trust prior shifts with score

        [TestMethod]
        public void Reputation_InitialTrustPrior_ShiftsWithScore()
        {
            var ledger = new CommunityReputationLedger();
            const string loc = "village";

            // Unknown subject → neutral baseline.
            var stranger = new HumanId(Guid.NewGuid());
            Assert.AreEqual(ReputationMath.DefaultTrustPrior, ledger.InitialTrustPrior(stranger, loc), 1e-9,
                "An unknown subject gets the neutral trust baseline (~0.4).");

            // Well-regarded subject → elevated prior (~0.7).
            var hero = new HumanId(Guid.NewGuid());
            for (var i = 0; i < 40; i++)
                ledger.Observe(hero, loc, ThirdPartyObservationType.PositiveAct, At(100));
            var heroPrior = ledger.InitialTrustPrior(hero, loc);
            Assert.IsTrue(heroPrior > 0.6, $"Positive reputation must raise the trust prior toward 0.7. Got: {heroPrior:F3}");

            // Ill-regarded subject → suppressed prior (~0.15).
            var villain = new HumanId(Guid.NewGuid());
            for (var i = 0; i < 40; i++)
                ledger.Observe(villain, loc, ThirdPartyObservationType.NegativeAct, At(100));
            var villainPrior = ledger.InitialTrustPrior(villain, loc);
            Assert.IsTrue(villainPrior < 0.22, $"Negative reputation must lower the trust prior toward 0.15. Got: {villainPrior:F3}");

            Assert.IsTrue(heroPrior > villainPrior, "Hero must be trusted more than villain on arrival.");
        }

        #endregion Test 3 — initial trust prior shifts with score

        #region Test 4 — cooperation collapses below the Nowak–Sigmund threshold

        [TestMethod]
        public void Reputation_CooperationCollapses_BelowThreshold()
        {
            const double costBenefit = 0.5; // c/b

            Assert.IsFalse(ReputationMath.CooperationStable(spread: 0.2, costBenefit),
                "When reputation spread q < c/b, cooperation is not stable.");
            Assert.IsTrue(ReputationMath.CooperationStable(spread: 0.8, costBenefit),
                "When q > c/b, cooperation is stable.");

            // Emergent: a barely-observed subject (low spread) cannot sustain cooperative treatment.
            var ledger = new CommunityReputationLedger();
            var subject = new HumanId(Guid.NewGuid());
            const string loc = "hamlet";
            ledger.Observe(subject, loc, ThirdPartyObservationType.PositiveAct, At(100)); // single observation → tiny spread
            var rep = ledger.Get(subject, loc)!;
            Assert.IsFalse(ReputationMath.CooperationStable(rep.Spread, costBenefit),
                $"A single observation leaves spread too low for stable cooperation. Spread={rep.Spread:F3}");
        }

        #endregion Test 4 — cooperation collapses below the Nowak–Sigmund threshold

        #region Test 5 — stern judging: negativity bias moves reputation harder

        [TestMethod]
        public void Reputation_SternJudging_NegativityBias()
        {
            const double halfLife = 7.0;

            var upFromZero = ReputationMath.UpdateScore(0.0, ThirdPartyObservationType.PositiveAct, halfLife);
            var downFromZero = ReputationMath.UpdateScore(0.0, ThirdPartyObservationType.NegativeAct, halfLife);

            Assert.IsTrue(Math.Abs(downFromZero) > Math.Abs(upFromZero),
                $"Negative acts must move reputation harder than positive (stern judging). " +
                $"up={upFromZero:F4}, down={downFromZero:F4}");

            // Intimate acts are reputation-neutral.
            Assert.AreEqual(0.3, ReputationMath.UpdateScore(0.3, ThirdPartyObservationType.IntimateAct, halfLife), 1e-9,
                "Intimate observations do not change the image score.");
        }

        #endregion Test 5 — stern judging: negativity bias moves reputation harder

        #region Helpers

        private static WDateTime At(int year) => WDateOnly.New(year, 1, 1).ToDateTime();

        #endregion Helpers
    }
}
