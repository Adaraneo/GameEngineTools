// DefaultObjectInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using System.Linq;

    /// <summary>
    /// Resolves <see cref="ActionNames.InteractWithObject"/> actions committed by the Behavior engine.
    /// Evaluates the interaction policy, mutates the world object provider state, and emits the
    /// appropriate domain events for downstream engines (Memory, Psychology, etc.) to consume.
    /// </summary>
    public sealed class DefaultObjectInteractionEngine : IObjectInteractionEngine
    {
        private readonly IMutableWorldObjectProvider _objectProvider;
        private readonly ILocationService _locations;
        private readonly IObjectInteractionPolicy _policy;
        private readonly ILogger<DefaultObjectInteractionEngine> _logger;

        public DefaultObjectInteractionEngine(
            IMutableWorldObjectProvider objectProvider,
            ILocationService locations,
            IObjectInteractionPolicy policy,
            ILogger<DefaultObjectInteractionEngine> logger)
        {
            _objectProvider = objectProvider;
            _locations = locations;
            _policy = policy;
            _logger = logger;
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not ActionCommitted committed)
                return;

            if (committed.Human != ctx.Id)
                return;

            // Route on ObjectInteraction payload presence, not on a specific action name.
            // This covers InteractWithObject AND all affordance-driven action names
            // (UseObject:Rest, UseObject:Work, etc.) that ObjectInteractionBehaviorModifier
            // now emits. The action name is informational only; the payload drives behaviour.
            var data = committed.ObjectInteraction;
            if (data is null)
                return;

            // Resolve the world object — search the committed location first,
            // then fall back to a cache-wide search (needed for Drop when the object
            // was picked up at a different location than where it is being dropped).
            var obj = _objectProvider.GetAllObjectsAt(data.LocationId)
                                     .FirstOrDefault(o => o.Id == data.ObjectId)
                      ?? (data.Kind == ObjectInteractionKind.Drop
                             ? _objectProvider.FindObject(data.ObjectId)
                             : null);

            if (obj is null)
                return;

            // Resolve the location descriptor for policy evaluation
            var descriptor = _locations.GetDescriptor(data.LocationId);
            if (descriptor is null)
                return;

            var permission = _policy.Evaluate(ctx, obj, data.Kind, descriptor);

            if (!permission.IsAllowed)
            {
                outbox.Add(new ObjectInteractionRefused(
                    committed.OccurredAt,
                    ctx.Id,
                    data.ObjectId,
                    permission.RefusalReason ?? "Interaction not permitted.",
                    permission.IsSocial));
                return;
            }

            switch (data.Kind)
            {
                case ObjectInteractionKind.Take:
                    _objectProvider.SetHeldBy(data.LocationId, data.ObjectId, ctx.Id);
                    outbox.Add(new ObjectTaken(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId));
                    break;

                case ObjectInteractionKind.UseInPlace:
                    // Apply non-Ownership affordances; consume the object if it is a one-use type
                    AffordanceApplicationService.Apply(obj, ctx, outbox, committed.OccurredAt);
                    var wasConsumed = obj.IsPickable; // pickable single-use objects are consumed on use
                    if (wasConsumed)
                        _objectProvider.ConsumeObject(data.LocationId, data.ObjectId, committed.OccurredAt);
                    outbox.Add(new ObjectUsed(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId, wasConsumed));

                    // Log object usage with affordance summary (EventId 1500)
                    var relevantAffordances = obj.Affordances
                        .Where(a => a.Type != AffordanceType.Ownership)
                        .ToList();
                    if (relevantAffordances.Count > 0)
                    {
                        var affordanceTypes   = string.Join("+", relevantAffordances.Select(a => a.Type.ToString()));
                        var totalSatisfaction = relevantAffordances.Sum(a => a.Satisfaction);
                        using (_logger.BeginCharacterScope(ctx.Id.Value, nameof(DefaultObjectInteractionEngine)))
                            _logger.ObjectUsed(
                                ctx.Id.ToString(),
                                obj.Id,
                                obj.DisplayName,
                                data.LocationId,
                                affordanceTypes,
                                totalSatisfaction,
                                wasConsumed);
                    }
                    break;

                case ObjectInteractionKind.Drop:
                    {
                        // data.LocationId = where the character is NOW (the drop location).
                        // The object may still be cached under its original location — find it.
                        var heldObj = _objectProvider.FindObject(data.ObjectId);
                        if (heldObj is not null && heldObj.LocationId != data.LocationId)
                        {
                            // Object is moving to a new location: remove from original, add at drop location.
                            _objectProvider.RemoveObject(heldObj.LocationId, data.ObjectId);
                            _objectProvider.AddObject(heldObj with
                            {
                                LocationId = data.LocationId,
                                HeldBy = null
                            });
                        }
                        else
                        {
                            // Same location — just clear the holder.
                            _objectProvider.SetHeldBy(data.LocationId, data.ObjectId, null);
                        }
                        outbox.Add(new ObjectDropped(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId));
                        break;
                    }
            }
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // No per-tick work needed; all logic is event-driven.
        }
    }
}
