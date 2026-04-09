// RelationshipState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;

    /// <summary>Snapshot of the full relationship graph for one character.</summary>
    public sealed record RelationshipState(
        IReadOnlyDictionary<HumanId, RelationshipEdge> Edges);
}
