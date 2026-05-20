// PortraitSpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Deterministic portrait specification derived from stable appearance data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All fields are resolved once at build time from stable character data
    /// (<see cref="PhysicalAppearance"/>, <see cref="Identity"/>, <see cref="EnginesSnapshot"/>)
    /// and are immutable thereafter. The specification is the single source of truth
    /// for both the portrait prompt formatter and any future rendering pipeline.
    /// </para>
    /// </remarks>
    public sealed record PortraitSpec(
        /// <summary>Biological sex of the character.</summary>
        SexBiology Biology,

        /// <summary>
        /// Natural-language age label understood by image generation models.
        /// Expressed as a decade range rather than an exact year —
        /// models respond better to "woman in her late 30s" than "woman aged 37".
        /// Examples: "young man in mid-to-late 20s", "elderly woman, 75 or older".
        /// </summary>
        string AgeLabel,

        /// <summary>
        /// Optional ancestry hint for image generation models.
        /// Short natural-language descriptor, e.g. "East Asian ancestry",
        /// "West African features", "Northern European ancestry".
        /// <c>null</c> means unspecified — portrait pipeline omits the hint entirely.
        /// </summary>
        string? AncestryHint,

        /// <summary>Body proportion and frame specification.</summary>
        BodyRenderSpec Body,

        /// <summary>Skin tone, lightness, undertone and texture policy.</summary>
        SkinRenderSpec Skin,

        /// <summary>Eye colour and iris variation specification.</summary>
        EyeRenderSpec Eyes,

        /// <summary>Hair colour, length, texture and volume policy.</summary>
        HairRenderSpec Hair,

        /// <summary>Face shape, feature projections and asymmetry policy.</summary>
        FaceRenderSpec Face,

        /// <summary>Facial expression derived from runtime psychology state.</summary>
        ExpressionRenderSpec Expression,

        /// <summary>Explicit guard rails that prevent beautification drift in downstream renderers.</summary>
        PortraitBiasGuard BiasGuard,

        /// <summary>Distinctive marks such as scars, birthmarks or tattoos.</summary>
        IReadOnlyList<string> DistinctiveMarks);

    // ── Sub-specs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Strict render specification for the eyes.
    /// </summary>
    public sealed record EyeRenderSpec(
        /// <summary>Dominant hue family, e.g. "brown", "hazel", "blue".</summary>
        string HueFamily,

        /// <summary>Iris colour variation range label, e.g. "low variation".</summary>
        string IrisVariationRange,

        /// <summary>Limbal ring intensity label, e.g. "soft", "medium".</summary>
        string LimbalRingIntensity,

        /// <summary>
        /// Negative constraint forwarded to the prompt, e.g. "do not enlarge eyes".
        /// Kept here so the formatter can emit it conditionally.
        /// </summary>
        string EyeScalePolicy);

    /// <summary>
    /// Strict render specification for the hair.
    /// </summary>
    public sealed record HairRenderSpec(
        /// <summary>Base colour family, e.g. "dark brown", "auburn".</summary>
        string BaseColorFamily,

        /// <summary>Brightness range label, e.g. "dark", "medium-light".</summary>
        string BrightnessRange,

        /// <summary>Strand texture label, e.g. "soft wave texture".</summary>
        string Texture,

        /// <summary>Curl/wave pattern label, e.g. "straight", "wavy", "coily".</summary>
        string Straightness,

        /// <summary>Length bucket, e.g. "short", "medium length", "long".</summary>
        string LengthBucket,

        /// <summary>Volume policy, e.g. "natural volume only".</summary>
        string VolumePolicy);

    /// <summary>
    /// Strict render specification for the skin.
    /// </summary>
    public sealed record SkinRenderSpec(
        /// <summary>Tone label, e.g. "olive", "very fair".</summary>
        string ToneLabel,

        /// <summary>Lightness bucket, e.g. "light-medium".</summary>
        string Lightness,

        /// <summary>Undertone label, e.g. "warm", "cool-neutral".</summary>
        string Undertone,

        /// <summary>Texture policy forwarded verbatim to the prompt.</summary>
        string TexturePolicy,

        /// <summary>Whether the renderer should preserve natural skin texture.</summary>
        bool PreserveNaturalTexture,

        /// <summary>Whether the renderer is allowed to smooth the skin.</summary>
        bool AllowSmoothing);

    /// <summary>
    /// Strict render specification for the face.
    /// </summary>
    public sealed record FaceRenderSpec(
        /// <summary>Face shape label, e.g. "oval", "square", "heart".</summary>
        string ShapeLabel,

        /// <summary>Width-to-height tendency label, e.g. "wider-than-tall tendency".</summary>
        string WidthHeightTendency,

        /// <summary>Nose projection bucket, e.g. "moderate projection".</summary>
        string NoseProjectionBucket,

        /// <summary>Lip fullness bucket, e.g. "medium-full".</summary>
        string LipFullnessBucket,

        /// <summary>Eye scale bucket, e.g. "medium eye scale".</summary>
        string EyeScaleBucket,

        /// <summary>Jaw definition bucket, e.g. "angular jaw definition".</summary>
        string JawDefinitionBucket,

        /// <summary>Facial asymmetry bucket, e.g. "subtle natural asymmetry".</summary>
        string FacialAsymmetryBucket,

        /// <summary>Symmetry policy forwarded verbatim to the prompt.</summary>
        string SymmetryPolicy);

    /// <summary>
    /// Strict render specification for the facial expression.
    /// </summary>
    public sealed record ExpressionRenderSpec(
        /// <summary>Expression category used for internal routing.</summary>
        PortraitExpressionKind Kind,

        /// <summary>Natural-language expression label, e.g. "calm", "tense".</summary>
        string ExpressionLabel,

        /// <summary>Mouth state label, e.g. "closed mouth".</summary>
        string MouthState,

        /// <summary>Brow tension label, e.g. "relaxed brows", "visible brow tension".</summary>
        string BrowTension,

        /// <summary>Whether a smile is permitted in the output.</summary>
        bool AllowSmile);

    /// <summary>
    /// Strict render specification for the body impression.
    /// </summary>
    /// <remarks>
    /// Raw centimetre values are stored for data fidelity but the formatter
    /// must use the bucket labels (<see cref="HeightBucket"/>, <see cref="ProportionBucket"/>,
    /// <see cref="FrameImpression"/>) rather than raw numbers — image models do not
    /// understand centimetre measurements.
    /// </remarks>
    public sealed record BodyRenderSpec(
        /// <summary>Raw height in centimetres — for data fidelity only.</summary>
        double HeightCm,

        /// <summary>Raw shoulder breadth in centimetres — for data fidelity only.</summary>
        double ShoulderBreadthCm,

        /// <summary>Raw hip breadth in centimetres — for data fidelity only.</summary>
        double HipBreadthCm,

        /// <summary>Raw waist-to-hip ratio — for data fidelity only.</summary>
        double WaistToHipRatio,

        /// <summary>Derived body frame category.</summary>
        BodyFrame Frame,

        /// <summary>Height bucket, e.g. "short", "medium height", "tall".</summary>
        string HeightBucket,

        /// <summary>Proportion bucket, e.g. "narrow proportions", "broad proportions".</summary>
        string ProportionBucket,

        /// <summary>Posture bucket, e.g. "neutral upright carriage".</summary>
        string PostureBucket,

        /// <summary>
        /// Frame impression for the prompt, e.g. "medium frame with balanced silhouette".
        /// This is the primary body descriptor used by the formatter.
        /// </summary>
        string FrameImpression);

    /// <summary>
    /// Explicit guard rails that prevent beautification drift in downstream renderers.
    /// </summary>
    /// <remarks>
    /// All flags default to <c>true</c> in <see cref="PortraitSpecBuilder"/> — the builder
    /// creates a maximally restrictive guard by default. Individual flags can be relaxed
    /// per-character if the simulation scenario warrants it (e.g. a glamour shot context).
    /// </remarks>
    public sealed record PortraitBiasGuard(
        /// <summary>When <c>true</c>, the renderer must not enhance facial symmetry.</summary>
        bool ForbidSymmetryEnhancement,

        /// <summary>When <c>true</c>, the renderer must not smooth skin texture.</summary>
        bool ForbidSkinSmoothing,

        /// <summary>When <c>true</c>, the renderer must not enlarge eyes.</summary>
        bool ForbidEyeEnlargement,

        /// <summary>When <c>true</c>, the renderer must not enhance lip volume or colour.</summary>
        bool ForbidLipEnhancement,

        /// <summary>When <c>true</c>, the renderer must not apply any aesthetic reinterpretation.</summary>
        bool ForbidAestheticReinterpretation,

        /// <summary>When <c>true</c>, the renderer must not force a smile on the character.</summary>
        bool ForbidForcedSmile);

    /// <summary>
    /// Conservative portrait expression categories.
    /// Maps to natural-language labels in <see cref="ExpressionRenderSpec"/>.
    /// </summary>
    public enum PortraitExpressionKind
    {
        /// <summary>Resting neutral state — no visible emotional signal.</summary>
        Neutral,

        /// <summary>Low arousal, low stress — soft and settled.</summary>
        Calm,

        /// <summary>High arousal — eyes open, brows slightly raised.</summary>
        Alert,

        /// <summary>High sleep debt or exhaustion — heavy eyelids, slack jaw.</summary>
        Tired,

        /// <summary>High stress — brow tension, tight jaw.</summary>
        Tense
    }
}
