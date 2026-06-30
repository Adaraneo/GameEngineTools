// WorldBootstrap.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Attraction;
    using GameEngineTools.Characters.Engines.Reputation;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Movement;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Simulation;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using NPC = GameEngineTools.Characters.GameObjects.NPC;

    /// <summary>Everything the simulation loop needs after the world has been loaded.</summary>
    public sealed record WorldContext(
        SystemClock Clock,
        IReadOnlyList<IHuman> Characters,
        ILocationService Locations,
        DefaultSceneOrchestrator Orchestrator,
        WorldObjectSnapshotCache ObjectCache,
        WorldObjectWriteBuffer WriteBuffer,
        ObjectRespawnScheduler Respawn,
        ICognitiveResolutionLevelRuntime Lod,
        IReadOnlyList<string> KnownLocations,
        IReadOnlyList<(string From, string To, double Dist)> Connections,
        IReadOnlyDictionary<string, string> Regions);

    /// <summary>
    /// Loads WorldObserver's world (a modern-day city) from the database and places the characters.
    /// The world's locations, connections and objects come entirely from the project's own seed —
    /// <c>SourceFiles/World/SQL/seed_data.sql</c> — which the engine seeder loads as a disk override
    /// of its embedded (medieval) seed. So this bootstrap is pure wiring: no world data is built here.
    /// </summary>
    public static class WorldBootstrap
    {
        /// <summary>Modern occupations defined in the project's Occupations.csv, assigned round-robin.</summary>
        private static readonly string[] ModernOccupations =
            { "programmer", "barista", "doctor", "trainer", "shopkeeper", "chef", "librarian", "clerk", "barkeeper",
              "baker", "farmhand", "food_worker", "orchardist" };

        /// <summary>Loads the world and returns the pieces the host wires into a scene.</summary>
        public static WorldContext Build(GameEngineToolsRuntimeHandle runtime, int characterCount)
        {
            var services = runtime.Services;
            var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;
            var clock = (SystemClock)runtime.Clock;

            var locations = services.GetRequiredService<ILocationService>();
            var db = services.GetRequiredService<SqliteWorldDatabase>();
            var objectCache = services.GetRequiredService<WorldObjectSnapshotCache>();
            var writeBuffer = services.GetRequiredService<WorldObjectWriteBuffer>();
            var respawn = services.GetRequiredService<ObjectRespawnScheduler>();
            var lod = services.GetRequiredService<ICognitiveResolutionLevelRuntime>();
            var familyGraph = services.GetRequiredService<FamilyGraph>();

            // Sane era so RandomizePerson's (year - age) birth dates stay valid.
            // Start in the morning (08:00) rather than midnight, so characters are awake and going
            // about their day from the first ticks instead of all sleeping at home.
            clock.SetNow(WDateTime.New(WDateOnly.New(2025, 1, 1), WTimeOnly.New(8, 0, 0)));

            // Load the world from the (modern) seed and register all of it.
            var worldMap = SqliteWorldMapLoader.Load(db);
            worldMap.RegisterAllLocations(locations);

            var allLocations = worldMap.GetAllRegions()
                .SelectMany(worldMap.GetLocationsInRegion)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (allLocations.Count == 0)
                throw new InvalidOperationException("World seed produced no locations.");

            // Residences = Rest-type locations (the apartments in the seed). Each character gets a real
            // home there (round-robin → roommates if more people than flats), starts at home, and the
            // home is registered so the "home territory" noise/stress reduction applies.
            var homes = allLocations
                .Where(l => locations.GetDescriptor(l)?.Type == LocationType.Rest)
                .ToList();
            if (homes.Count == 0)
                homes = allLocations;

            var people = new List<IHuman>(characterCount);
            for (var i = 0; i < characterCount; i++)
            {
                var person = manager.RandomizePerson(maxAge: 40, sexBiology: null, minAge: 18);
                manager.Characters.Add(new NPC(100, person));
                familyGraph.Register(person);
                people.Add(person);

                var home = homes[i % homes.Count];
                person.SetHomeLocation(home);
                locations.MoveCharacter(person.Id, home); // start the day at home

                // Modern occupation (re-seeds the daily schedule with city work hours/locations).
                person.ChangeOccupation(ModernOccupations[i % ModernOccupations.Length]);
            }

            // ── Scene orchestrator ───────────────────────────────────────────────────
            var orchestrator = new DefaultSceneOrchestrator(
                services.GetRequiredService<IAttractionCalculator>(),
                locations,
                services.GetRequiredService<IPerceptionFidelityPolicy>(),
                new CharacterPerceptionOptions(),
                lod,
                worldMap,
                services.GetRequiredService<IMovementSpeedProvider>(),
                new Random(),
                services.GetRequiredService<ILoggerFactory>().CreateLogger<DefaultSceneOrchestrator>(),
                services.GetRequiredService<IWorldObjectProvider>(),
                // Realistic movement: MoveTo:* takes travel time (character is held in transit
                // at its origin until the trip duration elapses) instead of teleporting.
                new SceneOrchestratorOptions { EnableTravelTime = true },
                services.GetService<CommunityReputationLedger>(),
                services.GetService<GameEngineTools.Characters.Engines.Status.StatusLedger>(),
                services.GetService<GameEngineTools.World.Objects.IMutableWorldObjectProvider>());

            // Undirected connection list (the seed stores both directions) for the realistic map layout.
            var connections = db.GetAllConnections()
                .Where(c => string.CompareOrdinal(c.FromId, c.ToId) < 0)
                .Select(c => (From: c.FromId, To: c.ToId, Dist: c.DistanceMeters))
                .ToList();

            // Region per location (City / Nature) — colours the map nodes.
            var regions = allLocations.ToDictionary(id => id, id => worldMap.GetRegionOf(id) ?? "");

            return new WorldContext(
                clock, people, locations, orchestrator,
                objectCache, writeBuffer, respawn, lod, allLocations, connections, regions);
        }
    }
}
