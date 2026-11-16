// ToolLent.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// An owner lent a tool to someone after a request.
    /// </summary>
    /// <param name="OccurredAt">When the loan was made.</param>
    /// <param name="Owner">The character who owns the tool.</param>
    /// <param name="Borrower">The character the tool was lent to.</param>
    /// <param name="ToolId">Id of the lent tool object.</param>
    public sealed record ToolLent(WDateTime OccurredAt, HumanId Owner, HumanId Borrower, string ToolId) : IDomainEvent;
}
