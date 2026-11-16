// ToolReturned.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// A borrower gave a lent tool back; the loan ended.
    /// </summary>
    /// <param name="OccurredAt">When the tool was put down.</param>
    /// <param name="Owner">The character who owns the tool.</param>
    /// <param name="Borrower">The character who had borrowed it.</param>
    /// <param name="ToolId">Id of the returned tool object.</param>
    public sealed record ToolReturned(WDateTime OccurredAt, HumanId Owner, HumanId Borrower, string ToolId) : IDomainEvent;
}
