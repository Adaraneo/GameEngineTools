// ObjectEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Fired when a character successfully takes an object from a location.
    /// </summary>
    public sealed record ObjectTaken(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        string FromLocationId) : IDomainEvent;

    /// <summary>
    /// Fired when a character uses an object in place (UseInPlace) or drops a held object.
    /// </summary>
    public sealed record ObjectUsed(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        string AtLocationId,
        bool WasConsumed) : IDomainEvent;

    /// <summary>
    /// Fired when a character drops a held object back into the world.
    /// </summary>
    public sealed record ObjectDropped(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        string AtLocationId) : IDomainEvent;

    /// <summary>
    /// Fired when the interaction policy refused an object interaction.
    /// </summary>
    public sealed record ObjectInteractionRefused(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        string Reason,
        bool IsSocial) : IDomainEvent;

    /// <summary>
    /// Fired when an object affordance (other than Ownership) is applied to a character.
    /// Downstream physiology/psychology engines consume this to adjust their state.
    /// </summary>
    public sealed record ObjectAffordanceApplied(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        AffordanceType AffordanceType,
        double Satisfaction) : IDomainEvent;
}
