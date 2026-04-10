// SemanticMemory.Events.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public sealed record SemanticBeliefUpdated(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Other,
        PersonBeliefKind Kind,
        double Strength,
        int EvidenceCount) : IDomainEvent;
}
