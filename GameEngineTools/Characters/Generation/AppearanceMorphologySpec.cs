// AppearanceMorphologySpec.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Tunable morphology generation parameters layered on top of <see cref="AppearanceGenSpec"/>.
    /// </summary>
    public sealed record MorphologyGenerationSpec(
        double JitterAmplitude,
        double SexBiasStrength,
        double CorrelationStrength,
        double AsymmetryMean,
        double SurfaceDetailRate,
        LatentFactorSpec Latent,
        MorphologyBounds Bounds)
    {
        /// <summary>Creates stadium-aware morphology defaults.</summary>
        public static MorphologyGenerationSpec For(StadiumType stadium, SexBiology sex)
        {
            var juvenile = stadium switch
            {
                StadiumType.Baby => 0.95,
                StadiumType.Child => 0.75,
                StadiumType.Teenager => 0.35,
                StadiumType.Old => 0.05,
                StadiumType.MidAged => 0.10,
                _ => 0.15
            };

            var aging = stadium switch
            {
                StadiumType.Old => 0.85,
                StadiumType.MidAged => 0.45,
                _ => 0.0
            };

            return new MorphologyGenerationSpec(
                JitterAmplitude: stadium == StadiumType.Baby ? 0.06 : 0.10,
                SexBiasStrength: stadium is StadiumType.Baby or StadiumType.Child ? 0.10 : 0.28,
                CorrelationStrength: 0.72,
                AsymmetryMean: stadium == StadiumType.Old ? 0.075 : 0.055,
                SurfaceDetailRate: stadium == StadiumType.Baby ? 0.08 : 0.18 + aging * 0.20,
                Latent: new LatentFactorSpec(
                    Juvenility: juvenile,
                    AgingFactor: aging,
                    PostureFactor: 0.72 - aging * 0.20 + juvenile * 0.08,
                    SexDimorphismFactor: sex switch
                    {
                        SexBiology.Male => 0.35,
                        SexBiology.Female => -0.28,
                        _ => 0.0
                    }),
                Bounds: MorphologyBounds.Default);
        }
    }

    /// <summary>Baseline latent factors supplied by the effective stadium and sex profile.</summary>
    public sealed record LatentFactorSpec(
        double Juvenility,
        double AgingFactor,
        double PostureFactor,
        double SexDimorphismFactor);

    /// <summary>Safety bounds for generated morphology values.</summary>
    public sealed record MorphologyBounds(
        (double Min, double Max) Unit,
        (double Min, double Max) FacialAsymmetry,
        (double Min, double Max) MandibleAngle,
        (double Min, double Max) NasolabialAngle)
    {
        /// <summary>Neutral bounded morphology ranges.</summary>
        public static MorphologyBounds Default => new(
            Unit: (0.0, 1.0),
            FacialAsymmetry: (0.0, 0.16),
            MandibleAngle: (95.0, 140.0),
            NasolabialAngle: (82.0, 118.0));
    }
}
