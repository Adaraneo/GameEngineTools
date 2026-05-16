// AppearanceLatents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
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
