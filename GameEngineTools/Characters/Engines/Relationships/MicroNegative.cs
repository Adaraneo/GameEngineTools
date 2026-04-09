// MicroNegative.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A brief negative gesture — criticism, being ignored, cold response.</summary>
    public sealed record MicroNegative(
        WDateTime OccurredAt,
        HumanId A,
        HumanId B,
        string What) : IDomainEvent;
}
