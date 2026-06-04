// MutualKnowledgeFormed.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.ToM
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a character forms <b>second-order</b> (L2) knowledge: not just "I know X",
    /// but "I know that <see cref="SharedWith"/> also knows X" — common ground.
    /// </summary>
    /// <remarks>
    /// Level-2 Theory of Mind. The default working recursion depth is 2
    /// (<see cref="ToMMath.DefaultRecursionDepth"/>); deeper recursion is bounded by a per-NPC
    /// ceiling (<see cref="Traits.Personality.ToMCeiling"/>) and degrades under stress.
    /// L3+ (recursive deception / intent) is intentionally a stub — see the research plan's frontier note.
    /// </remarks>
    public sealed record MutualKnowledgeFormed(
        WDateTime OccurredAt,
        HumanId Human,
        HumanId Subject,
        HumanId SharedWith,
        string ActionKind) : IDomainEvent;
}
