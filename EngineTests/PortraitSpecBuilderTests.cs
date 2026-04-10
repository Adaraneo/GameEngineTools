// PortraitSpecBuilderTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Generation.Portraits;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for deterministic portrait mapping and prompt formatting.
    /// </summary>
    [TestClass]
    public class PortraitSpecBuilderTests
    {
        private PortraitSpecBuilder _builder = default!;
        private PortraitPromptFormatter _formatter = default!;

        [TestInitialize]
        public void Setup()
        {
            _builder = new PortraitSpecBuilder();
            _formatter = new PortraitPromptFormatter();
        }

        [TestMethod]
        public void Build_SameAppearanceAndSnapshot_ProducesEqualSpec()
        {
            var appearance = BuildReferenceAppearance();
            var snapshot = BuildSnapshot();

            var left = _builder.Build(SexBiology.Female, appearance, snapshot);
            var right = _builder.Build(SexBiology.Female, appearance, snapshot);

            Assert.AreEqual(left.Body, right.Body);
            Assert.AreEqual(left.Skin, right.Skin);
            Assert.AreEqual(left.Eyes, right.Eyes);
            Assert.AreEqual(left.Hair, right.Hair);
            Assert.AreEqual(left.Face, right.Face);
            Assert.AreEqual(left.Expression, right.Expression);
            Assert.AreEqual(left.BiasGuard, right.BiasGuard);
            CollectionAssert.AreEqual(left.DistinctiveMarks.ToArray(), right.DistinctiveMarks.ToArray());
        }

        [TestMethod]
        public void Build_ReferenceAppearance_MapsEnumsExactly()
        {
            var spec = _builder.Build(SexBiology.Female, BuildReferenceAppearance());

            Assert.AreEqual("blue", spec.Eyes.HueFamily);
            Assert.AreEqual("dark blond", spec.Hair.BaseColorFamily);
            Assert.AreEqual("straight", spec.Hair.Straightness);
            Assert.AreEqual("oval", spec.Face.ShapeLabel);
            Assert.AreEqual("fair", spec.Skin.ToneLabel);
            Assert.IsTrue(spec.Skin.PreserveNaturalTexture);
            Assert.IsFalse(spec.Skin.AllowSmoothing);
        }

        [TestMethod]
        public void Build_ReferenceAppearance_MapsBodyAndFeatureBuckets()
        {
            var spec = _builder.Build(SexBiology.Female, BuildReferenceAppearance());

            Assert.AreEqual("medium height", spec.Body.HeightBucket);
            Assert.AreEqual("balanced proportions", spec.Body.ProportionBucket);
            Assert.AreEqual("petite frame with slightly hip-led silhouette", spec.Body.FrameImpression);
            Assert.AreEqual("moderate projection", spec.Face.NoseProjectionBucket);
            Assert.AreEqual("medium-full", spec.Face.LipFullnessBucket);
        }

        [TestMethod]
        public void Build_AlwaysSetsBeautificationGuards()
        {
            var spec = _builder.Build(SexBiology.Female, BuildReferenceAppearance());

            Assert.IsTrue(spec.BiasGuard.ForbidSymmetryEnhancement);
            Assert.IsTrue(spec.BiasGuard.ForbidSkinSmoothing);
            Assert.IsTrue(spec.BiasGuard.ForbidEyeEnlargement);
            Assert.IsTrue(spec.BiasGuard.ForbidLipEnhancement);
            Assert.IsTrue(spec.BiasGuard.ForbidAestheticReinterpretation);
            Assert.IsTrue(spec.BiasGuard.ForbidForcedSmile);
        }

        [TestMethod]
        public void Build_NeutralSnapshot_UsesNeutralExpression()
        {
            var spec = _builder.Build(SexBiology.Female, BuildReferenceAppearance(), BuildSnapshot());

            Assert.AreEqual(PortraitExpressionKind.Neutral, spec.Expression.Kind);
            Assert.AreEqual("neutral", spec.Expression.ExpressionLabel);
        }

        [TestMethod]
        public void Build_HighSleepDebt_UsesTiredExpression()
        {
            var spec = _builder.Build(
                SexBiology.Female,
                BuildReferenceAppearance(),
                BuildSnapshot(
                    physiology: new PhysiologyState(70, 12, 25, 20, 5, 10, 0, null),
                    behavior: new BehaviorState(85, 30, 25, 50, 50, 35, null)));

            Assert.AreEqual(PortraitExpressionKind.Tired, spec.Expression.Kind);
        }

        [TestMethod]
        public void Build_HighStress_UsesTenseExpression()
        {
            var spec = _builder.Build(
                SexBiology.Female,
                BuildReferenceAppearance(),
                BuildSnapshot(
                    psychology: new PsychologyState(0.0, 0.55, 0.5, 75, 25, DiscreteEmotion.Fear)));

            Assert.AreEqual(PortraitExpressionKind.Tense, spec.Expression.Kind);
        }

        [TestMethod]
        public void Build_HighArousal_UsesAlertExpression()
        {
            var spec = _builder.Build(
                SexBiology.Female,
                BuildReferenceAppearance(),
                BuildSnapshot(
                    psychology: new PsychologyState(0.0, 0.8, 0.5, 35, 20, DiscreteEmotion.Surprise)));

            Assert.AreEqual(PortraitExpressionKind.Alert, spec.Expression.Kind);
        }

        [TestMethod]
        public void Format_ProducesGroundedPromptWithoutBeautificationLanguage()
        {
            var spec = _builder.Build(SexBiology.Female, BuildReferenceAppearance(), BuildSnapshot());
            var prompt = _formatter.Format(spec);

            StringAssert.Contains(prompt, "blue eyes");
            StringAssert.Contains(prompt, "dark blond hair");
            StringAssert.Contains(prompt, "straight");
            StringAssert.Contains(prompt, "fair skin");
            StringAssert.Contains(prompt, "neutral");
            StringAssert.Contains(prompt, "Do not smooth skin.");
            StringAssert.Contains(prompt, "No beautification");
            StringAssert.Contains(prompt, "no glamour styling");
        }

        private static PhysicalAppearance BuildReferenceAppearance()
            => new(
                HeightCm: 163.43522575145366,
                Frame: BodyFrame.Petite,
                SkinTone: SkinTone.Fair,
                EyeColor: EyeColor.Blue,
                HairColor: HairColorNatural.DarkBlond,
                HairType: HairType.Straight,
                FaceShape: FaceShape.Oval,
                ShoulderBreadthCm: 38.25361578154313,
                HipBreadthCm: 39.84460565452852,
                NoseProminence: 0.56,
                LipFullness: 0.56,
                DistinctiveMarks: new[] { "small scar above left eyebrow" });

        private static EnginesSnapshot BuildSnapshot(
            PhysiologyState? physiology = null,
            PsychologyState? psychology = null,
            BehaviorState? behavior = null)
            => new(
                physiology ?? new PhysiologyState(70, 2, 25, 20, 5, 10, 0, null),
                psychology ?? new PsychologyState(0.1, 0.45, 0.5, 35, 20, DiscreteEmotion.Neutral),
                behavior ?? new BehaviorState(40, 30, 25, 50, 50, 35, null),
                new InteractionSurface("Unknown", false, 0.5, 0.5, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(Array.Empty<EpisodicMemory>()),
                SemanticMemoryState.Empty);
    }
}
