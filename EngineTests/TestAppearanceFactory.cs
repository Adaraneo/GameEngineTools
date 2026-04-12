// TestAppearanceFactory.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    internal static class TestAppearanceFactory
    {
        public static PhysicalAppearance Build(
            double heightCm = 170.0,
            BodyFrame frame = BodyFrame.Medium,
            SkinTone skinTone = SkinTone.Medium,
            EyeColor eyeColor = EyeColor.Brown,
            HairColorNatural hairColor = HairColorNatural.Brown,
            HairType hairType = HairType.Straight,
            FaceShape faceShape = FaceShape.Oval,
            double shoulderBreadthCm = 40.0,
            double hipBreadthCm = 38.0,
            double noseProjection = 0.5,
            double lipFullness = 0.5,
            IReadOnlyList<string>? distinctiveMarks = null,
            double hairLengthCm = 35.0)
        {
            var body = BuildBody(heightCm, frame, shoulderBreadthCm, hipBreadthCm);
            var face = BuildFace(faceShape, noseProjection, lipFullness);
            var surface = new SurfaceTraits(0.70, 0.50, 0.10, 2, 0.05, 0.05, 0.10, 0.20);
            var colors = new ColorTraits(skinTone, eyeColor, hairColor, hairType);

            return new PhysicalAppearance(body, face, surface, colors, distinctiveMarks, hairLengthCm);
        }

        private static BodyMorphology BuildBody(double heightCm, BodyFrame frame, double shoulderBreadthCm, double hipBreadthCm)
        {
            var sittingHeight = heightCm * 0.52;
            var legLength = heightCm - sittingHeight;
            var waistWidth = (shoulderBreadthCm + hipBreadthCm) * 0.42;
            var robustness = frame switch
            {
                BodyFrame.Petite => 0.35,
                BodyFrame.Large => 0.62,
                BodyFrame.Strong => 0.72,
                _ => 0.50
            };
            var muscularity = frame == BodyFrame.Strong ? 0.72 : 0.45;
            var adiposity = frame == BodyFrame.Large ? 0.62 : 0.45;

            return new BodyMorphology(
                new BodyProportions(
                    heightCm,
                    sittingHeight,
                    legLength,
                    sittingHeight,
                    heightCm * 0.44,
                    heightCm * 0.16,
                    heightCm * 0.18,
                    heightCm * 0.055,
                    SafeRatio(shoulderBreadthCm, hipBreadthCm),
                    SafeRatio(waistWidth, hipBreadthCm),
                    1.12,
                    SafeRatio(legLength, sittingHeight)),
                new SkeletalMorphology(
                    robustness,
                    shoulderBreadthCm * 0.78,
                    shoulderBreadthCm,
                    shoulderBreadthCm * 0.72,
                    heightCm * 0.12,
                    shoulderBreadthCm * 0.78,
                    hipBreadthCm * 0.82,
                    waistWidth,
                    heightCm * 0.065,
                    heightCm * 0.105,
                    heightCm * 0.145),
                new SoftTissueMorphology(
                    muscularity,
                    adiposity,
                    0.65,
                    0.50,
                    0.45,
                    0.45,
                    0.45,
                    0.35,
                    0.45,
                    0.45,
                    0.45,
                    0.45,
                    0.45),
                new RegionalSilhouetteMorphology(
                    waistWidth,
                    hipBreadthCm,
                    0.50,
                    0.50,
                    0.50,
                    0.50,
                    0.50,
                    0.35,
                    0.45,
                    0.50,
                    0.50),
                new PostureMorphology(0.75, 0.35, 0.45, 0.40, 0.30, 0.50));
        }

        private static FacialMorphology BuildFace(FaceShape faceShape, double noseProjection, double lipFullness)
        {
            var faceRatio = faceShape switch
            {
                FaceShape.Round => 0.92,
                FaceShape.Square => 0.88,
                FaceShape.Oblong => 0.72,
                FaceShape.Heart => 0.82,
                FaceShape.Diamond => 0.78,
                _ => 0.78
            };

            var faceHeight = 20.0;
            var faceWidth = faceHeight * faceRatio;
            var jawWidth = faceShape switch
            {
                FaceShape.Square => faceWidth * 0.92,
                FaceShape.Diamond => faceWidth * 0.74,
                FaceShape.Heart => faceWidth * 0.66,
                _ => faceWidth * 0.82
            };

            return new FacialMorphology(
                new CraniofacialStructure(
                    faceWidth * 1.05,
                    23.0,
                    18.0,
                    faceWidth,
                    faceHeight,
                    faceRatio,
                    faceHeight * 0.32,
                    faceHeight * 0.34,
                    faceHeight * 0.34,
                    faceHeight * 0.27,
                    faceWidth * 0.88,
                    faceWidth * 0.98,
                    faceWidth,
                    jawWidth,
                    118,
                    jawWidth * 0.55,
                    faceHeight * 0.14,
                    0.45,
                    0.45),
                new ForeheadBrowMorphology(0.40, 0.55, 0.45, 0.50, 0.50, 0.40, 0.45, 0.50),
                new EyeRegionMorphology(0.55, faceWidth * 0.18, faceWidth * 0.065, faceWidth * 0.22, 0.45, 0.50, 0.50, 0.55, 0.45, 0.52, 0.45),
                new NoseMorphology(0.45 + noseProjection * 0.35, faceWidth * 0.22, noseProjection, faceWidth * 0.09, noseProjection, noseProjection, 0.50, faceWidth * 0.22, faceWidth * 0.08, 0.45, 96 + noseProjection * 12),
                new MouthMorphology(faceWidth * 0.42, 0.45, lipFullness * 0.9, lipFullness, 0.50, 0.50, 0.50, 0.20 + lipFullness * 0.45),
                new CheekSoftTissueMorphology(0.50, 0.50, 0.50, 0.50, 0.50, 0.50),
                new JawMorphology(0.45, 0.50, jawWidth, 0.45),
                new EarMorphology(0.50, 0.45, 0.45, 0.50),
                new AsymmetryMorphology(0.05, 0.04, 0.03));
        }

        private static double SafeRatio(double a, double b)
            => b <= 0 ? 0 : Math.Round(a / b, 3);
    }
}
