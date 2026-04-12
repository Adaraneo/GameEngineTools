// AppearanceGenSpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Immutable configuration record for <see cref="AppearanceGenerator"/>.
    /// </summary>
    /// <remarks>
    /// The spec now contains only inputs still consumed by the structured morphology
    /// generator. Coarse frame, face-shape, nose, lip, and shoulder/hip knobs were
    /// removed so geometry stays the source of truth.
    /// </remarks>
    public sealed record AppearanceGenSpec(
        /// <summary>Height sampling range for female characters (cm).</summary>
        (double Min, double Max) HeightFemale,

        /// <summary>Height sampling range for male characters (cm).</summary>
        (double Min, double Max) HeightMale,

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
        /// High-resolution morphology generation settings.
        /// When omitted, stadium-aware morphology defaults are resolved at generation time.
        /// </summary>
        MorphologyGenerationSpec? Morphology = null)
    {
        /// <summary>
        /// Returns a sensible, culturally neutral default spec
        /// suitable for most fantasy or RPG populations.
        /// </summary>
        public static AppearanceGenSpec Default => new(
            HeightFemale: (155, 175),
            HeightMale: (165, 185),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.42, 0.40, 0.18 });

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
        /// <param name="sex">Biological sex - affects height and proportion baselines.</param>
        public static AppearanceGenSpec ForStadium(StadiumType stadium, SexBiology sex)
            => stadium switch
            {
                StadiumType.Baby => Baby(),
                StadiumType.Child => Child(),
                StadiumType.Teenager => Teenager(),
                StadiumType.MidAged => MidAged(),
                StadiumType.Old => Old(),
                _ => Default
            };

        /// <summary>Baby: 0-2 years. Very small, round proportions, no sexual dimorphism yet.</summary>
        private static AppearanceGenSpec Baby() => new(
            HeightFemale: (45, 90),
            HeightMale: (45, 92),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.50, 0.35, 0.15 });

        /// <summary>Child: 3-11 years. Proportionally larger head, slim limbs, minimal dimorphism.</summary>
        private static AppearanceGenSpec Child() => new(
            HeightFemale: (90, 148),
            HeightMale: (90, 150),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.42, 0.40, 0.18 });

        /// <summary>Teenager: 12-17 years. Puberty proportions, growing dimorphism.</summary>
        private static AppearanceGenSpec Teenager() => new(
            HeightFemale: (148, 168),
            HeightMale: (150, 178),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.42, 0.40, 0.18 });

        /// <summary>Mid-aged: 40-64 years. Mature adult morphology with aging factors handled by morphology spec.</summary>
        private static AppearanceGenSpec MidAged() => new(
            HeightFemale: (154, 172),
            HeightMale: (162, 182),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: Uniform(5),
            HairTypeWeights: new[] { 0.42, 0.40, 0.18 });

        /// <summary>Old: 65+ years. Reduced height with surface aging handled by morphology spec.</summary>
        private static AppearanceGenSpec Old() => new(
            HeightFemale: (148, 166),
            HeightMale: (158, 178),
            SkinToneWeights: Uniform(5),
            EyeColorWeights: Uniform(5),
            HairColorWeights: new[] { 0.05, 0.05, 0.15, 0.35, 0.40 },
            HairTypeWeights: new[] { 0.50, 0.35, 0.15 });

        #endregion Factories
    }
}
