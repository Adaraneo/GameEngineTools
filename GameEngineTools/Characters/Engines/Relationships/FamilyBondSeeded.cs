// FamilyBondSeeded.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Domain event fired by <see cref="GameEngineTools.Characters.Generation.FamilyBuilder"/>
    /// to inject a pre-built <see cref="RelationshipEdge"/> directly into a character's
    /// relationship graph at world-setup time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This event bypasses the normal interaction-driven edge growth and seeds the edge
    /// with values that represent the emotional baseline of a known family bond
    /// (partner, parent-child, sibling).
    /// </para>
    /// <para>
    /// <b>Why an event instead of a direct engine call?</b><br/>
    /// <see cref="DefaultRelationshipsEngine"/> is <c>internal</c> and only exposed via
    /// <see cref="IRelationshipsEngine"/>. Using a domain event keeps <c>FamilyBuilder</c>
    /// decoupled from engine internals and consistent with the existing
    /// <c>IHuman.ReceiveEvent</c> delivery pattern used throughout the engine layer.
    /// </para>
    /// <para>
    /// <b>Handler location:</b><br/>
    /// <see cref="DefaultRelationshipsEngine.Handle"/> must include a case for this event
    /// that writes <see cref="Edge"/> directly into the state dictionary, overwriting any
    /// existing edge for <see cref="TargetId"/>.
    /// </para>
    /// </remarks>
    /// <param name="OwnerId">The character whose graph receives the edge (the observer).</param>
    /// <param name="TargetId">The character being described by the edge (the target).</param>
    /// <param name="Edge">The pre-built edge to inject.</param>
    public sealed record FamilyBondSeeded(
        HumanId OwnerId,
        HumanId TargetId,
        RelationshipEdge Edge) : IDomainEvent
    {
        public WDateTime OccurredAt => Edge.LastContactTime.GetValueOrDefault();
    }
}
