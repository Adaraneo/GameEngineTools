// LexicalAcquisitionConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Language
{
    /// <summary>
    /// Tunables for how fast words are learned and forgotten. Every magic number in the layer lives here.
    /// </summary>
    /// <param name="MinHalfLifeDays">Floor on the half-life (~15 minutes) — Duolingo's MIN_HALF_LIFE.</param>
    /// <param name="MaxHalfLifeDays">Ceiling on the half-life (~9 months) — Duolingo's MAX_HALF_LIFE.</param>
    /// <param name="ThetaBias">Constant term of the half-life regression.</param>
    /// <param name="ThetaSeen">Weight on √(times seen) — mere exposure.</param>
    /// <param name="ThetaCorrect">Weight on √(times correct) — successful use sticks hardest.</param>
    /// <param name="ThetaIncorrect">Weight on √(times incorrect); negative, so failure erodes the half-life.</param>
    /// <param name="ReceptiveThreshold">θ_R — familiarity at which a listener starts decoding the word.</param>
    /// <param name="ProductiveThreshold">θ_P — familiarity at which a speaker will actively reach for it.</param>
    /// <param name="ProductiveHalfLifeFactor">
    /// Production erodes faster than recognition: you understand a word long after you would still think
    /// to use it. Scales the effective half-life when asking whether a speaker can produce a lemma.
    /// </param>
    /// <param name="CatOverAccommodationCap">
    /// Ceiling on the social gain multiplier. Communication Accommodation Theory treats unbounded
    /// convergence as over-accommodation — without a cap, a single admired speaker would flood the
    /// population's vocabulary.
    /// </param>
    /// <param name="ClosenessGainWeight">w_c — how much a close bond accelerates picking words up.</param>
    /// <param name="DominanceGainWeight">k — how much the speaker's perceived standing accelerates it.</param>
    /// <param name="ComprehensionConfidenceMin">
    /// Listener confidence at or above which hearing the word counts as having understood it.
    /// </param>
    public sealed record LexicalAcquisitionConfig(
        double MinHalfLifeDays = 0.01,
        double MaxHalfLifeDays = 274.0,
        double ThetaBias = 0.0,
        double ThetaSeen = 0.3,
        double ThetaCorrect = 0.5,
        double ThetaIncorrect = -0.3,
        double ReceptiveThreshold = 0.20,
        double ProductiveThreshold = 0.50,
        double ProductiveHalfLifeFactor = 0.6,
        double CatOverAccommodationCap = 2.5,
        double ClosenessGainWeight = 0.5,
        double DominanceGainWeight = 0.4,
        double ComprehensionConfidenceMin = 0.5);
}
