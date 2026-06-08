// AppearanceLatents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    /// <summary>
    /// Continuous latent vector describing body morphology, sampled during character
    /// generation and projected into concrete <see cref="Traits.BodyMorphology"/> traits.
    /// All components are normalised loadings.
    /// </summary>
    public sealed record BodyLatent(
        double HeightCm,
        double HeightNorm,
        double Robustness,
        double Vertical,
        double Horizontal,
        double Muscularity,
        double Adiposity,
        double LowerMass,
        double SoftTissueFullness,
        double Posture,
        double Juvenility,
        double Aging,
        double SexDimorphism);

    /// <summary>
    /// Continuous latent vector describing facial morphology, sampled during character
    /// generation and projected into concrete <see cref="Traits.FacialMorphology"/> traits.
    /// </summary>
    public sealed record FaceLatent(
        double Robustness,
        double Softness,
        double MidfaceProjection,
        double Angularity,
        double BrowProminence,
        double NoseScale,
        double EyeScale,
        double LipFullness,
        double Juvenility,
        double Aging,
        double SexDimorphism);
}
