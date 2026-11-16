// ObjectsPerceived.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A character looked around a place and saw these tools and workstations; what they had remembered there and no longer sees is gone.</summary>
    public sealed record ObjectsPerceived(WDateTime OccurredAt, HumanId Observer, string LocationId, IReadOnlyList<PerceivedObject> Objects) : IDomainEvent;
}
