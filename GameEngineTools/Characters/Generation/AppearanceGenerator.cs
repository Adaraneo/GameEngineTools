// AppearanceGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Generates a realistic, correlated <see cref="PhysicalAppearance"/> for a character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses correlated random variables: height drives shoulder/hip breadths,
    /// body frame biases face shape weights, and sexual dimorphism is enforced
    /// via a post-generation nudge step controlled by <see cref="AppearanceGenSpec.SexRatioBias"/>.
    /// </para>
    /// <para>
    /// Generation is fully deterministic when a fixed seed is provided.
    /// </para>
    /// </remarks>
    public sealed class AppearanceGenerator : IAppearanceGenerator
    {
        #region Fields

        private readonly IRandomSourceFactory _rngFactory;

        #endregion Fields

        #region Constructor

        /// <summary>
        /// Initializes a new instance of <see cref="AppearanceGenerator"/>.
        /// </summary>
        /// <param name="rngFactory">Factory used to create a per-seed RNG instance.</param>
        public AppearanceGenerator(IRandomSourceFactory rngFactory)
            => _rngFactory = rngFactory;

        #endregion Constructor

        #region IAppearanceGenerator

        /// <inheritdoc/>
        public PhysicalAppearance Generate(SexBiology sex, int seed, AppearanceGenSpec? spec = null)
        {
            spec ??= AppearanceGenSpec.Default;
            var rng = _rngFactory.Create(seed);

            // ── 1) Height — primary latent variable ─────────────────────────
            var (hMin, hMax) = sex == SexBiology.Female ? spec.HeightFemale : spec.HeightMale;
            var height = Lerp(rng.NextUnit(), hMin, hMax);

            // ── 2) Body frame — discrete category ───────────────────────────
            var frame = Pick(
                new[] { BodyFrame.Petite, BodyFrame.Medium, BodyFrame.Large, BodyFrame.Strong },
                spec.BodyFrameWeights,
                rng);

            // ── 3) Shoulder and hip breadths — correlated to height and frame

            // Sex-specific shoulder baseline: males start from a broader range.
            var (shoulderMin, shoulderMax) = sex == SexBiology.Female
                ? spec.ShoulderBreadthBaseFemale
                : spec.ShoulderBreadthBaseMale;

            var shoulderBase = Lerp(rng.NextUnit(), shoulderMin, shoulderMax);

            var (hipMin, hipMax) = sex == SexBiology.Female
                ? spec.HipBreadthBaseFemale
                : spec.HipBreadthBaseMale;

            var hipBase = Lerp(rng.NextUnit(), hipMin, hipMax);

            // Linear correlation corrections:
            //   heightNorm  — normalised height in [-1, 1]; taller → proportionally broader
            //   frameBias   — body frame contribution; larger/stronger frame → broader
            var heightNorm = (height - (hMin + hMax) * 0.5) / ((hMax - hMin) * 0.5);
            var frameBias = frame switch
            {
                BodyFrame.Petite  => -0.35,
                BodyFrame.Medium  =>  0.00,
                BodyFrame.Large   =>  0.25,
                BodyFrame.Strong  =>  0.40,
                _                 =>  0.00
            };

            var shoulder = shoulderBase
                           + spec.HeightToShoulderCorr * heightNorm * 2.0
                           + spec.FrameToBreadthsCorr  * frameBias  * 2.0
                           + Jitter(rng, 0.6);

            var hip = hipBase
                      + spec.HeightToHipCorr         * heightNorm * 2.0
                      + spec.FrameToBreadthsCorr      * frameBias  * 1.5
                      + (sex == SexBiology.Female ? 0.6 : -0.4)   // slight dimorphism nudge
                      + Jitter(rng, 0.6);

            // Hard-clamp to raw baseline first, then soft-clamp to height-proportional bounds.
            shoulder = Clamp(shoulder, shoulderMin, shoulderMax);
            hip      = Clamp(hip,      hipMin,      hipMax);

            var (shrMin, shrMax) = sex == SexBiology.Female ? (0.20, 0.26) : (0.22, 0.28);
            var (hhrMin, hhrMax) = sex == SexBiology.Female ? (0.20, 0.27) : (0.19, 0.26);

            shoulder = SoftClamp(shoulder, shrMin * height, shrMax * height);
            hip      = SoftClamp(hip,      hhrMin * height, hhrMax * height);

            // ── Sex-ratio silhouette enforcement ────────────────────────────
            // Guarantees the correct sex-typical silhouette:
            //   Female: hip ≥ shoulder  (pear / hourglass)
            //   Male:   shoulder ≥ hip  (inverted triangle)
            //
            // Both dimensions are nudged proportionally — random variation is preserved,
            // only the sign of (shoulder − hip) is corrected.
            var bias = spec.SexRatioBias;

            if (sex == SexBiology.Female && shoulder > hip)
            {
                // Female with too-wide shoulders → redistribute excess toward hips.
                var excess = shoulder - hip;
                shoulder -= excess * bias;
                hip      += excess * (1.0 - bias);
            }
            else if (sex == SexBiology.Male && hip > shoulder)
            {
                // Male with too-wide hips → redistribute excess toward shoulders.
                var excess = hip - shoulder;
                hip      -= excess * bias;
                shoulder += excess * (1.0 - bias);
            }

            // ── 4) Colours and face shape ────────────────────────────────────
            var skin = Pick(
                new[] { SkinTone.Fair, SkinTone.Light, SkinTone.LightMedium, SkinTone.Medium, SkinTone.Tan },
                spec.SkinToneWeights,
                rng);

            var eyes = Pick(
                new[] { EyeColor.Brown, EyeColor.Hazel, EyeColor.Green, EyeColor.Blue, EyeColor.Gray },
                spec.EyeColorWeights,
                rng);

            var hairColor = Pick(
                new[]
                {
                    HairColorNatural.Black,
                    HairColorNatural.DarkBrown,
                    HairColorNatural.Brown,
                    HairColorNatural.DarkBlond,
                    HairColorNatural.Blond
                },
                spec.HairColorWeights,
                rng);

            var hairType = Pick(
                new[] { HairType.Straight, HairType.Wavy, HairType.Curly },
                spec.HairTypeWeights,
                rng);

            var faceWeights = (double[])spec.FaceShapeWeights.Clone();

            // Subtle bias: stronger/larger frame nudges toward Square/Oblong;
            // slender frame nudges toward Oval/Heart.
            switch (frame)
            {
                case BodyFrame.Petite:
                    Bias(faceWeights, FaceShape.Oval,   +0.05);
                    Bias(faceWeights, FaceShape.Heart,  +0.03);
                    Bias(faceWeights, FaceShape.Square, -0.04);
                    break;

                case BodyFrame.Large:
                case BodyFrame.Strong:
                    Bias(faceWeights, FaceShape.Square, +0.05);
                    Bias(faceWeights, FaceShape.Oblong, +0.03);
                    Bias(faceWeights, FaceShape.Heart,  -0.03);
                    break;
            }

            Normalize(faceWeights);

            var face = Pick(
                new[] { FaceShape.Oval, FaceShape.Round, FaceShape.Heart, FaceShape.Square, FaceShape.Oblong },
                faceWeights,
                rng);

            // ── 5) Fine facial features ──────────────────────────────────────
            // BoxMuller-style approximation: sum of 3 uniform samples approximates
            // a normal distribution without requiring Math.Sqrt or trig functions.
            double SampleNormal(double mean, double dev)
                => Clamp(mean + dev * (rng.NextUnit() + rng.NextUnit() + rng.NextUnit() - 1.5), 0.0, 1.0);

            var nose = Math.Round(SampleNormal(spec.NoseProminence.Mean, spec.NoseProminence.Dev), 2);

            // Females have a slight lip fullness bias on average.
            var lipBias = sex == SexBiology.Female ? +0.03 : -0.02;
            var lips    = Math.Round(SampleNormal(spec.LipFullness.Mean + lipBias, spec.LipFullness.Dev), 2);

            return new PhysicalAppearance(
                HeightCm:          height,
                Frame:             frame,
                SkinTone:          skin,
                EyeColor:          eyes,
                HairColor:         hairColor,
                HairType:          hairType,
                FaceShape:         face,
                ShoulderBreadthCm: shoulder,
                HipBreadthCm:      hip,
                NoseProminence:    nose,
                LipFullness:       lips,
                DistinctiveMarks:  null  // extension point: supply via a custom factory if needed
            );
        }

        #endregion IAppearanceGenerator

        #region Private helpers

        // Linear interpolation between a and b at position u in [0, 1].
        private static double Lerp(double u, double a, double b)
            => a + (b - a) * u;

        // Hard clamp of x to [a, b].
        private static double Clamp(double x, double a, double b)
            => x < a ? a : (x > b ? b : x);

        // Soft clamp: values outside [min, max] are pulled back with factor k
        // instead of being cut off hard, preserving a small amount of overshoot.
        private static double SoftClamp(double v, double min, double max, double k = 0.2)
        {
            if (v < min)
            {
                return min + (v - min) * k;
            }

            if (v > max)
            {
                return max + (v - max) * k;
            }

            return v;
        }

        // Returns a uniform random value in [-amplitude/2, +amplitude/2].
        private static double Jitter(IRandomSource rng, double amplitude)
            => (rng.NextUnit() - 0.5) * amplitude;

        // Picks a value from values[] using weighted random sampling (roulette wheel).
        private static T Pick<T>(IReadOnlyList<T> values, IReadOnlyList<double> weights, IRandomSource rng)
        {
            double s = 0;
            var r = rng.NextUnit();

            for (var i = 0; i < values.Count; i++)
            {
                s += weights[i];

                if (r <= s)
                {
                    return values[i];
                }
            }

            return values[^1];
        }

        // Adds delta to the weight of a specific FaceShape entry.
        // Clamps to a minimum of 0 to prevent negative weights.
        private static void Bias(double[] w, FaceShape target, double delta)
        {
            var idx = target switch
            {
                FaceShape.Oval    => 0,
                FaceShape.Round   => 1,
                FaceShape.Heart   => 2,
                FaceShape.Square  => 3,
                FaceShape.Oblong  => 4,
                _                 => 0
            };

            w[idx] = Math.Max(0.0, w[idx] + delta);
        }

        // Normalises a weight array so all elements sum to 1.
        // Falls back to uniform distribution if the total is zero or negative.
        private static void Normalize(double[] w)
        {
            var sum = 0.0;

            foreach (var x in w)
            {
                sum += x;
            }

            if (sum <= 0)
            {
                var u = 1.0 / w.Length;

                for (var i = 0; i < w.Length; i++)
                {
                    w[i] = u;
                }

                return;
            }

            for (var i = 0; i < w.Length; i++)
            {
                w[i] /= sum;
            }
        }

        #endregion Private helpers
    }
}
