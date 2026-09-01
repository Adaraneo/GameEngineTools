// WorldContentGenerator.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using GameEngineTools.World.Objects;

namespace WorldGen.Generation;

/// <summary>
/// Populates a world.db with procedurally-placed locations, the connections between them, and a
/// small set of usable objects/affordances at each — all positioned on ground that TerraGen has
/// ALREADY generated in terrain.db (never invents new terrain itself). Independent of TerraGen's
/// own generation code — only consumes <see cref="TerrainHeightmap"/>, the shared read format.
/// </summary>
public static class WorldContentGenerator
{
    public sealed record Options(
        int Count,
        string Region = "Wilds",
        /// <summary>Minimum straight-line spacing (meters) enforced between any two generated locations.</summary>
        double MinDistanceMeters = 150.0,
        /// <summary>How many of its nearest other generated locations each location connects to.</summary>
        int ConnectionsPerLocation = 2,
        /// <summary>Elevation (meters) at/above which a placed location is classified Mountain terrain
        /// instead of Forest — same threshold TerrainEditor's RegionClassifier uses.</summary>
        double MountainThresholdMeters = 300.0,
        /// <summary>How many random candidate points to try before giving up on one location.</summary>
        int MaxPlacementAttempts = 50);

    public sealed record Result(int LocationsPlaced, int ConnectionsCreated, int ObjectsCreated);

    private sealed record Placed(string Id, double X, double Y);

    public static Result Generate(
        SqliteWorldDatabase worldDb, IReadOnlyList<TerrainHeightmap> tiles, Options options, Random rng,
        IReadOnlyList<FoodTemplate> catalog)
    {
        ArgumentNullException.ThrowIfNull(worldDb);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);
        if (tiles.Count == 0)
            throw new ArgumentException("No terrain tiles to place locations on — generate some with TerraGen first.", nameof(tiles));

        // Distinguishes ids across separate worldgen runs against the same world.db — without it,
        // a second run would pick the exact same "forest_001"/"mountain_001" ids as the first and
        // INSERT OR IGNORE would silently add nothing.
        var runToken = rng.Next(0, 0x10000).ToString("x4");

        var placed = new List<Placed>(options.Count);
        var objectsCreated = 0;
        var placedIndex = 0;

        for (var i = 0; i < options.Count; i++)
        {
            if (!TryFindCandidate(tiles, placed, options, rng, out var x, out var y, out var height))
                continue; // couldn't find a valid spot after MaxPlacementAttempts — skip this one

            placedIndex++;
            var category = height >= options.MountainThresholdMeters ? "mountain" : "forest";
            var id = $"{category}_{runToken}_{placedIndex:D3}";
            var displayName = category == "mountain"
                ? $"Horský tábor {placedIndex:D2}"
                : $"Lesní tábor {placedIndex:D2}";

            var descriptor = new LocationDescriptor(
                Id: id,
                DisplayName: displayName,
                BaseNoise: 0.10,
                NoisePerPerson: 0.03,
                Capacity: 6,
                AllowsPrivacy: false,
                Type: LocationType.Public,
                Terrain: category == "mountain" ? TerrainType.Mountain : TerrainType.Forest,
                DangerLevel: category == "mountain" ? 0.30 : 0.15,
                AllowsPickup: true,
                NormId: null,
                X: x,
                Y: y,
                AltitudeMeters: height);
            worldDb.InsertLocation(descriptor, options.Region);

            objectsCreated += AddCatalogObjects(worldDb, id, category, catalog, rng);
            placed.Add(new Placed(id, x, y));
        }

        var connectionsCreated = ConnectNearestNeighbors(worldDb, placed, options.ConnectionsPerLocation);

        return new Result(placed.Count, connectionsCreated, objectsCreated);
    }

    private static bool TryFindCandidate(
        IReadOnlyList<TerrainHeightmap> tiles, List<Placed> placed, Options options, Random rng,
        out double x, out double y, out double height)
    {
        for (var attempt = 0; attempt < options.MaxPlacementAttempts; attempt++)
        {
            var tile = tiles[rng.Next(tiles.Count)];
            var candidateX = tile.OriginX + rng.NextDouble() * tile.Width * tile.CellSizeMeters;
            var candidateY = tile.OriginY + rng.NextDouble() * tile.Height * tile.CellSizeMeters;
            var candidateHeight = tile.SampleAt(candidateX, candidateY);

            if (candidateHeight < 0.0) continue; // underwater — don't place a camp in the sea

            var tooClose = false;
            foreach (var p in placed)
            {
                var dx = p.X - candidateX;
                var dy = p.Y - candidateY;
                if (dx * dx + dy * dy < options.MinDistanceMeters * options.MinDistanceMeters) { tooClose = true; break; }
            }
            if (tooClose) continue;

            x = candidateX;
            y = candidateY;
            height = candidateHeight;
            return true;
        }

        x = y = height = 0;
        return false;
    }

    /// <summary>Connects each placed location to its <see cref="Options.ConnectionsPerLocation"/>
    /// nearest OTHER placed locations, both directions, with the real straight-line distance.
    /// Doesn't touch any pre-existing locations already in world.db.</summary>
    private static int ConnectNearestNeighbors(SqliteWorldDatabase worldDb, List<Placed> placed, int connectionsPerLocation)
    {
        var madePairs = new HashSet<string>();
        var created = 0;

        foreach (var from in placed)
        {
            var nearest = placed
                .Where(p => p.Id != from.Id)
                .OrderBy(p => DistanceSquared(from, p))
                .Take(Math.Max(0, connectionsPerLocation));

            foreach (var to in nearest)
            {
                var pairKey = string.CompareOrdinal(from.Id, to.Id) <= 0 ? $"{from.Id}|{to.Id}" : $"{to.Id}|{from.Id}";
                if (!madePairs.Add(pairKey)) continue;

                var distance = Math.Sqrt(DistanceSquared(from, to));
                worldDb.InsertConnection(from.Id, to.Id, distance);
                worldDb.InsertConnection(to.Id, from.Id, distance);
                created++;
            }
        }

        return created;
    }

    private static double DistanceSquared(Placed a, Placed b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Adds one object per need (Hunger/Thirst/Rest) so a procedurally-placed location
    /// isn't a dead end for NPC need-satisfaction — same role CastleVillageSeed's hand-authored
    /// objects play for the Castle/Village content. Which object gets used is picked at random
    /// from <see cref="FoodTemplate"/> rows whose <see cref="FoodTemplate.Biome"/> matches this
    /// location's classification (or is <c>"Any"</c>) — a need with no matching template in the
    /// catalog is simply skipped, not an error.</summary>
    private static int AddCatalogObjects(
        SqliteWorldDatabase worldDb, string locationId, string biome,
        IReadOnlyList<FoodTemplate> catalog, Random rng)
    {
        var created = 0;

        foreach (var need in new[] { AffordanceType.Hunger, AffordanceType.Thirst, AffordanceType.Rest })
        {
            var candidates = catalog
                .Where(t => t.AffordanceType == need &&
                    (string.Equals(t.Biome, biome, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Biome, "Any", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0) continue;

            var template = candidates[rng.Next(candidates.Count)];
            worldDb.AddObject(new WorldObject
            {
                Category = CategoryFor(template.AffordanceType),
                Id = $"{locationId}_{template.TemplateId}_01",
                DisplayName = template.DisplayName,
                LocationId = locationId,
                IsAvailable = true,
                IsPickable = template.Pickable,
                ItemKind = template.ItemKind,
                Respawns = template.RespawnMinutes > 0,
                RespawnMinutes = template.RespawnMinutes,
                WeightGrams = template.WeightGrams,
                Affordances = [new WorldObjectAffordance(template.AffordanceType, template.Satisfaction)],
                NutritionalProfile = template.AffordanceType is AffordanceType.Hunger or AffordanceType.Thirst
                    ? new NutritionalProfile(
                        template.CalorieGain, template.ProteinGain, template.IronGain,
                        template.VitaminDGain, template.HydrationGain,
                        template.HemeIronFraction, template.VitaminCMilligrams)
                    : null,
            });
            created++;
        }

        return created;
    }

    private static WorldObjectCategory CategoryFor(AffordanceType affordanceType) => affordanceType switch
    {
        AffordanceType.Hunger => WorldObjectCategory.Food,
        AffordanceType.Thirst => WorldObjectCategory.Drink,
        AffordanceType.Rest => WorldObjectCategory.Shelter,
        _ => WorldObjectCategory.Ambient,
    };
}
