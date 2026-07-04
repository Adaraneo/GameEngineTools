// TransferenceMathTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;
    using Microsoft.Extensions.DependencyInjection;
    using System.Collections.Generic;
    using System.Linq;

    [TestClass]
    public class TransferenceMathTests : TestBase
    {
        private static readonly RelationshipsConfig DefaultCfg = new();

        #region PersonalityResemblance

        [TestMethod]
        public void PersonalityResemblance_IdenticalProfiles_ReturnsOne()
        {
            var a = new BigFive(0.4, 0.6, 0.5, 0.7, 0.3);
            var b = new BigFive(0.4, 0.6, 0.5, 0.7, 0.3);

            Assert.AreEqual(1.0, TransferenceMath.PersonalityResemblance(a, b), 0.0001);
        }

        [TestMethod]
        public void PersonalityResemblance_OppositeProfiles_ReturnsZero()
        {
            var a = new BigFive(0.0, 0.0, 0.0, 0.0, 0.0);
            var b = new BigFive(1.0, 1.0, 1.0, 1.0, 1.0);

            Assert.AreEqual(0.0, TransferenceMath.PersonalityResemblance(a, b), 0.0001);
        }

        [TestMethod]
        public void PersonalityResemblance_PartialSimilarity_ReturnsProportionalScore()
        {
            var identical = TransferenceMath.PersonalityResemblance(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), new BigFive(0.5, 0.5, 0.5, 0.5, 0.5));
            var slightlyOff = TransferenceMath.PersonalityResemblance(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), new BigFive(0.6, 0.5, 0.5, 0.5, 0.5));
            var veryOff = TransferenceMath.PersonalityResemblance(new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), new BigFive(1.0, 1.0, 0.5, 0.5, 0.5));

            Assert.AreEqual(1.0, identical, 0.0001);
            Assert.IsTrue(slightlyOff < identical && slightlyOff > veryOff,
                $"Resemblance should decrease proportionally with distance (identical={identical:F3}, slightlyOff={slightlyOff:F3}, veryOff={veryOff:F3})");
        }

        #endregion PersonalityResemblance

        #region FacialResemblance

        private FacialMorphology Face(int seed)
        {
            var rngFactory = ServiceProvider.GetRequiredService<IRandomSourceFactory>();
            return new AppearanceGenerator(rngFactory).Generate(SexBiology.Female, seed).Face;
        }

        [TestMethod]
        public void FacialResemblance_IdenticalDescriptor_ReturnsOne()
        {
            var face = Face(1);
            Assert.AreEqual(1.0, TransferenceMath.FacialResemblance(face, face), 0.0001);
        }

        [TestMethod]
        public void FacialResemblance_DifferentDescriptors_ReturnsLessThanOne()
        {
            var faceA = Face(1);
            var faceB = Face(2);

            var resemblance = TransferenceMath.FacialResemblance(faceA, faceB);
            Assert.IsTrue(resemblance < 1.0 && resemblance >= 0.0,
                $"Different faces must resemble less than an identical face (got {resemblance:F3})");
        }

        [TestMethod]
        public void FacialResemblance_ManyDifferentSeeds_SpanAWideRange()
        {
            // With no single canonical "maximally different" pair available from the generator,
            // sample several seed pairs and confirm the metric produces a spread of scores rather
            // than collapsing to a constant — i.e. it is actually sensitive to the input.
            var baseline = Face(100);
            var scores = new List<double>();
            for (var seed = 101; seed < 110; seed++)
            {
                scores.Add(TransferenceMath.FacialResemblance(baseline, Face(seed)));
            }

            Assert.IsTrue(scores.Max() - scores.Min() > 0.01,
                $"FacialResemblance should vary across different comparison faces (min={scores.Min():F3}, max={scores.Max():F3})");
            Assert.IsTrue(scores.All(s => s >= 0.0 && s <= 1.0));
        }

        #endregion FacialResemblance

        #region CombinedResemblance / SexWeighted

        [TestMethod]
        public void CombinedResemblance_BlendsBothComponents_AtDefaultWeight()
        {
            var cfg = DefaultCfg with { ApplySexDifferentiatedFacialResemblance = false };
            var combined = TransferenceMath.CombinedResemblance(1.0, 0.0, SexBiology.Female, cfg, faceWeight: 0.5);
            Assert.AreEqual(0.5, combined, 0.0001, "At default 0.5 weight, facial=1.0 and personality=0.0 should blend to 0.5");
        }

        [TestMethod]
        public void SexWeightedFacialResemblance_FemaleObserver_HigherThanMale_AtSameRawResemblance()
        {
            var female = TransferenceMath.SexWeightedFacialResemblance(0.5, SexBiology.Female, DefaultCfg);
            var male = TransferenceMath.SexWeightedFacialResemblance(0.5, SexBiology.Male, DefaultCfg);

            Assert.IsTrue(female > male,
                $"Female observers should weight facial resemblance higher per Günaydın et al. 2012 (female={female:F3}, male={male:F3})");
        }

        [TestMethod]
        public void SexWeightedFacialResemblance_Disabled_FallsBackToSexNeutral()
        {
            var cfg = DefaultCfg with { ApplySexDifferentiatedFacialResemblance = false };
            var female = TransferenceMath.SexWeightedFacialResemblance(0.5, SexBiology.Female, cfg);
            var male = TransferenceMath.SexWeightedFacialResemblance(0.5, SexBiology.Male, cfg);

            Assert.AreEqual(0.5, female, 0.0001);
            Assert.AreEqual(0.5, male, 0.0001);
        }

        #endregion CombinedResemblance / SexWeighted
    }
}
