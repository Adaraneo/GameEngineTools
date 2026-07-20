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
        RelationalActKind RelationalActKind,
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
        SocialTargetingData? SocialTargeting = null,
        /// <summary>
        /// Present when <see cref="Name"/> == <see cref="ActionNames.InteractWithObject"/>
        /// or any affordance-driven object action (UseObject:Rest, UseObject:Work, …).
        /// Carries the target object ID, location, and intended interaction kind.
        /// </summary>
        ObjectInteractionData? ObjectInteraction = null,
        /// <summary>
        /// Body/mind channels this action requires for the duration of its execution.
        /// Two actions can run simultaneously only when their masks share no bits.
        /// Defaults to <see cref="ActionSlotMask.None"/> — safe for movement and passive actions.
        /// </summary>
        ActionSlotMask SlotMask = ActionSlotMask.None);
}
