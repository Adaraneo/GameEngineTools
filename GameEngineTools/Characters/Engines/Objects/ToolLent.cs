// ToolLent.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>An owner lent a tool to someone after a request.</summary>
    public sealed record ToolLent(WDateTime OccurredAt, HumanId Owner, HumanId Borrower, string ToolId) : IDomainEvent;
}
