// DefaultInventoryEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Inventory;

using GameEngineTools.Characters.Core;
using GameEngineTools.World.Utils.Time;

/// <summary>
/// Minimal inventory engine implementation.
/// Holds inventory state and provides hook points for future pickup/use/drop logic.
/// Full item resolution is deferred to a later development phase.
/// </summary>
public sealed class DefaultInventoryEngine : IInventoryEngine
{
    private InventoryState _state = InventoryState.Empty;

    /// <inheritdoc/>
    public InventoryState State => _state;

    /// <inheritdoc/>
    public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox) { }

    /// <inheritdoc/>
    public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox) { }

    /// <inheritdoc/>
    public void RestoreState(InventoryState state) => _state = state;
}
