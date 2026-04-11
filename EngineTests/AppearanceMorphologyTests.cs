// AppearanceMorphologyTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools;

    /// <summary>
    /// Tests for high-resolution appearance morphology generation.
    /// </summary>
    [TestClass]
    public class AppearanceMorphologyTests
    {
        #region Fields

        private AppearanceGenerator _generator = default!;

        #endregion Fields

        #region Setup

        [TestInitialize]
        public void Setup()
            => _generator = new AppearanceGenerator(new LocalRandomSourceFactory());

        #endregion Setup

        #region Determinism

        [TestMethod]
        public void Generate_SameSeedSexAndStadium_IsDeterministic()
        {
            var a = _generator.Generate(SexBiology.Female, 12345, StadiumType.Adult);
            var b = _generator.Generate(SexBiology.Female, 12345, StadiumType.Adult);

            Assert.AreEqual(a, b);
        }

        [TestMethod]
        public void Generate_DifferentSeeds_ProducesDifferentMorphology()
        {
            var a = _generator.Generate(SexBiology.Female, 12345, StadiumType.Adult);
            var b = _generator.Generate(SexBiology.Female, 54321, StadiumType.Adult);

            Assert.IsTrue(
                a.Body.Skeletal.ShoulderBreadth != b.Body.Skeletal.ShoulderBreadth ||
                a.Face.Nose.NoseProjection != b.Face.Nose.NoseProjection ||
                a.Face.Mouth.MouthWidth != b.Face.Mouth.MouthWidth);
        }

        #endregion Determinism

        #region Coherence

        [TestMethod]
        public void Generate_BodyProportionsStayCoherent()
        {
            var a = _generator.Generate(SexBiology.Male, 111, StadiumType.Adult);
            var p = a.Body.Proportions;

            Assert.AreEqual(p.HeightCm, p.LegLength + p.TorsoLength, delta: 0.2);
            Assert.IsTrue(p.LegToTorsoRatio is > 0.55 and < 1.25);
            Assert.IsTrue(a.Body.Skeletal.HandSize < a.Body.Skeletal.FootSize);
            Assert.IsTrue(a.Body.Silhouette.HipWidth > 0);
            Assert.IsTrue(a.Body.Proportions.WaistToHipRatio is > 0.45 and < 1.25);
        }

        [TestMethod]
        public void Generate_EyeSpacingIsPhysicallyPlausibleRelativeToEyeWidth()
        {
            var a = _generator.Generate(SexBiology.Female, 222, StadiumType.Adult);

            Assert.IsTrue(a.Face.EyeRegion.EyeSpacing >= a.Face.EyeRegion.EyeWidth * 0.85);
            Assert.IsTrue(a.Face.EyeRegion.EyeSpacing < a.Face.Craniofacial.FaceWidth * 0.40);
        }

        [TestMethod]
        public void Generate_DerivedFaceShapeMatchesProportionThreshold()
        {
            var a = _generator.Generate(SexBiology.Female, 333, StadiumType.Adult);
            var ratio = a.Face.Craniofacial.FaceWidthToHeightRatio;

            if (ratio <= 0.72)
            {
                Assert.AreEqual(FaceShape.Oblong, a.FaceShape);
            }
            else if (ratio >= 0.86)
            {
                Assert.IsTrue(a.FaceShape is FaceShape.Round or FaceShape.Square);
            }
        }

        #endregion Coherence

        #region Stadiums

        [TestMethod]
        public void Generate_BabyHasJuvenileMorphology()
        {
            var baby = _generator.Generate(SexBiology.Female, 444, StadiumType.Baby);

            Assert.IsTrue(baby.Face.Jaw.JawProminence < 0.45);
            Assert.IsTrue(baby.Face.Nose.NoseProjection < 0.55);
            Assert.IsTrue(baby.Face.Cheeks.CheekFullness > 0.55);
            Assert.IsTrue(baby.Body.Proportions.SittingHeight / baby.Body.Proportions.HeightCm > 0.55);
        }

        [TestMethod]
        public void Generate_OldHasHigherAgeSurfaceFactorThanAdult()
        {
            var adult = _generator.Generate(SexBiology.Male, 555, StadiumType.Adult);
            var old = _generator.Generate(SexBiology.Male, 555, StadiumType.Old);

            Assert.IsTrue(old.Surface.AgeSurfaceFactor > adult.Surface.AgeSurfaceFactor);
            Assert.IsTrue(old.Surface.WrinkleTendency > adult.Surface.WrinkleTendency);
            Assert.IsTrue(old.Body.Posture.HeadForwardness >= adult.Body.Posture.HeadForwardness);
        }

        #endregion Stadiums

        #region Sex overlap

        [TestMethod]
        public void Generate_MaleFemalePopulationsHaveShoulderBreadthOverlap()
        {
            var femaleShoulders = new List<double>();
            var maleShoulders = new List<double>();

            for (var i = 0; i < 40; i++)
            {
                femaleShoulders.Add(_generator.Generate(SexBiology.Female, 1000 + i, StadiumType.Adult).ShoulderBreadthCm);
                maleShoulders.Add(_generator.Generate(SexBiology.Male, 2000 + i, StadiumType.Adult).ShoulderBreadthCm);
            }

            Assert.IsTrue(femaleShoulders.Max() > maleShoulders.Min());
        }

        #endregion Sex overlap

        #region Test infrastructure

        private sealed class LocalRandomSourceFactory : IRandomSourceFactory
        {
            public IRandomSource Create(int seed)
                => new LocalRandom(seed);
        }

        private sealed class LocalRandom : IRandomSource
        {
            private readonly Random _random;

            public LocalRandom(int seed)
                => _random = new Random(seed);

            public int Next(int min, int max)
                => _random.Next(min, max);

            public double NextUnit()
                => _random.NextDouble();

            public bool Chance(double p)
                => _random.NextDouble() < p;
        }

        #endregion Test infrastructure
    }
}
