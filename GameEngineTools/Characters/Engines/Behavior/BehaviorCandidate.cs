// BehaviorCandidate.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Memory-shaped targeting metadata for socially scoped candidates.
    /// </summary>
    internal sealed record SocialTargetingData(
        HumanId TargetHuman,
        SpeechAct SpeechAct,
        double ExpectedAcceptance,
        double VulnerabilitySafety,
        double RejectionRisk,
        bool PsychologicallyBlocked = false,
        string? Reason = null);

    /// <summary>
    /// Concrete action option considered by the behavior pipeline during the current tick.
    /// </summary>
    internal sealed record BehaviorCandidate(
        string Name,
        double Utility,
        WTimeSpan Duration,
        BehaviorDomain Domain,
        IReadOnlyList<string>? Tags = null,
        SocialTargetingData? SocialTargeting = null);
}
