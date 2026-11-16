// ToolRequestRefused.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// An owner declined to lend a tool, or no owner could be asked.
    /// </summary>
    /// <param name="OccurredAt">When the request was settled.</param>
    /// <param name="Requester">The character who asked.</param>
    /// <param name="Owner">The character who was asked.</param>
    /// <param name="ToolTypeId">Id of the requested tool type.</param>
    /// <param name="Reason">Why the request failed.</param>
    public sealed record ToolRequestRefused(WDateTime OccurredAt, HumanId Requester, HumanId Owner, string ToolTypeId, string Reason) : IDomainEvent;
}
