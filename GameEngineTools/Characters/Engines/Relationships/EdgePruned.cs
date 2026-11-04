// EdgePruned.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A relationship edge was dropped to stay under <see cref="RelationshipsConfig.MaxEdges"/>; whoever holds beliefs about <paramref name="Other"/> may forget them.</summary>
    public sealed record EdgePruned(WDateTime OccurredAt, HumanId Human, HumanId Other) : IDomainEvent;
}
