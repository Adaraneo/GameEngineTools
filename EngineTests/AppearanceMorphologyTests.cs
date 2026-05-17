// AppearanceMorphologyTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using System.Text.Json;
    using TraitsProjector = GameEngineTools.Characters.Traits.AppearanceProjector;

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
                Assert.AreEqual(FaceShape.Oblong, DeriveFaceShape(a.Face.Craniofacial));
            }
            else if (ratio >= 0.86)
            {
                Assert.IsTrue(DeriveFaceShape(a.Face.Craniofacial) is FaceShape.Round or FaceShape.Square);
            }
        }

        #endregion Coherence

        #region Stadiums

        [TestMethod]
        public void Generate_BabyHasJuvenileMorphology()
        {
            var baby = _generator.Generate(SexBiology.Female, 444, StadiumType.Baby);

            Assert.IsTrue(baby.Face.Jaw.JawProminence < 0.45);
            Assert.IsTrue(baby.Face.Nose.NoseProjection < 0.65);
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

        #region Projection

        [TestMethod]
        public void Projector_UsesGeneratedHairLengthAndMorphology()
        {
            var appearance = _generator.Generate(SexBiology.Female, 888, StadiumType.Adult);
            var physio = new PhysiologyState(70, 0, 25, 20, 5, 10, 0, null);

            var view = TraitsProjector.Compute(appearance, physio, SexBiology.Female);

            Assert.AreEqual(appearance.HairLengthCm, view.HairLengthCm, delta: 0.05);
            Assert.IsTrue(view.PostureScore <= Math.Round(appearance.Body.Posture.PostureUprightness * 100.0, 1));
            Assert.IsTrue(view.PostureScore >= Math.Round(appearance.Body.Posture.PostureUprightness * 100.0, 1) - 10.0);
        }

        #endregion Projection

        #region Sex overlap

        [TestMethod]
        public void Generate_MaleFemalePopulationsHaveShoulderBreadthOverlap()
        {
            var femaleShoulders = new List<double>();
            var maleShoulders = new List<double>();

            for (var i = 0; i < 40; i++)
            {
                femaleShoulders.Add(_generator.Generate(SexBiology.Female, 1000 + i, StadiumType.Adult).Body.Skeletal.ShoulderBreadth);
                maleShoulders.Add(_generator.Generate(SexBiology.Male, 2000 + i, StadiumType.Adult).Body.Skeletal.ShoulderBreadth);
            }

            Assert.IsTrue(femaleShoulders.Max() > maleShoulders.Min());
        }

        #endregion Sex overlap

        #region Serialization

        [TestMethod]
        public void Serialize_PhysicalAppearance_DoesNotWriteConvenienceAliases()
        {
            var appearance = _generator.Generate(SexBiology.Female, 777, StadiumType.Adult);

            var json = JsonSerializer.Serialize(appearance);

            Assert.IsTrue(json.Contains("\"Body\""));
            Assert.IsTrue(json.Contains("\"Face\""));
            Assert.IsTrue(json.Contains("\"Surface\""));
            Assert.IsTrue(json.Contains("\"Colors\""));
            Assert.IsFalse(json.Contains("\"BodyMorphology\""));
            Assert.IsFalse(json.Contains("\"FacialMorphology\""));
        }

        #endregion Serialization

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

        private static FaceShape DeriveFaceShape(CraniofacialStructure c)
        {
            var ratio = c.FaceWidthToHeightRatio;
            var jawToFace = c.JawWidth / Math.Max(1.0, c.FaceWidth);
            var cheekToJaw = c.CheekboneWidth / Math.Max(1.0, c.JawWidth);
            if (ratio >= 0.86 && jawToFace >= 0.80)
            {
                return FaceShape.Square;
            }

            if (ratio >= 0.86)
            {
                return FaceShape.Round;
            }

            if (ratio <= 0.72)
            {
                return FaceShape.Oblong;
            }

            if (cheekToJaw >= 1.23)
            {
                return FaceShape.Diamond;
            }

            return jawToFace <= 0.68 ? FaceShape.Heart : FaceShape.Oval;
        }

        #endregion Test infrastructure
    }
}
