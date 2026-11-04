// WorldStateStore.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Persistence
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.Characters.Engines.Status;
    using GameEngineTools.World.Economy;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;

    /// <summary>Captures, applies, serialises and atomically writes a <see cref="WorldSaveState"/>.</summary>
    public static class WorldStateStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        /// <summary>Gathers the shared world state from its owners; any owner may be <c>null</c> when that subsystem is not in use.</summary>
        public static WorldSaveState Capture(
            WDateTime now,
            EconomyLedger? economy,
            CommunityReputationLedger? reputation,
            StatusLedger? status,
            DefaultLocationService? locations,
            DefaultSceneOrchestrator? orchestrator)
            => new(
                WorldSaveState.CurrentVersion,
                now,
                economy?.Snapshot() ?? Array.Empty<PriceEntry>(),
                reputation?.Snapshot() ?? Array.Empty<ReputationEntry>(),
                status?.Snapshot(),
                locations?.SnapshotPlacements() ?? Array.Empty<PlacementEntry>(),
                orchestrator?.CaptureState());

        /// <summary>Puts a saved state back into its owners; owners that are <c>null</c> are skipped.</summary>
        public static void Apply(
            WorldSaveState state,
            EconomyLedger? economy,
            CommunityReputationLedger? reputation,
            StatusLedger? status,
            DefaultLocationService? locations,
            DefaultSceneOrchestrator? orchestrator)
        {
            ArgumentNullException.ThrowIfNull(state);
            EnsureSupported(state.SchemaVersion);

            economy?.Restore(state.Prices);
            reputation?.Restore(state.Reputations);
            if (status is not null)
            {
                if (state.Status is { } statusState)
                    status.Restore(statusState);
                else
                    status.Clear();
            }

            locations?.RestorePlacements(state.Placements);
            if (orchestrator is not null && state.Orchestrator is { } orchestratorState)
                orchestrator.RestoreState(orchestratorState);
        }

        /// <summary>Serialises a state to indented JSON.</summary>
        public static string ToJson(WorldSaveState state)
            => JsonSerializer.Serialize(state, Options);

        /// <summary>Parses a state from JSON and rejects a schema version this build does not know.</summary>
        /// <exception cref="InvalidDataException">The JSON is empty/invalid or its version is unsupported.</exception>
        public static WorldSaveState FromJson(string json)
        {
            var state = JsonSerializer.Deserialize<WorldSaveState>(json, Options)
                        ?? throw new InvalidDataException("World save is empty.");
            EnsureSupported(state.SchemaVersion);
            return state;
        }

        /// <summary>Writes to a temp file next to <paramref name="path"/> and renames it over, so a crash never leaves a half-written save.</summary>
        public static void Save(string path, WorldSaveState state)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var temp = path + ".tmp";
            File.WriteAllText(temp, ToJson(state));
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Reads a state written by <see cref="Save"/>.</summary>
        public static WorldSaveState Load(string path)
            => FromJson(File.ReadAllText(path));

        private static void EnsureSupported(int version)
        {
            if (version != WorldSaveState.CurrentVersion)
                throw new InvalidDataException($"Unsupported world save version {version} (this build reads {WorldSaveState.CurrentVersion}).");
        }
    }
}
