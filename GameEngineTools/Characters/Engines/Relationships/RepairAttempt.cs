// RepairAttempt.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>An attempt to repair a damaged relationship, accepted or rejected.</summary>
    public sealed record RepairAttempt(
        WDateTime OccurredAt,
        HumanId A,
        HumanId B,
        bool Accepted) : IDomainEvent;
}
