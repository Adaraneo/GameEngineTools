// ToolRequestRefused.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>An owner declined to lend a tool, or no owner could be asked.</summary>
    public sealed record ToolRequestRefused(WDateTime OccurredAt, HumanId Requester, HumanId Owner, string ToolTypeId, string Reason) : IDomainEvent;
}
