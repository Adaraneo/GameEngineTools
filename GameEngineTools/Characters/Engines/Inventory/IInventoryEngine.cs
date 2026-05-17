// IInventoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Inventory;

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Utils.Time;

/// <summary>
/// Manages a character's personal inventory — pickups, usage, and dropping of items.
/// </summary>
public interface IInventoryEngine
{
    /// <summary>Current inventory state.</summary>
    InventoryState State { get; }

    /// <summary>
    /// Reacts to domain events (e.g. item picked up, item used).
    /// </summary>
    void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);

    /// <summary>
    /// Advances inventory state by one simulation tick.
    /// </summary>
    void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);

    /// <summary>
    /// Restores engine state from a previously captured snapshot.
    /// </summary>
    void RestoreState(InventoryState state);
}
