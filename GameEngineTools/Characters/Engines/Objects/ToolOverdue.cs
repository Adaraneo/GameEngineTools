// ToolOverdue.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>A lent tool has been kept past the grace period; the borrower is reminded, which renews the memory that it is owed back.</summary>
    public sealed record ToolOverdue(WDateTime OccurredAt, HumanId Owner, HumanId Borrower, string ToolId) : IDomainEvent;
}
