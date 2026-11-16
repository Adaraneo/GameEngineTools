// IRandomSource.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using Characters.Engines.Behavior;
    using Characters.Engines.Goals;
    using Characters.Engines.Interactions;
    using Characters.Engines.Physiology;
    using Characters.Engines.Psychology;
    using Characters.Engines.Relationships;
    using Characters.Engines.Schedule;
    using Characters.Engines.SemanticMemory;
    using Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Delegate run by the scheduler when a scheduled action fires.</summary>
    /// <param name="ctx">Character context and services.</param>
    /// <param name="outbox">Collector for events emitted by the action.</param>
    public delegate void ScheduledAction(IHumanContext ctx, IEventCollector outbox);

    /// <summary>Deterministic random source seeded per character.</summary>
    public interface IRandomSource
    {
        /// <summary>Returns a random integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        /// <param name="minInclusive">Inclusive lower bound.</param>
        /// <param name="maxExclusive">Exclusive upper bound.</param>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>Returns a random double in [0, 1).</summary>
        double NextUnit(); // <0,1)

        /// <summary>Returns <c>true</c> with probability <paramref name="p"/>.</summary>
        /// <param name="p">Probability in [0, 1].</param>
        bool Chance(double p);
    }
}
