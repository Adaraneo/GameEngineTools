// LifeStageEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.LifeStage
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitted when a character crosses a life-stage boundary (e.g. Teenager → Adult).
    /// </summary>
    /// <remarks>
    /// Transitions are <b>not</b> scripted crises. They merely raise the probability of a
    /// (usually mild, often null) life-evaluation episode and feed gradual value/identity
    /// reweighting. The age-locked "midlife crisis" is explicitly NOT modelled.
    /// </remarks>
    public sealed record LifeStageTransitionOccurred(
        WDateTime OccurredAt,
        HumanId Human,
        StadiumType From,
        StadiumType To) : IDomainEvent;

    /// <summary>
    /// Emitted when a transition (or loss/illness) actually triggers a life-evaluation episode —
    /// a months-to-years span of mild reappraisal. Most transitions do NOT produce one.
    /// </summary>
    public sealed record LifeEvaluationEpisodeStarted(
        WDateTime OccurredAt,
        HumanId Human,
        StadiumType From,
        StadiumType To) : IDomainEvent;

    /// <summary>
    /// Emitted when the last child leaves the household. Default effect is a small <b>positive</b>
    /// shift in relational satisfaction (Bouchard 2014); a minority with strong parenting identity
    /// experience a transient negative.
    /// </summary>
    public sealed record EmptyNestOccurred(
        WDateTime OccurredAt,
        HumanId Human) : IDomainEvent;
}
