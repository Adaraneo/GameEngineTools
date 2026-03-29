// AppearanceGenSpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;

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

        #region Factories

        /// <summary>
        /// Returns a spec calibrated for the given life stage and biological sex.
        /// </summary>
        /// <param name="stadium">Character's life stage.</param>
        /// <param name="sex">Biological sex — affects height and proportion baselines.</param>
        public static AppearanceGenSpec ForStadium(StadiumType stadium, SexBiology sex)
            => stadium switch
            {
                StadiumType.Baby => Baby(),
                StadiumType.Child => Child(sex),
                StadiumType.Teenager => Teenager(sex),
                StadiumType.MidAged => MidAged(sex),
                StadiumType.Old => Old(sex),
                _ => Default  // Adult
            };

        // ── Private stadium factories ────────────────────────────────────────────────

        /// <summary>Baby: 0–2 years. Very small, round proportions, no sexual dimorphism yet.</summary>
        private static AppearanceGenSpec Baby() => new(
            HeightFemale: (45, 90),    // cm — newborn to ~2yr
            HeightMale: (45, 92),
            BodyFrameWeights: new[] { 0.5, 0.4, 0.1, 0.0 }, // mostly Petite/Medium
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.5, 0.35, 0.15 },
            FaceShapeWeights: new[] { 0.1, 0.7, 0.1, 0.05, 0.05 }, // mostly Round
            HeightToShoulderCorr: 0.3,
            HeightToHipCorr: 0.3,
            FrameToBreadthsCorr: 0.2,
            ShoulderBreadthBaseFemale: (14, 18),
            ShoulderBreadthBaseMale: (14, 19),
            HipBreadthBaseFemale: (14, 18),
            HipBreadthBaseMale: (14, 18),
            NoseProminence: (0.20, 0.08),  // small, subtle
            LipFullness: (0.55, 0.10),  // babies have full lips
            SexRatioBias: 0.1            // almost no dimorphism
        );

        /// <summary>Child: 3–11 years. Proportionally larger head, slim limbs, minimal dimorphism.</summary>
        private static AppearanceGenSpec Child(SexBiology sex)
        {
            bool f = sex == SexBiology.Female;
            return new(
                HeightFemale: (90, 148),
                HeightMale: (90, 150),
                BodyFrameWeights: new[] { 0.35, 0.45, 0.15, 0.05 },
                SkinToneWeights: Uniform(5),
                EyeColorWeights: Uniform(5),
                HairColorWeights: Uniform(5),
                HairTypeWeights: new[] { 0.42, 0.40, 0.18 },
                FaceShapeWeights: new[] { 0.2, 0.4, 0.2, 0.1, 0.1 }, // still Round-biased
                HeightToShoulderCorr: 0.4,
                HeightToHipCorr: 0.4,
                FrameToBreadthsCorr: 0.25,
                ShoulderBreadthBaseFemale: (22, 32),
                ShoulderBreadthBaseMale: (22, 33),
                HipBreadthBaseFemale: (22, 31),
                HipBreadthBaseMale: (22, 31),
                NoseProminence: (0.35, 0.12),
                LipFullness: (0.50, 0.12),
                SexRatioBias: 0.2           // still minimal dimorphism
            );
        }

        /// <summary>Teenager: 12–17 years. Puberty proportions, growing dimorphism.</summary>
        private static AppearanceGenSpec Teenager(SexBiology sex)
        {
            bool f = sex == SexBiology.Female;
            return new(
                HeightFemale: (148, 168),
                HeightMale: (150, 178),
                BodyFrameWeights: new[] { 0.3, 0.45, 0.2, 0.05 },
                SkinToneWeights: Uniform(5),
                EyeColorWeights: Uniform(5),
                HairColorWeights: Uniform(5),
                HairTypeWeights: new[] { 0.42, 0.40, 0.18 },
                FaceShapeWeights: Uniform(5),
                HeightToShoulderCorr: 0.50,
                HeightToHipCorr: 0.45,
                FrameToBreadthsCorr: 0.30,
                ShoulderBreadthBaseFemale: (30, 40),
                ShoulderBreadthBaseMale: (34, 44),
                HipBreadthBaseFemale: (32, 41),
                HipBreadthBaseMale: (30, 38),
                NoseProminence: (0.45, 0.16),
                LipFullness: (0.50, 0.15),
                SexRatioBias: 0.45
            );
        }

        /// <summary>
        /// MidAged: 40–64 years.
        /// Slight broadening of frame, facial features become more pronounced.
        /// </summary>
        private static AppearanceGenSpec MidAged(SexBiology sex) => new(
            HeightFemale: (154, 172),   // slight shrinkage from Adult
            HeightMale: (162, 182),
            BodyFrameWeights: new[] { 0.10, 0.35, 0.35, 0.20 }, // more Large/Strong
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.42, 0.40, 0.18 },
            FaceShapeWeights: new[] { 0.20, 0.25, 0.15, 0.25, 0.15 }, // more Square/Round
            HeightToShoulderCorr: 0.55,
            HeightToHipCorr: 0.48,
            FrameToBreadthsCorr: 0.38,
            ShoulderBreadthBaseFemale: (34, 44),
            ShoulderBreadthBaseMale: (40, 50),
            HipBreadthBaseFemale: (38, 46),
            HipBreadthBaseMale: (35, 44),
            NoseProminence: (0.55, 0.18),  // nose becomes more prominent with age
            LipFullness: (0.44, 0.14),  // lips thin slightly
            SexRatioBias: 0.55
        );

        /// <summary>
        /// Old: 65+ years.
        /// Reduced height, more uniform frame distribution, pronounced facial features.
        /// </summary>
        private static AppearanceGenSpec Old(SexBiology sex) => new(
            HeightFemale: (148, 166),
            HeightMale: (158, 178),
            BodyFrameWeights: new[] { 0.15, 0.30, 0.35, 0.20 },
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: new[] { 0.05, 0.05, 0.15, 0.35, 0.40 }, // mostly gray/white tones
            HairTypeWeights: new[] { 0.50, 0.35, 0.15 },
            FaceShapeWeights: new[] { 0.20, 0.25, 0.10, 0.25, 0.20 },
            HeightToShoulderCorr: 0.50,
            HeightToHipCorr: 0.45,
            FrameToBreadthsCorr: 0.35,
            ShoulderBreadthBaseFemale: (30, 42),
            ShoulderBreadthBaseMale: (36, 48),
            HipBreadthBaseFemale: (36, 46),
            HipBreadthBaseMale: (33, 44),
            NoseProminence: (0.62, 0.16),
            LipFullness: (0.38, 0.12),
            SexRatioBias: 0.50
        );
        #endregion
    }
}
