// DefaultObjectInteractionPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;

    /// <summary>
    /// Default implementation of <see cref="IObjectInteractionPolicy"/>.
    /// Allows all interactions except: taking an object already held by someone else,
    /// or taking from a location that prohibits pickup.
    /// </summary>
    public sealed class DefaultObjectInteractionPolicy : IObjectInteractionPolicy
    {
        /// <inheritdoc/>
        public ObjectInteractionPermission Evaluate(
            IHumanContext actor,
            WorldObject target,
            ObjectInteractionKind kind,
            LocationDescriptor location)
        {
            if (kind == ObjectInteractionKind.Take && target.HeldBy is not null)
                return new ObjectInteractionPermission(false, "This object is held by someone else.", IsSocial: true);

            if (kind == ObjectInteractionKind.Take && !location.AllowsPickup)
                return new ObjectInteractionPermission(false, "Objects cannot be taken from this location.");

            if (kind == ObjectInteractionKind.Take && target.ConsumedAt is not null)
                return new ObjectInteractionPermission(false, "This object has already been consumed.");

            return new ObjectInteractionPermission(true);
        }
    }
}
