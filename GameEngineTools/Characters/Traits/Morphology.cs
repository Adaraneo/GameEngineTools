// Morphology.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Anatomically structured whole-body morphology.
    /// </summary>
    public sealed record BodyMorphology(
        BodyProportions Proportions,
        SkeletalMorphology Skeletal,
        SoftTissueMorphology SoftTissue,
        RegionalSilhouetteMorphology Silhouette,
        PostureMorphology Posture)
    {
        /// <summary>
        /// Builds a conservative morphology projection for legacy appearance records.
        /// </summary>
        public static BodyMorphology FromLegacy(double heightCm, double shoulderBreadthCm, double hipBreadthCm, BodyFrame frame)
        {
            var sittingHeight = heightCm * 0.52;
            var legLength = heightCm - sittingHeight;
            var torsoLength = sittingHeight;
            var robustness = frame switch
            {
                BodyFrame.Petite => 0.35,
                BodyFrame.Large => 0.62,
                BodyFrame.Strong => 0.72,
                _ => 0.50
            };

            var waistWidth = (shoulderBreadthCm + hipBreadthCm) * 0.42;

            return new BodyMorphology(
                new BodyProportions(
                    HeightCm: heightCm,
                    SittingHeight: sittingHeight,
                    LegLength: legLength,
                    TorsoLength: torsoLength,
                    ArmLength: heightCm * 0.44,
                    ForearmLength: heightCm * 0.16,
                    UpperArmLength: heightCm * 0.18,
                    NeckLength: heightCm * 0.055,
                    ShoulderToHipRatio: SafeRatio(shoulderBreadthCm, hipBreadthCm),
                    WaistToHipRatio: SafeRatio(waistWidth, hipBreadthCm),
                    ChestToWaistRatio: 1.12,
                    LegToTorsoRatio: SafeRatio(legLength, torsoLength)),
                new SkeletalMorphology(
                    SkeletalRobustness: robustness,
                    ClavicleBreadth: shoulderBreadthCm * 0.78,
                    ShoulderBreadth: shoulderBreadthCm,
                    RibcageWidth: shoulderBreadthCm * 0.72,
                    RibcageDepth: heightCm * 0.12,
                    ChestBreadth: shoulderBreadthCm * 0.78,
                    PelvicBreadth: hipBreadthCm * 0.82,
                    WaistBaseWidth: waistWidth,
                    NeckThickness: heightCm * 0.065,
                    HandSize: heightCm * 0.105,
                    FootSize: heightCm * 0.145),
                SoftTissueMorphology.Neutral,
                new RegionalSilhouetteMorphology(
                    WaistWidth: waistWidth,
                    HipWidth: hipBreadthCm,
                    ButtockProjection: 0.5,
                    ThighInnerFullness: 0.5,
                    CalfDefinition: 0.5,
                    ShoulderSlope: 0.5,
                    ChestProjection: 0.5,
                    AbdomenProjection: 0.35,
                    BustOrChestSoftTissueVolume: 0.45,
                    PelvicFlare: 0.5,
                    WaistTaper: 0.5),
                PostureMorphology.Neutral);
        }

        private static double SafeRatio(double a, double b)
            => b <= 0 ? 0 : Math.Round(a / b, 3);
    }

    /// <summary>Global body proportions in centimetres and dimensionless ratios.</summary>
    public sealed record BodyProportions(
        double HeightCm,
        double SittingHeight,
        double LegLength,
        double TorsoLength,
        double ArmLength,
        double ForearmLength,
        double UpperArmLength,
        double NeckLength,
        double ShoulderToHipRatio,
        double WaistToHipRatio,
        double ChestToWaistRatio,
        double LegToTorsoRatio);

    /// <summary>Skeletal breadth, robustness, and distal-size morphology.</summary>
    public sealed record SkeletalMorphology(
        double SkeletalRobustness,
        double ClavicleBreadth,
        double ShoulderBreadth,
        double RibcageWidth,
        double RibcageDepth,
        double ChestBreadth,
        double PelvicBreadth,
        double WaistBaseWidth,
        double NeckThickness,
        double HandSize,
        double FootSize);

    /// <summary>Soft-tissue composition and regional volume distribution.</summary>
    public sealed record SoftTissueMorphology(
        double Muscularity,
        double Adiposity,
        double TissueFirmness,
        double SoftTissueFullness,
        double FatDistributionUpper,
        double FatDistributionMid,
        double FatDistributionLower,
        double AbdominalVolume,
        double GlutealVolume,
        double ThighVolume,
        double CalfVolume,
        double UpperArmVolume,
        double ForearmVolume)
    {
        /// <summary>Neutral soft-tissue projection for legacy records.</summary>
        public static SoftTissueMorphology Neutral => new(
            Muscularity: 0.45,
            Adiposity: 0.45,
            TissueFirmness: 0.65,
            SoftTissueFullness: 0.50,
            FatDistributionUpper: 0.45,
            FatDistributionMid: 0.45,
            FatDistributionLower: 0.45,
            AbdominalVolume: 0.35,
            GlutealVolume: 0.45,
            ThighVolume: 0.45,
            CalfVolume: 0.45,
            UpperArmVolume: 0.45,
            ForearmVolume: 0.45);
    }

    /// <summary>Regional silhouette and sex-linked soft regional tendencies.</summary>
    public sealed record RegionalSilhouetteMorphology(
        double WaistWidth,
        double HipWidth,
        double ButtockProjection,
        double ThighInnerFullness,
        double CalfDefinition,
        double ShoulderSlope,
        double ChestProjection,
        double AbdomenProjection,
        double BustOrChestSoftTissueVolume,
        double PelvicFlare,
        double WaistTaper);

    /// <summary>Postural carriage and balance morphology.</summary>
    public sealed record PostureMorphology(
        double PostureUprightness,
        double ShoulderRoll,
        double PelvicTilt,
        double SpineCurvatureProxy,
        double HeadForwardness,
        double BalanceCenter)
    {
        /// <summary>Neutral postural projection for legacy records.</summary>
        public static PostureMorphology Neutral => new(
            PostureUprightness: 0.75,
            ShoulderRoll: 0.35,
            PelvicTilt: 0.45,
            SpineCurvatureProxy: 0.40,
            HeadForwardness: 0.30,
            BalanceCenter: 0.50);
    }

    /// <summary>
    /// Anatomically structured facial morphology.
    /// </summary>
    public sealed record FacialMorphology(
        CraniofacialStructure Craniofacial,
        ForeheadBrowMorphology ForeheadBrow,
        EyeRegionMorphology EyeRegion,
        NoseMorphology Nose,
        MouthMorphology Mouth,
        CheekSoftTissueMorphology Cheeks,
        JawMorphology Jaw,
        EarMorphology Ears,
        AsymmetryMorphology Asymmetry)
    {
        /// <summary>
        /// Builds a conservative morphology projection for legacy appearance records.
        /// </summary>
        public static FacialMorphology FromLegacy(FaceShape faceShape, double noseProminence, double lipFullness)
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
            var jawWidth = faceShape == FaceShape.Square ? faceWidth * 0.92 : faceWidth * 0.78;

            return new FacialMorphology(
                new CraniofacialStructure(
                    HeadWidth: faceWidth * 1.05,
                    HeadHeight: 23.0,
                    HeadDepth: 18.0,
                    FaceWidth: faceWidth,
                    FaceHeight: faceHeight,
                    FaceWidthToHeightRatio: faceRatio,
                    UpperFaceHeight: faceHeight * 0.32,
                    MidFaceHeight: faceHeight * 0.34,
                    LowerFaceHeight: faceHeight * 0.34,
                    ForeheadHeight: faceHeight * 0.27,
                    TempleWidth: faceWidth * 0.88,
                    CheekboneWidth: faceWidth * 0.98,
                    BizygomaticWidth: faceWidth,
                    JawWidth: jawWidth,
                    MandibleAngle: 118,
                    ChinWidth: jawWidth * 0.55,
                    ChinHeight: faceHeight * 0.14,
                    ChinProjection: 0.45,
                    LowerFaceProjection: 0.45),
                ForeheadBrowMorphology.Neutral,
                EyeRegionMorphology.Neutral(faceWidth),
                NoseMorphology.FromLegacy(noseProminence, faceWidth),
                MouthMorphology.FromLegacy(lipFullness, faceWidth),
                CheekSoftTissueMorphology.Neutral,
                new JawMorphology(0.45, 0.50, jawWidth, 0.45),
                EarMorphology.Neutral,
                AsymmetryMorphology.Neutral);
        }
    }

    /// <summary>Global craniofacial structure in centimetres and derived ratios.</summary>
    public sealed record CraniofacialStructure(
        double HeadWidth,
        double HeadHeight,
        double HeadDepth,
        double FaceWidth,
        double FaceHeight,
        double FaceWidthToHeightRatio,
        double UpperFaceHeight,
        double MidFaceHeight,
        double LowerFaceHeight,
        double ForeheadHeight,
        double TempleWidth,
        double CheekboneWidth,
        double BizygomaticWidth,
        double JawWidth,
        double MandibleAngle,
        double ChinWidth,
        double ChinHeight,
        double ChinProjection,
        double LowerFaceProjection);

    /// <summary>Forehead, brow ridge, hairline, and brow morphology.</summary>
    public sealed record ForeheadBrowMorphology(
        double BrowRidgeProminence,
        double BrowHeight,
        double BrowThickness,
        double BrowAngle,
        double BrowArch,
        double ForeheadSlope,
        double HairlineHeight,
        double HairlineCurvature)
    {
        /// <summary>Neutral forehead and brow projection for legacy records.</summary>
        public static ForeheadBrowMorphology Neutral => new(0.40, 0.55, 0.45, 0.50, 0.50, 0.40, 0.45, 0.50);
    }

    /// <summary>Eye region morphology including spacing, lids, tilt, and iris scale.</summary>
    public sealed record EyeRegionMorphology(
        double EyeSize,
        double EyeWidth,
        double EyeHeight,
        double EyeSpacing,
        double EyeDepth,
        double EyeTilt,
        double CanthalTilt,
        double UpperLidExposure,
        double LowerLidFullness,
        double IrisSize,
        double EyelashProminence)
    {
        /// <summary>Neutral eye projection for legacy records.</summary>
        public static EyeRegionMorphology Neutral(double faceWidth)
            => new(0.55, faceWidth * 0.18, faceWidth * 0.065, faceWidth * 0.22, 0.45, 0.50, 0.50, 0.55, 0.45, 0.52, 0.45);
    }

    /// <summary>Nasal bridge, base, nostril, tip, and projection morphology.</summary>
    public sealed record NoseMorphology(
        double NoseLength,
        double NoseWidth,
        double NoseBridgeHeight,
        double NoseBridgeWidth,
        double NoseProjection,
        double NoseTipProjection,
        double NoseTipRoundness,
        double NoseBaseWidth,
        double NostrilWidth,
        double NostrilFlare,
        double NasolabialAngle)
    {
        /// <summary>Projects legacy nose prominence into nasal morphology.</summary>
        public static NoseMorphology FromLegacy(double noseProminence, double faceWidth)
            => new(0.45 + noseProminence * 0.35, faceWidth * 0.22, noseProminence, faceWidth * 0.09, noseProminence, noseProminence, 0.50, faceWidth * 0.22, faceWidth * 0.08, 0.45, 96 + noseProminence * 12);
    }

    /// <summary>Mouth, lip, philtrum, and corner morphology.</summary>
    public sealed record MouthMorphology(
        double MouthWidth,
        double PhiltrumLength,
        double UpperLipFullness,
        double LowerLipFullness,
        double LipContourSharpness,
        double CupidBowProminence,
        double MouthCornerTilt,
        double VermilionHeight)
    {
        /// <summary>Projects legacy lip fullness into mouth morphology.</summary>
        public static MouthMorphology FromLegacy(double lipFullness, double faceWidth)
            => new(faceWidth * 0.42, 0.45, lipFullness * 0.9, lipFullness, 0.50, 0.50, 0.50, 0.20 + lipFullness * 0.45);
    }

    /// <summary>Cheek and facial soft tissue morphology.</summary>
    public sealed record CheekSoftTissueMorphology(
        double CheekFullness,
        double BuccalFullness,
        double MalarProminence,
        double FacialSoftness,
        double FacialAngularity,
        double SoftTissueFullness)
    {
        /// <summary>Neutral cheek and facial soft tissue projection for legacy records.</summary>
        public static CheekSoftTissueMorphology Neutral => new(0.50, 0.50, 0.50, 0.50, 0.50, 0.50);
    }

    /// <summary>Jaw, gonial, chin, and lower-face mass morphology.</summary>
    public sealed record JawMorphology(
        double JawProminence,
        double JawRoundness,
        double GonialWidth,
        double LowerFaceMass);

    /// <summary>Ear size, projection, lobe, and attachment morphology.</summary>
    public sealed record EarMorphology(
        double EarSize,
        double EarProjection,
        double EarlobeSize,
        double EarAttachmentHeight)
    {
        /// <summary>Neutral ear projection for legacy records.</summary>
        public static EarMorphology Neutral => new(0.50, 0.45, 0.45, 0.50);
    }

    /// <summary>Subtle left-right variation amplitudes.</summary>
    public sealed record AsymmetryMorphology(
        double FacialAsymmetry,
        double LeftRightVariationAmplitude,
        double BodyAsymmetryAmplitude)
    {
        /// <summary>Neutral asymmetry projection for legacy records.</summary>
        public static AsymmetryMorphology Neutral => new(0.05, 0.04, 0.03);
    }

    /// <summary>Moderate-resolution skin and surface-detail traits.</summary>
    public sealed record SurfaceTraits(
        double SkinSmoothness,
        double SkinThickness,
        double FreckleDensity,
        int MoleCount,
        double DistinctiveMarkProbability,
        double ScarProbability,
        double WrinkleTendency,
        double AgeSurfaceFactor)
    {
        /// <summary>Neutral surface projection for legacy records.</summary>
        public static SurfaceTraits Neutral => new(0.70, 0.50, 0.10, 2, 0.05, 0.05, 0.10, 0.20);
    }

    /// <summary>Stable colour and hair-texture traits.</summary>
    public sealed record ColorTraits(
        SkinTone SkinTone,
        EyeColor EyeColor,
        HairColorNatural HairColor,
        HairType HairType);
}
