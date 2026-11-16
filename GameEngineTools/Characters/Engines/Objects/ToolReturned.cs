// ToolReturned.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A borrower gave a lent tool back; the loan ended.</summary>
    public sealed record ToolReturned(WDateTime OccurredAt, HumanId Owner, HumanId Borrower, string ToolId) : IDomainEvent;
}
