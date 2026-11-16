// ToolSearchFailed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A character looked everywhere they knew for a tool and found none to take or borrow; they do not look again before <paramref name="RetryAt"/>.</summary>
    public sealed record ToolSearchFailed(WDateTime OccurredAt, HumanId Seeker, string ToolTypeIds, WDateTime RetryAt) : IDomainEvent;
}
