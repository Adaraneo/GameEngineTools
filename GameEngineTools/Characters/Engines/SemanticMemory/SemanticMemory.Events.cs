// SemanticMemory.Events.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted whenever the character's belief about another person is updated.
    /// Consumed by the BehaviorEngine, the targeting subsystem and logging.
    /// </summary>
    public sealed record SemanticBeliefUpdated(
        WDateTime OccurredAt,
        /// <summary>The character whose belief was updated.</summary>
        HumanId Human,
        /// <summary>The person the belief is about.</summary>
        HumanId Other,
        /// <summary>The kind of belief that changed.</summary>
        PersonBeliefKind Kind,
        /// <summary>The new Strength value after the update [0.0–1.0].</summary>
        double Strength,
        /// <summary>Total number of pieces of evidence for this belief kind.</summary>
        int EvidenceCount) : IDomainEvent;

    /// <summary>
    /// Delivered by <c>DefaultSceneOrchestrator</c> after resolving a
    /// <see cref="Relationships.SignificantOtherThresholdCrossed"/> event into a full
    /// <see cref="SignificantOtherImprint"/> (cross-person appearance/personality data, only
    /// available at orchestrator level). <see cref="ISemanticMemoryEngine"/> appends the imprint
    /// on receipt (Topic C, Task C.1/C.2).
    /// </summary>
    public sealed record SignificantOtherImprintCaptured(
        WDateTime OccurredAt,
        HumanId Self,
        SignificantOtherImprint Imprint) : IDomainEvent;

    /// <summary>
    /// Delivered by <c>DefaultSceneOrchestrator</c> when a newly-encountered person's resemblance
    /// to a stored <see cref="SignificantOtherImprint"/> crosses the activation threshold.
    /// Cross-person appearance/personality resolution happens only at orchestrator level (see
    /// <c>DefaultSceneOrchestrator.FireFirstImpressions</c>) — this event carries the
    /// already-resolved result so <see cref="ISemanticMemoryEngine"/> only needs to apply it
    /// (Topic C, Task C.4).
    /// </summary>
    public sealed record TransferenceActivated(
        WDateTime OccurredAt,
        HumanId Self,
        HumanId NewPerson,
        PersonBeliefKind TransferredKind,
        double SourceBeliefStrength,
        double Resemblance) : IDomainEvent;
}
