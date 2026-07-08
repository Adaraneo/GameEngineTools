// NutritionMathTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Physiology;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class NutritionMathTests
    {
        #region Vitamin C x non-heme iron

        [TestMethod]
        public void ComputeVitaminCIronMultiplier_NoVitaminC_ReturnsOne()
        {
            Assert.AreEqual(1.0, NutritionMath.ComputeVitaminCIronMultiplier(0), 0.0001);
        }

        [TestMethod]
        public void ComputeVitaminCIronMultiplier_LowAnchor_MatchesLiteratureRatio()
        {
            // Source: Cook & Monsen 1977, Am J Clin Nutr 30(2):235-241 — 25mg -> 1.65x
            Assert.AreEqual(1.65, NutritionMath.ComputeVitaminCIronMultiplier(25), 0.01);
        }

        [TestMethod]
        public void ComputeVitaminCIronMultiplier_HighAnchor_IsCappedForGameplay()
        {
            // Literature ratio at 1000mg is 9.57x, but the engine caps at 3.5x for gameplay
            // realism (single-meal pharmacologic doses are not representative of food servings).
            var result = NutritionMath.ComputeVitaminCIronMultiplier(1000);
            Assert.AreEqual(3.5, result, 0.0001);
            Assert.IsTrue(result < 9.57, "Gameplay cap must stay below the pharmacologic ceiling.");
        }

        [TestMethod]
        public void ComputeEffectiveIronGain_HemeIronUnaffectedByVitaminC()
        {
            // Fully heme source (fraction=1.0): vitamin C must have no effect.
            var withVitC = NutritionMath.ComputeEffectiveIronGain(10.0, hemeIronFraction: 1.0, vitaminCMilligrams: 500);
            var withoutVitC = NutritionMath.ComputeEffectiveIronGain(10.0, hemeIronFraction: 1.0, vitaminCMilligrams: 0);
            Assert.AreEqual(withoutVitC, withVitC, 0.0001);
        }

        [TestMethod]
        public void ComputeEffectiveIronGain_NonHemeIronBoostedByVitaminC()
        {
            // Fully non-heme source (fraction=0.0): vitamin C must increase effective gain.
            var withVitC = NutritionMath.ComputeEffectiveIronGain(10.0, hemeIronFraction: 0.0, vitaminCMilligrams: 500);
            var withoutVitC = NutritionMath.ComputeEffectiveIronGain(10.0, hemeIronFraction: 0.0, vitaminCMilligrams: 0);
            Assert.IsTrue(withVitC > withoutVitC,
                $"Vitamin C must boost non-heme iron gain. With={withVitC:F2}, Without={withoutVitC:F2}");
        }

        #endregion Vitamin C x non-heme iron
    }
}
