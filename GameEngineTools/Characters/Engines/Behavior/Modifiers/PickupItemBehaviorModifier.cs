// PickupItemBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers;

using System.Collections.Generic;
using System.Linq;
using GameEngineTools.World.Objects;
using static ActionNames;

/// <summary>
/// Generates a <see cref="ActionNames.PickupItem"/> candidate when the character's
/// current location allows pickup and contains at least one pickable world object.
/// </summary>
/// <remarks>
/// This is a stub implementation. Full pickup arbitration (item selection, weight limits,
/// need-driven priority) is deferred to a later development phase.
/// </remarks>
internal sealed class PickupItemBehaviorModifier : IBehaviorModifierEngine
{
    private const double BasePickupUtility = 10.0;

    /// <inheritdoc/>
    public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
    {
        // Only generate the candidate when available world objects are wired in.
        if (context.AvailableObjects is null)
            return;

        // Check location descriptor for pickup permission.
        var surface = context.HumanContext.Snapshot.InteractionSurface;
        var locationId = surface.Location;
        if (locationId is null)
            return;

        // Look for at least one pickable object at this location.
        var hasPickable = context.AvailableObjects.Any(o => o.IsPickable && o.IsAvailable);
        if (!hasPickable)
            return;

        // Add the PickupItem action with a base utility that other modifiers can scale.
        BehaviorCandidateEditor.Add(candidates, PickupItem, BasePickupUtility);
    }
}
