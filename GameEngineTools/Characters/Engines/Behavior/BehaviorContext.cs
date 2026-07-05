// BehaviorContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Immutable snapshot of everything behavior sub-engines need for one orchestration pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built once per tick by <see cref="DefaultBehaviorEngine"/> and passed immutably
    /// through the entire pipeline: need engines → modifiers → intent → arbitration.
    /// </para>
    /// <para>
    /// <b><see cref="AvailableObjects"/>:</b><br/>
    /// Populated from <see cref="IWorldObjectProvider"/> when the engine is configured
    /// with one. Modifier engines (e.g. <see cref="Modifiers.WorldObjectAffordanceEngine"/>)
    /// read this list to nudge candidate utility based on physical objects present
    /// in the character's current location. <c>null</c> when no provider is registered
    /// (e.g. in unit tests or headless simulations without world data).
    /// </para>
    /// </remarks>
    internal sealed record BehaviorContext(
        WDateTime Now,
        WTimeSpan Dt,
        IHumanContext HumanContext,
        IEventCollector Outbox,
        BehaviorState State,
        BehaviorConfig Config,
        IReadOnlyDictionary<string, double> Cooldowns,
        IDictionary<string, DecisionWorkingSet>? DecisionWorkingSets = null,
        IHabitApplicabilityModulator? HabitApplicabilityModulator = null,

        /// <summary>
        /// World objects present in the character's current location.
        /// <c>null</c> if no <see cref="IWorldObjectProvider"/> is wired into the engine
        /// (tests, standalone runs).
        /// Only available objects (<see cref="WorldObject.IsAvailable"/> == true) are included.
        /// </summary>
        IReadOnlyList<WorldObject>? AvailableObjects = null,

        /// <summary>
        /// Items currently held by this character (the pantry/inventory), from
        /// <see cref="IWorldObjectProvider.GetHeldBy"/>. Drives food-economy Tier 1 gating and
        /// provisioning (eat-from-hand / recipe inputs). <c>null</c> when no provider is wired.
        /// </summary>
        IReadOnlyList<WorldObject>? HeldObjects = null);
}
