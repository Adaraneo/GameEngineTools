// ToolReturnDeferred.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A borrower would not walk so far to bring a tool back to its shed (more than the body can afford); they do not try again before <paramref name="RetryAt"/>.</summary>
    public sealed record ToolReturnDeferred(WDateTime OccurredAt, HumanId Borrower, string ToolId, WDateTime RetryAt) : IDomainEvent;
}
