// AffordanceApplicationService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Applies the affordances of a world object to a character by emitting
    /// <see cref="ObjectAffordanceApplied"/> events. Downstream engines (Physiology,
    /// Psychology) consume these events to adjust their state.
    /// </summary>
    /// <remarks>
    /// <see cref="AffordanceType.Ownership"/> is deliberately skipped — ownership
    /// transitions are managed by <see cref="DefaultObjectInteractionEngine"/> directly.
    /// </remarks>
    public static class AffordanceApplicationService
    {
        /// <summary>
        /// Applies a used object's affordances by emitting <c>ObjectAffordanceApplied</c> events
        /// for Physiology/Psychology to consume.
        /// </summary>
        /// <param name="obj">The object being used.</param>
        /// <param name="ctx">Character context.</param>
        /// <param name="outbox">Collector for emitted affordance events.</param>
        /// <param name="now">Current game time.</param>
        public static void Apply(
            WorldObject obj,
            IHumanContext ctx,
            IEventCollector outbox,
            WDateTime now)
        {
            foreach (var affordance in obj.Affordances)
            {
                if (affordance.Type == AffordanceType.Ownership)
                    continue; // ownership is handled by the engine, not applied as a stat delta

                outbox.Add(new ObjectAffordanceApplied(
                    now,
                    ctx.Id,
                    obj.Id,
                    affordance.Type,
                    affordance.Satisfaction));
            }
        }
    }
}
