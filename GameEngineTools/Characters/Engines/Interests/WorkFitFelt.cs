// WorkFitFelt.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Raised at most once a day when a character works: how well the job's theme fits their interests.</summary>
    /// <param name="OccurredAt">When the work was done.</param>
    /// <param name="Human">The worker.</param>
    /// <param name="Fit">The job theme's interest relative to the worker's own mean (about −0.3..+0.3); positive = a job they like.</param>
    public sealed record WorkFitFelt(WDateTime OccurredAt, HumanId Human, double Fit) : IDomainEvent;
}
