// AppearanceProjector.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Projects a <see cref="GeneticBlueprint"/> onto a <see cref="PhysicalAppearance"/> at a given age.
    /// Pure static math — no DI, no state, fully deterministic per age year.
    /// </summary>
    public static class AppearanceProjector
    {
        #region Public API

        /// <summary>
        /// Derives the current <see cref="PhysicalAppearance"/> from the genetic blueprint and the character's age.
        /// </summary>
        /// <param name="blueprint">Immutable genetic data generated once at character creation.</param>
        /// <param name="ageYears">Current age in years (fractional values supported).</param>
        /// <returns>Age-appropriate <see cref="PhysicalAppearance"/>.</returns>
        public static PhysicalAppearance Project(GeneticBlueprint blueprint, double ageYears)
        {
            var juvVal = Juvenility(ageYears);
            var agingVal = AgingFactor(ageYears);
            var heightCm = ProjectedHeightCm(blueprint, ageYears);

            var bodyLatent = blueprint.BodyLatent with
            {
                HeightCm = heightCm,
                Juvenility = juvVal,
                Aging = agingVal
            };

            var ms = BuildMorphologySpec(blueprint, juvVal, agingVal);

            // Seed is per-age-year so the same character at the same age always looks identical.
            IRandomSource rng = new InlineRng(blueprint.Seed + (int)ageYears);

            // Recompute correlated face latent from the projected body latent, then overlay
            // the character's genetic NoseScale / EyeScale / LipFullness from the blueprint.
            var faceLatent = AppearanceMath.ProjectFaceLatent(ms, bodyLatent, blueprint.FaceLatent, rng);

            var body = AppearanceMath.GenerateBody(bodyLatent, rng);
            var face = AppearanceMath.GenerateFace(body, ms, faceLatent, rng);
            var surface = AppearanceMath.GenerateSurface(ms, faceLatent, bodyLatent, rng);
            var colors = ApplyHairGraying(blueprint.Colors, ageYears, blueprint.Seed);
            var marks = AppearanceMath.GenerateMarks(surface);
            var hairLengthCm = ProjectedHairLengthCm(ageYears, blueprint.Seed);

            return new PhysicalAppearance(body, face, surface, colors, marks, hairLengthCm);
        }

        #endregion Public API

        #region Continuous age functions

        /// <summary>Juvenility scalar: 0.95 at birth → 0.0 at age 30+.</summary>
        private static double Juvenility(double age)
        {
            if (age <= 0) return 0.95;
            if (age >= 30) return 0.0;
            // Piecewise smooth: rapid drop to 0.35 by 15, then linear to 0
            if (age <= 15)
            {
                // Sigmoid-like: 0.95 → 0.75 by 3, → 0.35 by 15
                var t = age / 15.0;
                return 0.95 - t * t * 0.60;
            }
            // Linear 0.35 → 0.0 from age 15 to 30
            return 0.35 * (1.0 - (age - 15.0) / 15.0);
        }

        /// <summary>Aging factor: 0.0 until age 30, rises to 0.45 at 50, 0.85 at 75+.</summary>
        private static double AgingFactor(double age)
        {
            if (age <= 30) return 0.0;
            if (age >= 75) return 0.85;
            if (age <= 50)
            {
                // 0.0 → 0.45 from age 30 to 50
                var t = (age - 30.0) / 20.0;
                return t * t * 0.45;
            }
            // 0.45 → 0.85 from age 50 to 75
            return 0.45 + ((age - 50.0) / 25.0) * 0.40;
        }

        /// <summary>
        /// Projects height from the blueprint's <c>HeightNorm</c>.
        /// Grows until ~18, stable to ~65, slight decline after.
        /// </summary>
        private static double ProjectedHeightCm(GeneticBlueprint blueprint, double ageYears)
        {
            // Determine adult height range from sex
            var (minH, maxH) = blueprint.Sex == SexBiology.Female ? (155.0, 175.0) : (165.0, 185.0);
            var adultHeight = (minH + maxH) * 0.5 + blueprint.BodyLatent.HeightNorm * (maxH - minH) * 0.5;

            if (ageYears >= 65)
            {
                // Slight height loss: up to -3 cm by age 85
                var loss = Math.Min(3.0, (ageYears - 65.0) * 0.15);
                return Math.Round(adultHeight - loss, 1);
            }

            if (ageYears >= 18)
                return Math.Round(adultHeight, 1);

            // Growth curve: baby ~50 cm, reaches full height at 18
            var minBabyHeight = blueprint.Sex == SexBiology.Female ? 49.0 : 50.0;
            var growthT = Math.Pow(Math.Max(0, ageYears) / 18.0, 0.55);
            return Math.Round(minBabyHeight + growthT * (adultHeight - minBabyHeight), 1);
        }

        /// <summary>
        /// Hair length seeded from blueprint: deterministic personal preference scaled by age range.
        /// </summary>
        private static double ProjectedHairLengthCm(double ageYears, int seed)
        {
            // Personal preference hash — stable across age years
            var preferenceHash = (uint)(seed ^ 0x9E3779B9) * 1664525u + 1013904223u;
            var t = Math.Pow((preferenceHash >> 8) / (double)(1u << 24), 1.25);

            var (min, max) = ageYears switch
            {
                < 1 => (0.0, 8.0),
                < 12 => (2.0, 55.0),
                < 18 => (3.0, 85.0),
                >= 65 => (0.0, 70.0),
                _ => (1.0, 100.0)
            };

            return Math.Round(min + t * (max - min), 1);
        }

        #endregion Continuous age functions

        #region Hair graying

        private static ColorTraits ApplyHairGraying(ColorTraits original, double ageYears, int seed)
        {
            // Deterministic gray onset age from blueprint seed: range 28–60
            var onsetAge = 28.0 + ((uint)(seed * 1664525 + 1013904223) >> 17 & 0x1F);
            if (ageYears < onsetAge) return original;

            // One lightening step per 12 years past onset
            var steps = (int)((ageYears - onsetAge) / 12.0);
            if (steps <= 0) return original;

            return original with { HairColor = LightenHair(original.HairColor, steps) };
        }

        private static HairColorNatural LightenHair(HairColorNatural color, int steps)
        {
            if (steps <= 0) return color;
            var next = color switch
            {
                HairColorNatural.Black    => HairColorNatural.DarkBrown,
                HairColorNatural.DarkBrown => HairColorNatural.Brown,
                HairColorNatural.Brown    => HairColorNatural.DarkBlond,
                HairColorNatural.Auburn   => HairColorNatural.DarkBlond,
                HairColorNatural.Red      => HairColorNatural.DarkBlond,
                _                          => HairColorNatural.Blond
            };
            return next == color ? color : LightenHair(next, steps - 1);
        }

        #endregion Hair graying

        #region MorphologySpec builder

        private static MorphologyGenerationSpec BuildMorphologySpec(GeneticBlueprint blueprint, double juvVal, double agingVal)
            => new MorphologyGenerationSpec(
                JitterAmplitude: juvVal >= 0.90 ? 0.06 : 0.10,
                SexBiasStrength: juvVal >= 0.60 ? 0.10 : 0.28,
                CorrelationStrength: 0.72,
                AsymmetryMean: agingVal >= 0.75 ? 0.075 : 0.055,
                SurfaceDetailRate: juvVal >= 0.90 ? 0.08 : 0.18 + agingVal * 0.20,
                Latent: new LatentFactorSpec(
                    Juvenility: juvVal,
                    AgingFactor: agingVal,
                    PostureFactor: 0.72 - agingVal * 0.20 + juvVal * 0.08,
                    SexDimorphismFactor: blueprint.Sex switch
                    {
                        SexBiology.Male   => 0.35,
                        SexBiology.Female => -0.28,
                        _                 => 0.0
                    }),
                Bounds: MorphologyBounds.Default);

        #endregion MorphologySpec builder

        #region Inline RNG

        /// <summary>
        /// Minimal <see cref="IRandomSource"/> backed by <see cref="System.Random"/>.
        /// Keeps <see cref="AppearanceProjector"/> dependency-free (no <c>IRandomSourceFactory</c>).
        /// </summary>
        private sealed class InlineRng : IRandomSource
        {
            private readonly Random _r;
            public InlineRng(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        #endregion Inline RNG
    }
}
