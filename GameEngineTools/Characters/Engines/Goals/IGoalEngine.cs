// IGoalEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Goals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;

    #region Enums

    /// <summary>Category of a persistent goal, used to route utility bias.</summary>
    public enum PersistentGoalKind
    {
        // Existential
        FindMeaning,
        OvercomeTrauma,
        BuildIdentity,

        // Survival / Safety
        ProtectFamily,
        EscapeDanger,

        // Career / Mastery
        MasterCraft,
        BuildReputation,

        // Relational
        FindPartner,
        RepairRelationship,
        SeekRevenge
    }

    /// <summary>How the goal was created.</summary>
    public enum GoalOrigin { Personality, Event, Scripted }

    /// <summary>Why the goal was removed from the active list.</summary>
    public enum GoalResolution { Completed, Abandoned, Faded, Displaced }

    #endregion

    #region PersistentGoal

    /// <summary>
    /// A long-term motivational drive that persists across ticks and biases
    /// utility-based action selection toward goal-relevant behaviour.
    /// </summary>
    /// <remarks>
    /// Goals do not prescribe a plan. Instead they apply a continuous utility
    /// pressure proportional to <see cref="Salience"/>. Progress and Salience
    /// are updated by <see cref="IGoalEngine"/> each tick based on committed
    /// actions and incoming domain events.
    /// </remarks>
    public sealed record PersistentGoal(
        Guid Id,
        PersistentGoalKind Kind,
        GoalOrigin Origin,

        /// <summary>
        /// Current motivational pressure [0..1]. Grows when the character acts
        /// toward this goal; decays passively each tick. Drives utility bias magnitude.
        /// </summary>
        double Salience,

        /// <summary>Completion progress [0..1]. Reaches 1.0 on goal completion.</summary>
        double Progress,

        /// <summary>
        /// Accumulated frustration [0..1]. Rises when goal-relevant actions are
        /// blocked or rejected. High frustration can trigger Abandoned resolution.
        /// </summary>
        double Frustration,

        WDateTime CreatedAt,
        WDateTime LastProgressAt,

        /// <summary>
        /// Optional target character — used for relational goals
        /// (RepairRelationship, SeekRevenge, ProtectFamily).
        /// </summary>
        HumanId? TargetHuman = null,

        /// <summary>Set when the goal is resolved; null while active.</summary>
        GoalResolution? Resolution = null);

    #endregion

    #region GoalState

    /// <summary>Immutable snapshot of all persistent goals for one character.</summary>
    public sealed record GoalState(IReadOnlyList<PersistentGoal> Goals)
    {
        /// <summary>Empty state for newly created characters.</summary>
        public static GoalState Empty { get; } = new(Array.Empty<PersistentGoal>());

        /// <summary>Returns only goals that are not yet resolved.</summary>
        public IEnumerable<PersistentGoal> Active => Goals.Where(g => g.Resolution is null);
    }

    #endregion

    #region Domain events

    /// <summary>Emitted when a new goal becomes active for the character.</summary>
    public sealed record GoalActivated(
        WDateTime OccurredAt,
        HumanId Human,
        Guid GoalId,
        PersistentGoalKind Kind,
        GoalOrigin Origin,
        double InitialSalience) : IDomainEvent;

    /// <summary>Emitted when salience or progress changes meaningfully (delta > 0.05).</summary>
    public sealed record GoalProgressed(
        WDateTime OccurredAt,
        HumanId Human,
        Guid GoalId,
        PersistentGoalKind Kind,
        double OldSalience,
        double NewSalience,
        double OldProgress,
        double NewProgress) : IDomainEvent;

    /// <summary>Emitted when a goal is resolved (completed, abandoned, faded, or displaced).</summary>
    public sealed record GoalResolved(
        WDateTime OccurredAt,
        HumanId Human,
        Guid GoalId,
        PersistentGoalKind Kind,
        GoalResolution Resolution) : IDomainEvent;

    /// <summary>
    /// External injection — allows scripts or external systems to give a character a goal directly.
    /// </summary>
    public sealed record GoalInjected(
        WDateTime OccurredAt,
        HumanId Human,
        PersistentGoalKind Kind,
        double InitialSalience,
        HumanId? TargetHuman = null) : IDomainEvent;

    #endregion

    #region IGoalEngine

    /// <summary>Manages a character's persistent long-term goals.</summary>
    public interface IGoalEngine : IEngine<GoalState, GoalConfig>
    {
        /// <summary>
        /// Seeds initial goals from the character's personality.
        /// Call once after factory construction, before the first tick.
        /// </summary>
        void SeedFromPersonality(Personality personality, WDateTime now);
    }

    #endregion
}
