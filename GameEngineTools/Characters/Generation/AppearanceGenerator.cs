// AppearanceGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Characters.Traits;

    using static AppearanceMath;

    /// <summary>
    /// Generates correlated, anatomically structured physical morphology.
    /// </summary>
    public sealed class AppearanceGenerator : IAppearanceGenerator
    {
        #region Fields

        private readonly IRandomSourceFactory _rngFactory;

        #endregion Fields

        #region Constructor

        /// <summary>Initializes a deterministic appearance generator.</summary>
        public AppearanceGenerator(IRandomSourceFactory rngFactory)
            => _rngFactory = rngFactory;

        #endregion Constructor

        #region IAppearanceGenerator

        /// <inheritdoc/>
        public GeneticBlueprint GenerateBlueprint(SexBiology sex, int seed, AppearanceGenSpec? spec = null)
        {
            // Blueprint always uses Adult-neutral parameters — the projector supplies age overrides at runtime.
            spec ??= AppearanceGenSpec.Default;
            var ms = spec.Morphology ?? MorphologyGenerationSpec.For(StadiumType.Adult, sex);
            var rng = _rngFactory.Create(seed);

            var bodyLatent = GenerateBodyLatent(sex, spec, ms, rng);
            var faceLatent = GenerateFaceLatent(ms, bodyLatent, rng);
            var colors = GenerateColors(spec, rng);

            return new GeneticBlueprint(sex, seed, colors, bodyLatent, faceLatent);
        }

        /// <inheritdoc/>
        public PhysicalAppearance Generate(SexBiology sex, int seed, StadiumType stadium = StadiumType.Adult, AppearanceGenSpec? spec = null)
            => AppearanceProjector.Project(GenerateBlueprint(sex, seed, spec), AgeFromStadium(stadium));

        #endregion IAppearanceGenerator

        #region Latent factors

        private static BodyLatent GenerateBodyLatent(SexBiology sex, AppearanceGenSpec spec, MorphologyGenerationSpec ms, IRandomSource rng)
        {
            var (min, max) = sex == SexBiology.Female ? spec.HeightFemale : spec.HeightMale;
            var mid = (min + max) * 0.5;
            var height = Clamp(mid + Normal(rng) * Math.Max(1.0, (max - min) / 4.6), min, max);
            var hNorm = ClampSigned((height - mid) / Math.Max(1.0, (max - min) * 0.5));
            var sexDim = ClampSigned(ms.Latent.SexDimorphismFactor + Normal(rng) * 0.18);
            var juvenile = C01(ms.Latent.Juvenility + Normal(rng) * 0.05);
            var aging = C01(ms.Latent.AgingFactor + Normal(rng) * 0.06);
            var robust = C01(0.52 + hNorm * 0.14 + sexDim * ms.SexBiasStrength + Normal(rng) * 0.15 - juvenile * 0.18);
            var muscle = C01(0.45 + robust * 0.28 + sexDim * 0.12 + Normal(rng) * 0.16 - aging * 0.06);
            var fat = C01(0.42 + Normal(rng) * 0.17 + aging * 0.08 + juvenile * 0.07);
            var lowerMass = C01(0.48 - sexDim * 0.16 + fat * 0.22 + Normal(rng) * 0.13);
            var soft = C01(0.44 - robust * 0.16 - muscle * 0.10 - sexDim * 0.10 + fat * 0.22 + juvenile * 0.25 + Normal(rng) * 0.12);
            var vertical = ClampSigned(hNorm * 0.45 + Normal(rng) * 0.35);
            var horizontal = ClampSigned(robust * 0.55 + fat * 0.35 + Normal(rng) * 0.25 - 0.45);
            var posture = C01(ms.Latent.PostureFactor + muscle * 0.10 - aging * 0.22 + Normal(rng) * 0.10);

            return new(height, hNorm, robust, vertical, horizontal, muscle, fat, lowerMass, soft, posture, juvenile, aging, sexDim);
        }

        #endregion Latent factors

        #region Private helpers

        private static double AgeFromStadium(StadiumType stadium) => stadium switch
        {
            StadiumType.Baby => 0.5,
            StadiumType.Child => 7.0,
            StadiumType.Teenager => 15.0,
            StadiumType.MidAged => 45.0,
            StadiumType.Old => 72.0,
            _ => 25.0  // Adult
        };

        #endregion Private helpers
    }
}
