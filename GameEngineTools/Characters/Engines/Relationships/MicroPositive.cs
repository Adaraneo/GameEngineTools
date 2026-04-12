// MicroPositive.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A brief positive gesture — smile, compliment, small act of help.</summary>
    public sealed record MicroPositive(
        WDateTime OccurredAt,
        HumanId A,
        HumanId B,
        string Kind) : IDomainEvent;
}
