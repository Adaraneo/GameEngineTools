// DefaultObjectInteractionEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Resolves <see cref="ActionNames.InteractWithObject"/> actions committed by the Behavior engine.
    /// Evaluates the interaction policy, mutates the world object provider state, and emits the
    /// appropriate domain events for downstream engines (Memory, Psychology, etc.) to consume.
    /// </summary>
    public sealed class DefaultObjectInteractionEngine : IObjectInteractionEngine
    {
        private readonly CsvWorldObjectProvider _objectProvider;
        private readonly ILocationService _locations;
        private readonly IObjectInteractionPolicy _policy;

        public DefaultObjectInteractionEngine(
            CsvWorldObjectProvider objectProvider,
            ILocationService locations,
            IObjectInteractionPolicy policy)
        {
            _objectProvider = objectProvider;
            _locations = locations;
            _policy = policy;
        }

        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            if (@event is not ActionCommitted committed)
                return;

            if (committed.Human != ctx.Id)
                return;

            if (committed.ActionName != ActionNames.InteractWithObject)
                return;

            var data = committed.ObjectInteraction;
            if (data is null)
                return;

            // Resolve the world object — search the committed location
            var obj = _objectProvider.GetAllObjectsAt(data.LocationId)
                                     .FirstOrDefault(o => o.Id == data.ObjectId);

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
                    break;

                case ObjectInteractionKind.Drop:
                    _objectProvider.SetHeldBy(data.LocationId, data.ObjectId, null);
                    outbox.Add(new ObjectDropped(committed.OccurredAt, ctx.Id, data.ObjectId, data.LocationId));
                    break;
            }
        }

        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            // No per-tick work needed; all logic is event-driven.
        }
    }
}
