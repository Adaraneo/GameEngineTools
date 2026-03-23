// AppearanceGenSpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    /// <summary>
    /// Immutable configuration record for <see cref="AppearanceGenerator"/>.
    /// </summary>
    /// <remarks>
    /// All values are culturally agnostic — numbers represent neutral anatomical ranges
    /// that can be overridden per population, region, or game setting via
    /// a custom <see cref="AppearanceGenSpec"/> instance.
    /// </remarks>
    public sealed record AppearanceGenSpec(
        // ── Height ranges (cm) ──────────────────────────────────────────────

        /// <summary>Height sampling range for female characters (cm).</summary>
        (double Min, double Max) HeightFemale,

        /// <summary>Height sampling range for male characters (cm).</summary>
        (double Min, double Max) HeightMale,

        // ── Category weights ─────────────────────────────────────────────────

        /// <summary>
        /// Sampling weights for <c>BodyFrame</c> values
        /// in the order: Petite, Medium, Large, Strong.
        /// </summary>
        double[] BodyFrameWeights,

        /// <summary>
        /// Sampling weights for <c>SkinTone</c> values
        /// in the order: Fair, Light, LightMedium, Medium, Tan.
        /// </summary>
        double[] SkinToneWeights,

        /// <summary>
        /// Sampling weights for <c>EyeColor</c> values
        /// in the order: Brown, Hazel, Green, Blue, Gray.
        /// </summary>
        double[] EyeColorWeights,

        /// <summary>
        /// Sampling weights for <c>HairColorNatural</c> values
        /// in the order: Black, DarkBrown, Brown, DarkBlond, Blond.
        /// </summary>
        double[] HairColorWeights,

        /// <summary>
        /// Sampling weights for <c>HairType</c> values
        /// in the order: Straight, Wavy, Curly.
        /// </summary>
        double[] HairTypeWeights,

        /// <summary>
        /// Sampling weights for <c>FaceShape</c> values
        /// in the order: Oval, Round, Heart, Square, Oblong.
        /// </summary>
        double[] FaceShapeWeights,

        // ── Correlation strengths (0..1) ─────────────────────────────────────

        /// <summary>
        /// Strength of the height → shoulder breadth correlation (0..1).
        /// Higher values make taller characters proportionally broader across the shoulders.
        /// </summary>
        double HeightToShoulderCorr,

        /// <summary>
        /// Strength of the height → hip breadth correlation (0..1).
        /// Higher values make taller characters proportionally broader at the hips.
        /// </summary>
        double HeightToHipCorr,

        /// <summary>
        /// Strength of the body frame → breadths correlation (0..1).
        /// Higher values make larger/stronger frames produce wider shoulder and hip measurements.
        /// </summary>
        double FrameToBreadthsCorr,

        // ── Shoulder breadth baselines (cm) — sex-specific ───────────────────

        /// <summary>
        /// Base shoulder breadth range for female characters (cm), before correlation corrections.
        /// Females have narrower shoulders on average than males.
        /// </summary>
        (double Min, double Max) ShoulderBreadthBaseFemale,

        /// <summary>
        /// Base shoulder breadth range for male characters (cm), before correlation corrections.
        /// Males have broader shoulders on average than females.
        /// </summary>
        (double Min, double Max) ShoulderBreadthBaseMale,

        // ── Hip breadth baselines (cm) ────────────────────────────────────────

        /// <summary>Base hip breadth range for female characters (cm), before correlation corrections.</summary>
        (double Min, double Max) HipBreadthBaseFemale,

        /// <summary>Base hip breadth range for male characters (cm), before correlation corrections.</summary>
        (double Min, double Max) HipBreadthBaseMale,

        // ── Facial feature distributions (0..1) ──────────────────────────────

        /// <summary>
        /// Mean and standard deviation for nose prominence.
        /// 0 = flat, 1 = very prominent.
        /// </summary>
        (double Mean, double Dev) NoseProminence,

        /// <summary>
        /// Mean and standard deviation for lip fullness.
        /// 0 = thin, 1 = very full.
        /// </summary>
        (double Mean, double Dev) LipFullness,

        // ── Sex-ratio silhouette enforcement ─────────────────────────────────

        /// <summary>
        /// Controls how aggressively the post-generation step enforces sexually dimorphic silhouettes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// After shoulder and hip breadths are generated independently, this nudge step
        /// ensures the correct sex-typical silhouette:
        /// <list type="bullet">
        ///   <item><term>Female</term><description>hip ≥ shoulder (pear / hourglass tendency).</description></item>
        ///   <item><term>Male</term><description>shoulder ≥ hip (inverted triangle tendency).</description></item>
        /// </list>
        /// </para>
        /// <para>
        /// A value of <c>0.6</c> moves 60 % of the excess from the "wrong" dimension
        /// and redistributes 40 % to the "correct" one — preserving random spread
        /// while guaranteeing dimorphism. Range: 0..1.
        /// </para>
        /// </remarks>
        double SexRatioBias
    )
    {
        /// <summary>
        /// Returns a sensible, culturally neutral default spec
        /// suitable for most fantasy or RPG populations.
        /// </summary>
        public static AppearanceGenSpec Default => new(
            HeightFemale:              (155, 175),
            HeightMale:                (165, 185),
            BodyFrameWeights:          Uniform(4),
            SkinToneWeights:           Uniform(5),
            EyeColorWeights:           Uniform(5),
            HairColorWeights:          Uniform(5),
            HairTypeWeights:           new[] { 0.42, 0.40, 0.18 },
            FaceShapeWeights:          Uniform(5),
            HeightToShoulderCorr:      0.55,
            HeightToHipCorr:           0.45,
            FrameToBreadthsCorr:       0.35,
            ShoulderBreadthBaseFemale: (32, 42),
            ShoulderBreadthBaseMale:   (38, 48),
            HipBreadthBaseFemale:      (36, 44),
            HipBreadthBaseMale:        (34, 42),
            NoseProminence:            (0.50, 0.20),
            LipFullness:               (0.50, 0.20),
            SexRatioBias:              0.60
        );

        /// <summary>
        /// Creates a uniform weight array of length <paramref name="n"/>
        /// where each element equals <c>1 / n</c>.
        /// </summary>
        /// <param name="n">Number of categories to distribute weight across.</param>
        /// <returns>A new <c>double[]</c> of length <paramref name="n"/> summing to 1.</returns>
        public static double[] Uniform(int n)
        {
            var a = new double[n];

            for (var i = 0; i < n; i++)
            {
                a[i] = 1.0 / n;
            }

            return a;
        }
    }
}
