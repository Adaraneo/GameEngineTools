// PortraitSpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Deterministic portrait specification derived from stable appearance data.
    /// </summary>
    public sealed record PortraitSpec(
        SexBiology Biology,
        BodyRenderSpec Body,
        SkinRenderSpec Skin,
        EyeRenderSpec Eyes,
        HairRenderSpec Hair,
        FaceRenderSpec Face,
        ExpressionRenderSpec Expression,
        PortraitBiasGuard BiasGuard,
        IReadOnlyList<string> DistinctiveMarks);

    /// <summary>
    /// Strict render specification for the eyes.
    /// </summary>
    public sealed record EyeRenderSpec(
        string HueFamily,
        string IrisVariationRange,
        string LimbalRingIntensity,
        string EyeScalePolicy);

    /// <summary>
    /// Strict render specification for the hair.
    /// </summary>
    public sealed record HairRenderSpec(
        string BaseColorFamily,
        string BrightnessRange,
        string Texture,
        string Straightness,
        string LengthBucket,
        string VolumePolicy);

    /// <summary>
    /// Strict render specification for the skin.
    /// </summary>
    public sealed record SkinRenderSpec(
        string ToneLabel,
        string Lightness,
        string Undertone,
        string TexturePolicy,
        bool PreserveNaturalTexture,
        bool AllowSmoothing);

    /// <summary>
    /// Strict render specification for the face.
    /// </summary>
    public sealed record FaceRenderSpec(
        string ShapeLabel,
        string WidthHeightTendency,
        string NoseProjectionBucket,
        string LipFullnessBucket,
        string EyeScaleBucket,
        string JawDefinitionBucket,
        string FacialAsymmetryBucket,
        string SymmetryPolicy);

    /// <summary>
    /// Strict render specification for the expression.
    /// </summary>
    public sealed record ExpressionRenderSpec(
        PortraitExpressionKind Kind,
        string ExpressionLabel,
        string MouthState,
        string BrowTension,
        bool AllowSmile);

    /// <summary>
    /// Strict render specification for the body impression.
    /// </summary>
    public sealed record BodyRenderSpec(
        double HeightCm,
        double ShoulderBreadthCm,
        double HipBreadthCm,
        double WaistToHipRatio,
        BodyFrame Frame,
        string HeightBucket,
        string ProportionBucket,
        string PostureBucket,
        string FrameImpression);

    /// <summary>
    /// Explicit guard rails that prevent beautification drift in downstream renderers.
    /// </summary>
    public sealed record PortraitBiasGuard(
        bool ForbidSymmetryEnhancement,
        bool ForbidSkinSmoothing,
        bool ForbidEyeEnlargement,
        bool ForbidLipEnhancement,
        bool ForbidAestheticReinterpretation,
        bool ForbidForcedSmile);

    /// <summary>
    /// Conservative portrait expression categories.
    /// </summary>
    public enum PortraitExpressionKind
    {
        Neutral,
        Calm,
        Alert,
        Tired,
        Tense
    }
}
