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

        /// <summary>Existential — search for meaning/purpose.</summary>
        FindMeaning,

        /// <summary>Existential — overcome past trauma.</summary>
        OvercomeTrauma,

        /// <summary>Existential — build a coherent identity.</summary>
        BuildIdentity,

        // Survival / Safety

        /// <summary>Survival — protect family members.</summary>
        ProtectFamily,

        /// <summary>Survival — escape immediate danger.</summary>
        EscapeDanger,

        // Career / Mastery

        /// <summary>Career — master a craft or skill.</summary>
        MasterCraft,

        /// <summary>Career — build social reputation.</summary>
        BuildReputation,

        // Relational

        /// <summary>Relational — find a romantic partner.</summary>
        FindPartner,

        /// <summary>Relational — repair a damaged relationship.</summary>
        RepairRelationship,

        /// <summary>Relational — seek revenge for a transgression.</summary>
        SeekRevenge
    }

    /// <summary>How the goal was created.</summary>
    public enum GoalOrigin
    {
        /// <summary>Seeded from the character's personality.</summary>
        Personality,

        /// <summary>Triggered by a domain event.</summary>
        Event,

        /// <summary>Injected by a script or external system.</summary>
        Scripted
    }

    /// <summary>Why the goal was removed from the active list.</summary>
    public enum GoalResolution
    {
        /// <summary>Goal reached completion.</summary>
        Completed,

        /// <summary>Goal was abandoned (e.g. high frustration).</summary>
        Abandoned,

        /// <summary>Goal faded as salience decayed to zero.</summary>
        Faded,

        /// <summary>Goal was displaced by a higher-priority goal.</summary>
        Displaced
    }

    #endregion Enums

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
        GoalResolution? Resolution = null,

        /// <summary>
        /// Optional parent goal id, expressing the be-goal → do-goal → motor-goal hierarchy
        /// (Carver &amp; Scheier 1998). <c>null</c> for a top-level (be-)goal. Flat goals created before
        /// this field existed remain valid with a <c>null</c> parent.
        /// Children of a goal are those whose <see cref="ParentId"/> equals its <see cref="Id"/>.
        /// </summary>
        Guid? ParentId = null);

    #endregion PersistentGoal

    #region GoalState

    /// <summary>Immutable snapshot of all persistent goals for one character.</summary>
    public sealed record GoalState(IReadOnlyList<PersistentGoal> Goals)
    {
        /// <summary>Empty state for newly created characters.</summary>
        public static GoalState Empty { get; } = new(Array.Empty<PersistentGoal>());

        /// <summary>Returns only goals that are not yet resolved.</summary>
        public IEnumerable<PersistentGoal> Active => Goals.Where(g => g.Resolution is null);

        /// <summary>Returns the active child sub-goals of the goal with the given id.</summary>
        /// <param name="parentId">The parent goal id.</param>
        public IEnumerable<PersistentGoal> Children(Guid parentId)
            => Goals.Where(g => g.Resolution is null && g.ParentId == parentId);
    }

    #endregion GoalState

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

    /// <summary>
    /// Emitted when a persistently-blocked goal is actively disengaged from (progress stalled +
    /// frustration accumulated). Distinct from a fading/abandoned resolution: disengagement is an
    /// adaptive self-regulatory act that relieves distress and precedes reengagement.
    /// Source: Wrosch et al. (2003, <i>PSPB</i> 29(12)).
    /// </summary>
    public sealed record GoalDisengaged(
        WDateTime OccurredAt,
        HumanId Human,
        Guid GoalId,
        PersistentGoalKind Kind) : IDomainEvent;

    /// <summary>
    /// Emitted when, following disengagement, the character reengages on an alternative goal
    /// (a child/sibling sub-goal where possible, else another active goal). Reengagement predicts
    /// higher well-being. Source: Wrosch et al. (2003, <i>PSPB</i> 29(12)).
    /// </summary>
    public sealed record GoalReengaged(
        WDateTime OccurredAt,
        HumanId Human,
        PersistentGoalKind FromKind,
        Guid ToGoalId,
        PersistentGoalKind ToKind) : IDomainEvent;

    #endregion Domain events

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

    #endregion IGoalEngine
}
