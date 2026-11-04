// WorldSaveState.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Persistence
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>One shop price: the posted price and the learned base (price at target stock) for a shop/item kind.</summary>
    /// <param name="ShopId">The shop.</param>
    /// <param name="Kind">The item kind.</param>
    /// <param name="Price">Current posted price.</param>
    /// <param name="Base">Price at the target stock level, or <c>null</c> if not yet learned.</param>
    public sealed record PriceEntry(string ShopId, PickupItemKind Kind, double Price, double? Base);

    /// <summary>One community reputation: how a subject is regarded at a location.</summary>
    public sealed record ReputationEntry(HumanId Subject, string LocationId, double Score, double Spread, WDateTime LastUpdatedAt);

    /// <summary>One agent's emergent social standing.</summary>
    public sealed record StatusEntry(HumanId Agent, double Dominance, double Prestige, double? PreviousSalience);

    /// <summary>The social-hierarchy ledger's state.</summary>
    public sealed record StatusLedgerState(IReadOnlyList<StatusEntry> Agents, double Stability);

    /// <summary>Where a character stands (not in transit).</summary>
    public sealed record PlacementEntry(HumanId Character, string LocationId);

    /// <summary>A trip in progress.</summary>
    public sealed record TransitEntry(HumanId Character, string Origin, string Destination, WDateTime DepartAt, WDateTime ArriveAt);

    /// <summary>The scene orchestrator's per-character bookkeeping.</summary>
    public sealed record OrchestratorState(
        IReadOnlyList<HumanId> Dead,
        IReadOnlyList<HumanId> Retired,
        IReadOnlyList<HumanId> BereavementRouted,
        IReadOnlyList<HumanId> CorpseSpawned,
        IReadOnlyList<TransitEntry> Transits);

    /// <summary>
    /// Everything about the shared world that lives outside the characters' own snapshots and the SQLite object store:
    /// shop prices, reputations, social standing, who stands where, trips in progress and the orchestrator's death bookkeeping.
    /// </summary>
    /// <param name="SchemaVersion">Format version; a loader rejects a version it does not know.</param>
    /// <param name="SavedAt">World time of the save.</param>
    public sealed record WorldSaveState(
        int SchemaVersion,
        WDateTime SavedAt,
        IReadOnlyList<PriceEntry> Prices,
        IReadOnlyList<ReputationEntry> Reputations,
        StatusLedgerState? Status,
        IReadOnlyList<PlacementEntry> Placements,
        OrchestratorState? Orchestrator)
    {
        /// <summary>The version this build writes and reads.</summary>
        public const int CurrentVersion = 1;
    }
}
