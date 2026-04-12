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
        PostureMorphology Posture);

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
        double ForearmVolume);

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
        double BalanceCenter);

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
        AsymmetryMorphology Asymmetry);

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
        double HairlineCurvature);

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
        double EyelashProminence);

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
        double NasolabialAngle);

    /// <summary>Mouth, lip, philtrum, and corner morphology.</summary>
    public sealed record MouthMorphology(
        double MouthWidth,
        double PhiltrumLength,
        double UpperLipFullness,
        double LowerLipFullness,
        double LipContourSharpness,
        double CupidBowProminence,
        double MouthCornerTilt,
        double VermilionHeight);

    /// <summary>Cheek and facial soft tissue morphology.</summary>
    public sealed record CheekSoftTissueMorphology(
        double CheekFullness,
        double BuccalFullness,
        double MalarProminence,
        double FacialSoftness,
        double FacialAngularity,
        double SoftTissueFullness);

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
        double EarAttachmentHeight);

    /// <summary>Subtle left-right variation amplitudes.</summary>
    public sealed record AsymmetryMorphology(
        double FacialAsymmetry,
        double LeftRightVariationAmplitude,
        double BodyAsymmetryAmplitude);

    /// <summary>Moderate-resolution skin and surface-detail traits.</summary>
    public sealed record SurfaceTraits(
        double SkinSmoothness,
        double SkinThickness,
        double FreckleDensity,
        int MoleCount,
        double DistinctiveMarkProbability,
        double ScarProbability,
        double WrinkleTendency,
        double AgeSurfaceFactor);

    /// <summary>Stable colour and hair-texture traits.</summary>
    public sealed record ColorTraits(
        SkinTone SkinTone,
        EyeColor EyeColor,
        HairColorNatural HairColor,
        HairType HairType);
}
