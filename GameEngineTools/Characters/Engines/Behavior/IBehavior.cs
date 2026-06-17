// IBehavior.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior.Intent;
    using GameEngineTools.World.Utils.Time;

    #region Configuration and state

    /// <summary>
    /// Runtime tuning parameters for the orchestrated behavior system.
    /// </summary>
    public sealed record BehaviorConfig(
        double InertiaWeight = 0.25,
        double NoveltyPenalty = 0.1,
        double PlanningHorizonHours = 2,
        double BaseSleepHours = 8,
        double MinSleepHours = 4,
        double MaxSleepHours = 12,
        double SleepCooldownHours = 16,
        bool UseIntentManagement = true,
        double IntentSwitchMargin = 10,
        double IntentBaseBias = 8,
        double IntentCommitmentBiasStep = 1,
        double IntentTimeoutHours = 2,
        double EmergencyIntentOverrideThreshold = 75,
        double HabitLearningRate = 0.08,
        double HabitDecayPerDay = 0.015,
        double HabitMaxUtilityMultiplier = 0.18,
        double HabitMaxFlatBias = 4.0,
        int MaxHabitTraces = 64,
        /// <summary>
        /// Maximum utility reduction applied to Work/Create when Noise=1.0 (Glass &amp; Singer 1972).
        /// At Noise=0.55 penalty is 0; at Noise=1.0 penalty equals this value.
        /// </summary>
        double NoiseCognitivePenaltyMax = 0.45,
        /// <summary>
        /// ReachOut utility bonus per point of PerceivedPrestige above 50.
        /// High-prestige targets attract voluntary social approach (Redhead et al. 2019).
        /// </summary>
        double PrestigeReachOutBonusPerPoint = 0.06,
        /// <summary>
        /// ReachOut utility penalty per point of PerceivedDominance above 70 when Closeness &lt; 30.
        /// Dominant strangers trigger avoidance; close dominant figures do not (Cheng et al. 2013).
        /// </summary>
        double DominanceAvoidancePenaltyPerPoint = 0.08,
    /// <summary>
    /// Maximum habit utility multiplier applied to locomotion actions (<c>MoveTo:*</c>).
    /// Intentionally lower than <see cref="HabitMaxUtilityMultiplier"/> because locomotion
    /// is an instrumental action — it does not directly satisfy the social need, only positions
    /// the character to do so. Capping it prevents the habit system from locking a character
    /// into perpetual movement without ever reaching the terminal interaction (ReachOut).
    /// </summary>
    /// <remarks>
    /// Design rationale: if <c>MoveTo:Social</c> accumulates a full-strength habit
    /// (multiplier=0.18, flatBias=4.0), its utility can grow past <c>ReachOut</c>'s
    /// theoretical ceiling and permanently block social interaction. A locomotion cap
    /// of ~0.06 keeps movement habits meaningful without crowding out their goal.
    /// </remarks>
    double LocomotionHabitMultiplierCap = 0.06,

    /// <summary>
    /// Maximum flat habit bias added to locomotion actions (<c>MoveTo:*</c>).
    /// See <see cref="LocomotionHabitMultiplierCap"/>.
    /// </summary>
    double LocomotionHabitFlatBiasCap = 1.0,

    // ── Loss aversion (Prospect Theory) ───────────────────────────────────────
    // Status-quo bias is NOT fully reducible to loss aversion (Gal & Rucker 2018):
    // InertiaWeight (above) models the *inaction* component; the λ parameters below model
    // the *loss-weighting* component. The two are independently togglable.

    /// <summary>
    /// Loss-aversion coefficient λ for general (non-risky) action domains: a perceived loss relative
    /// to the status-quo reference point is weighted λ× an equal-magnitude gain. Source: Brown et al.
    /// 2024 (<i>JEL</i> 62(2)) general default [1.82–2.10]. Default 1.96.
    /// </summary>
    double LossAversionLambda = 1.96,

    /// <summary>
    /// Loss-aversion coefficient λ for risky-choice (uncertain-outcome) domains such as social
    /// approach with rejection risk. Lower than the general default. Source: Walasek et al. 2024
    /// (<i>J. Econ. Psych.</i> 103) pure risky monetary choice [1.10–1.53]. Default 1.31.
    /// </summary>
    double LossAversionLambdaRiskyChoice = 1.31,

    /// <summary>
    /// Modest scaling of λ by Neuroticism: λ_eff = λ × (1 + (N − 0.5) × this). Individual differences
    /// explain much of the heterogeneity in loss aversion. Hypothesis-grade link. Default 0.4.
    /// </summary>
    double LossAversionNeuroticismScale = 0.4,

    // ── Dark-core behavioral modulation ───────────────────────────────────────
    // D-factor (Moshagen et al. 2018) scales antagonistic utility and penalises prosocial actions.
    // Calibrated against: D↔self-reported aggression r≈.65–.67 (Moshagen et al. 2020);
    //                     D↔low empathy r≈−.31 to −.37 (Moshagen et al. 2020).

    /// <summary>
    /// Flat utility bonus added to antagonistic actions (e.g. <c>Fight</c>) per unit DarkCore axis [0..1].
    /// At DarkCore=1.0 the bonus equals this value; at DarkCore=0 the bonus is zero.
    /// Calibration anchor: D↔self-reported aggression r≈.65–.67 (Moshagen et al. 2020).
    /// </summary>
    double DarkCoreAntagonismUtilityWeight = 12.0,

    /// <summary>
    /// Flat utility penalty applied to prosocial actions (e.g. <c>ReachOut</c>, <c>InviteIntimacy</c>)
    /// per unit DarkCore axis [0..1]. At DarkCore=1.0 the penalty equals this value.
    /// Calibration anchor: D↔low empathy r≈−.31 to −.37 (Moshagen et al. 2020).
    /// </summary>
    double DarkCoreProsocialPenaltyWeight = 8.0)
    {
        /// <summary>Parameterless constructor — all fields use their defaults.</summary>
        public BehaviorConfig() : this(0.25, 0.1, 2, 8, 4, 12, 16, true, 10, 8, 1, 2, 75) { }
    }

    /// <summary>
    /// Persistent per-character behavior state carried from tick to tick.
    /// </summary>
    public sealed record BehaviorState(
        double NeedRest,
        double NeedFood,
        double NeedWater,
        double NeedBelonging,
        double NeedCompetence,
        double NeedIntimacy,
        PlannedAction? CurrentPlan,
        IReadOnlyDictionary<string, double>? Cooldowns = null,
        bool WaitingForSleepConfirmation = false,
        int SleepDeclineCount = 0,
        WDateTime? SleepGraceExpiresAt = null,
        ActiveIntent? ActiveIntent = null,
        IReadOnlyDictionary<string, BehaviorHabitTrace>? HabitTraces = null);

    #endregion Configuration and state

    #region Engine contract

    /// <summary>
    /// Public behavior engine contract.
    /// </summary>
    public interface IBehaviorEngine : IEngine<BehaviorState, BehaviorConfig>
    { }

    #endregion Engine contract

    #region Plans and events

    /// <summary>
    /// Represents the currently committed action and its expected runtime.
    /// </summary>
    public sealed record PlannedAction(
        string Name,
        WDateTime Start,
        WTimeSpan ExpectedDuration,
        double Utility,
        HumanId? TargetHuman = null,
        /// <summary>
        /// Populated when <see cref="Name"/> is <c>Eat</c> or <c>Drink</c> —
        /// identifies which world object is being consumed. Used by
        /// <see cref="DefaultPhysiologyEngine"/> to apply per-object nutritional gains.
        /// </summary>
        ObjectInteractionData? ObjectInteraction = null);

    /// <summary>
    /// Emitted when the engine proposes a winning action for the current tick.
    /// </summary>
    public sealed record ActionProposed(WDateTime OccurredAt, HumanId Human, string ActionName, double Utility, HumanId? TargetHuman = null, string? IntendedActionName = null, string? ConflictReason = null) : IDomainEvent;

    /// <summary>
    /// Emitted when the orchestrator commits an action to the behavior state.
    /// <see cref="ObjectInteraction"/> is populated when <see cref="ActionName"/> == <see cref="ActionNames.InteractWithObject"/>.
    /// </summary>
    public sealed record ActionCommitted(WDateTime OccurredAt, HumanId Human, string ActionName, WTimeSpan Duration, HumanId? TargetHuman = null, string? IntendedActionName = null, string? ConflictReason = null, ObjectInteractionData? ObjectInteraction = null) : IDomainEvent;

    #endregion Plans and events
}
