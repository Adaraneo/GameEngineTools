// WorldContentGenerator.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.World.Data;
using GameEngineTools.World.Location;
using GameEngineTools.World.Objects;
using GameEngineTools.World.Objects.Production;

namespace WorldGen.Generation;

/// <summary>
/// Populates a world.db with procedurally-placed locations, the connections between them, and a
/// small set of usable objects/affordances at each — all positioned on ground that TerraGen has
/// ALREADY generated in terrain.db (never invents new terrain itself). Independent of TerraGen's
/// own generation code — only consumes <see cref="TerrainHeightmap"/>, the shared read format
/// (plus independent ports of TerraGen's <see cref="TectonicPlates"/> and
/// <see cref="RoadPathfinder"/> — see those files for why).
/// </summary>
public static class WorldContentGenerator
{
    /// <summary>How developed a placed location is — drives capacity, object density, and how
    /// aggressively it gets linked into the road network. Picked per-location by
    /// <see cref="PickTier"/>, weighted by the location's <see cref="TerrainType"/> biome (a
    /// mountain candidate is far more likely to end up a lone camp than a town).</summary>
    public enum SettlementTier { Camp, Village, Town }

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
        int MaxPlacementAttempts = 50,
        /// <summary>A below-mountain candidate within this distance (meters) of any underwater
        /// sample is classified Coastline instead of Plains/Forest.</summary>
        double CoastRadiusMeters = 60.0,
        /// <summary>Local slope (rise/run, dimensionless) at/below which a below-mountain,
        /// non-coastal candidate is classified Plains instead of Forest.</summary>
        double PlainsSlopeThreshold = 0.03,
        /// <summary>Tectonic plate count for weighting danger near plate boundaries — 0 (default)
        /// disables tectonic weighting entirely. Pass the SAME seed/count/radius TerraGen used to
        /// generate this terrain (see <see cref="WorldGen.PlanetSettings"/>, which Program.cs
        /// reads automatically from the same appsettings.World.json TerraGen reads), or the
        /// boundary classification is meaningless noise against the wrong plate layout.</summary>
        int TectonicPlateCount = 0,
        int TectonicSeed = 0,
        double PlanetRadiusMeters = 6_378_100.0,
        /// <summary>Temperature at the equator at sea level, in °C — the warm end of the
        /// latitude gradient <see cref="ClimateModel"/> uses for classification.</summary>
        double EquatorTemperatureCelsius = 27.0,
        /// <summary>Temperature at the poles at sea level, in °C — the cold end of the same
        /// gradient.</summary>
        double PoleTemperatureCelsius = -25.0,
        /// <summary>Temperature drop per kilometer of altitude, in °C/km — the standard
        /// environmental lapse rate (~6.5) unless overridden.</summary>
        double LapseRateCPerKm = 6.5,
        /// <summary>Seed for the independent humidity noise field — 0 (default) reuses whatever
        /// value the caller passes (WorldGen's CLI reuses the planet's own tectonic seed so a
        /// climate map stays reproducible per-planet without a separate flag).</summary>
        int ClimateSeed = 0,
        /// <summary>Wavelength (meters) of the large-scale humidity noise field — real climate
        /// zones span hundreds of kilometers, so this defaults far larger than terrain-noise
        /// wavelengths.</summary>
        double HumidityWavelengthMeters = 500_000.0,
        /// <summary>How much humidity drops per kilometer of altitude — higher ground holds less
        /// moisture (a coarse stand-in for orographic drying, without modeling prevailing wind or
        /// rain shadows).</summary>
        double AltitudeDrynessPerKm = 0.05,
        /// <summary>Temperature (°C) at/below which a candidate (that isn't already Mountain) is
        /// classified Tundra regardless of humidity, slope, or coastal proximity.</summary>
        double TundraTemperatureThresholdC = -5.0,
        /// <summary>Humidity at/below which a hot, non-coastal, non-tundra candidate classifies
        /// Desert instead of continuing through the Plains/Forest/Savanna checks.</summary>
        double DesertHumidityThreshold = 0.2,
        /// <summary>Minimum temperature (°C) for the Desert check above to apply.</summary>
        double DesertTemperatureThresholdC = 24.0,
        /// <summary>Humidity at/above which a hot, non-coastal, non-tundra, non-desert candidate
        /// classifies Jungle instead of continuing through the Plains/Forest/Savanna checks.</summary>
        double JungleHumidityThreshold = 0.8,
        /// <summary>Minimum temperature (°C) for the Jungle check above to apply.</summary>
        double JungleTemperatureThresholdC = 22.0,
        /// <summary>Humidity at/below which a flat, warm candidate (that already passed the
        /// Desert/Jungle checks) classifies Savanna instead of Plains.</summary>
        double SavannaHumidityThreshold = 0.4,
        /// <summary>Minimum temperature (°C) for the Savanna check above to apply.</summary>
        double SavannaTemperatureThresholdC = 18.0,
        /// <summary>Skips <see cref="PickTier"/>'s per-biome weighted roll and always uses this
        /// tier instead — mainly for deterministic tests; leave <c>null</c> for real generation.</summary>
        SettlementTier? ForcedTier = null,
        /// <summary>Whether Village/Town-tier settlements get <see cref="LocationType.Rest"/>
        /// house sub-locations laid out along radial streets (see <see cref="AddHouses"/>'s own
        /// remarks for the layout grammar; count via <see cref="HousesPerVillage"/>/<see cref="HousesPerTown"/>)
        /// — GameSandbox uses these for character home assignment. Opt-in at this library level
        /// (existing callers/tests expect exactly the locations they asked for); WorldGen's own
        /// CLI (<c>WorldGen/Program.cs</c>) turns this on by default for real runs.</summary>
        bool GenerateHouses = false,
        int HousesPerVillage = 4,
        int HousesPerTown = 10,
        /// <summary>Whether a single cemetery location is created at a deterministic id
        /// (<c>"{Region}_cemetery"</c>) so callers can wire it into
        /// <c>SceneOrchestratorOptions.CemeteryLocationId</c> without inspecting <see cref="Result"/>.
        /// Opt-in here for the same reason as <see cref="GenerateHouses"/>.</summary>
        bool GenerateCemetery = false,
        /// <summary>Whether a field→mill→bakery production-fixture chain (see
        /// <see cref="ProductionSiteFactory"/>) is attached to the largest settlement placed this
        /// run. Opt-in here for the same reason as <see cref="GenerateHouses"/>.</summary>
        bool GenerateProductionChain = false);

    public sealed record Result(int LocationsPlaced, int ConnectionsCreated, int ObjectsCreated, string? CemeteryLocationId = null);

    /// <summary>Internal (not private) so WorldGenTests can hand-build a <see cref="List{Placed}"/>
    /// and call <see cref="ConnectNearestNeighbors"/> directly — deterministic road-network testing
    /// without needing a full noise-driven <see cref="Generate"/> run to happen to produce a
    /// particular tier mix.</summary>
    internal sealed record Placed(string Id, double X, double Y, TerrainHeightmap Tile, SettlementTier Tier);

    private static readonly AffordanceType[] BaseNeeds =
        [AffordanceType.Hunger, AffordanceType.Thirst, AffordanceType.Rest];

    /// <summary>Nominative-singular adjective forms agreeing with each tier noun's grammatical
    /// gender (tábor = masc., vesnice = fem., město = neut.) — <c>-ní</c> adjectives (Forest,
    /// Coastline) are gender-invariant so all three columns repeat the same word.</summary>
    private static readonly Dictionary<TerrainType, (string Masc, string Fem, string Neut)> BiomeAdjectives = new()
    {
        [TerrainType.Mountain] = ("Horský", "Horská", "Horské"),
        [TerrainType.Forest] = ("Lesní", "Lesní", "Lesní"),
        [TerrainType.Plains] = ("Planinský", "Planinská", "Planinské"),
        [TerrainType.Coastline] = ("Pobřežní", "Pobřežní", "Pobřežní"),
        [TerrainType.Desert] = ("Pouštní", "Pouštní", "Pouštní"),
        [TerrainType.Tundra] = ("Tundrový", "Tundrová", "Tundrové"),
        [TerrainType.Savanna] = ("Savanový", "Savanová", "Savanové"),
        [TerrainType.Jungle] = ("Džunglový", "Džunglová", "Džunglové"),
    };

    private static readonly Dictionary<SettlementTier, string> TierNoun = new()
    {
        [SettlementTier.Camp] = "tábor",
        [SettlementTier.Village] = "vesnice",
        [SettlementTier.Town] = "město",
    };

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

        // Pure function of (seed, count) — built once per run and reused for every candidate's
        // boundary sample, the same convention TerraGen's own TileGenerator follows.
        var plates = options.TectonicPlateCount > 0
            ? TectonicPlates.Generate(options.TectonicSeed, options.TectonicPlateCount)
            : null;

        // Distinguishes ids across separate worldgen runs against the same world.db — without it,
        // a second run would pick the exact same "forest_001"/"mountain_001" ids as the first and
        // INSERT OR IGNORE would silently add nothing.
        var runToken = rng.Next(0, 0x10000).ToString("x4");

        var placed = new List<Placed>(options.Count);
        var objectsCreated = 0;
        var placedIndex = 0;

        for (var i = 0; i < options.Count; i++)
        {
            if (!TryFindCandidate(tiles, placed, options, rng, out var x, out var y, out var height, out var tile))
                continue; // couldn't find a valid spot after MaxPlacementAttempts — skip this one

            placedIndex++;
            var climate = ClimateModel.At(x, y, height, options);
            var biome = ClassifyBiome(tiles, tile, x, y, height, climate, options);
            var tier = options.ForcedTier ?? PickTier(biome, rng);
            var categoryPrefix = biome.ToString().ToLowerInvariant();
            var id = $"{categoryPrefix}_{runToken}_{placedIndex:D3}";
            var displayName = $"{DisplayName(biome, tier)} {placedIndex:D2}";

            var (capacity, allowsPrivacy, baseNoise, noisePerPerson) = TierProfile(tier);
            var dangerLevel = BaseDangerLevel(biome) + TectonicDangerBonus(plates, x, y, options.PlanetRadiusMeters);

            var descriptor = new LocationDescriptor(
                Id: id,
                DisplayName: displayName,
                BaseNoise: baseNoise,
                NoisePerPerson: noisePerPerson,
                Capacity: capacity,
                AllowsPrivacy: allowsPrivacy,
                Type: LocationType.Public,
                Terrain: biome,
                DangerLevel: Math.Clamp(dangerLevel, 0.0, 1.0),
                AllowsPickup: true,
                NormId: null,
                X: x,
                Y: y,
                AltitudeMeters: height,
                TemperatureCelsius: climate.TemperatureCelsius,
                Humidity: climate.Humidity);
            worldDb.InsertLocation(descriptor, options.Region);

            objectsCreated += AddCatalogObjects(worldDb, id, biome, tier, catalog, rng);
            placed.Add(new Placed(id, x, y, tile, tier));
        }

        var connectionsCreated = ConnectNearestNeighbors(worldDb, placed, options);

        if (options.GenerateHouses)
            connectionsCreated += AddHouses(worldDb, placed, options, rng);

        string? cemeteryLocationId = null;
        if (options.GenerateCemetery)
        {
            var (id, cemeteryConnections) = AddCemetery(worldDb, placed, options, rng);
            cemeteryLocationId = id;
            connectionsCreated += cemeteryConnections;
        }

        if (options.GenerateProductionChain)
            objectsCreated += AddProductionChain(worldDb, placed);

        return new Result(placed.Count, connectionsCreated, objectsCreated, cemeteryLocationId);
    }

    private static bool TryFindCandidate(
        IReadOnlyList<TerrainHeightmap> tiles, List<Placed> placed, Options options, Random rng,
        out double x, out double y, out double height, out TerrainHeightmap tile)
    {
        for (var attempt = 0; attempt < options.MaxPlacementAttempts; attempt++)
        {
            var candidateTile = tiles[rng.Next(tiles.Count)];
            var candidateX = candidateTile.OriginX + rng.NextDouble() * candidateTile.Width * candidateTile.CellSizeMeters;
            var candidateY = candidateTile.OriginY + rng.NextDouble() * candidateTile.Height * candidateTile.CellSizeMeters;
            var candidateHeight = candidateTile.SampleAt(candidateX, candidateY);

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
            tile = candidateTile;
            return true;
        }

        x = y = height = 0;
        tile = tiles[0];
        return false;
    }

    /// <summary>Classifies a candidate's biome: Mountain by raw elevation (unchanged, existing
    /// threshold) always wins first; then Tundra by low temperature (cold overrides everything
    /// else — a frozen coastline is still Tundra, not Coastline); then Coastline when land within
    /// <see cref="Options.CoastRadiusMeters"/> touches water; then, among the remaining warmer
    /// inland candidates, Desert (hot+dry) or Jungle (hot+wet) by <paramref name="climate"/>
    /// regardless of slope; finally, for the flat remainder, Savanna (warm+seasonally-dry) or
    /// Plains, and for the sloped remainder, Forest — same fallback the pre-climate classifier
    /// used.</summary>
    private static TerrainType ClassifyBiome(
        IReadOnlyList<TerrainHeightmap> tiles, TerrainHeightmap tile, double x, double y, double height,
        ClimateModel.Sample climate, Options options)
    {
        if (height >= options.MountainThresholdMeters) return TerrainType.Mountain;
        if (climate.TemperatureCelsius <= options.TundraTemperatureThresholdC) return TerrainType.Tundra;
        if (IsNearWater(tiles, tile, x, y, options.CoastRadiusMeters)) return TerrainType.Coastline;

        if (climate.Humidity <= options.DesertHumidityThreshold && climate.TemperatureCelsius >= options.DesertTemperatureThresholdC)
            return TerrainType.Desert;
        if (climate.Humidity >= options.JungleHumidityThreshold && climate.TemperatureCelsius >= options.JungleTemperatureThresholdC)
            return TerrainType.Jungle;

        if (EstimateSlope(tile, x, y) > options.PlainsSlopeThreshold) return TerrainType.Forest;

        return climate.Humidity <= options.SavannaHumidityThreshold && climate.TemperatureCelsius >= options.SavannaTemperatureThresholdC
            ? TerrainType.Savanna
            : TerrainType.Plains;
    }

    /// <summary>Finite-difference gradient magnitude (rise/run) at (x,y), sampled one cell-width
    /// out in each axis.</summary>
    private static double EstimateSlope(TerrainHeightmap tile, double x, double y)
    {
        var d = tile.CellSizeMeters;
        var gx = (tile.SampleAt(x + d, y) - tile.SampleAt(x - d, y)) / (2 * d);
        var gy = (tile.SampleAt(x, y + d) - tile.SampleAt(x, y - d)) / (2 * d);
        return Math.Sqrt(gx * gx + gy * gy);
    }

    /// <summary>Casts a ring of sample rays out to <paramref name="radiusMeters"/> looking for any
    /// underwater elevation — samples against WHICHEVER tile in <paramref name="tiles"/> actually
    /// contains each ray point (falling back to <paramref name="fallbackTile"/>, which clamps to
    /// its own edge) so a candidate near a tile border still sees its true neighbor, not a
    /// repeated edge value.</summary>
    private static bool IsNearWater(IReadOnlyList<TerrainHeightmap> tiles, TerrainHeightmap fallbackTile, double x, double y, double radiusMeters)
    {
        const int rays = 8;
        for (var i = 0; i < rays; i++)
        {
            var angle = i * (2 * Math.PI / rays);
            var sx = x + Math.Cos(angle) * radiusMeters;
            var sy = y + Math.Sin(angle) * radiusMeters;
            if (SampleHeightAt(tiles, fallbackTile, sx, sy) < 0.0) return true;
        }
        return false;
    }

    private static double SampleHeightAt(IReadOnlyList<TerrainHeightmap> tiles, TerrainHeightmap fallbackTile, double x, double y)
    {
        foreach (var t in tiles)
        {
            if (x >= t.OriginX && x <= t.OriginX + t.Width * t.CellSizeMeters &&
                y >= t.OriginY && y <= t.OriginY + t.Height * t.CellSizeMeters)
                return t.SampleAt(x, y);
        }
        return fallbackTile.SampleAt(x, y);
    }

    /// <summary>Weighted random tier by biome — mountains are overwhelmingly lone camps, while
    /// flat/coastal land (the historically real siting for towns: flat building ground plus water
    /// access) is far likelier to hold a village or town.</summary>
    private static SettlementTier PickTier(TerrainType biome, Random rng)
    {
        var (campWeight, villageWeight, townWeight) = biome switch
        {
            TerrainType.Mountain => (0.75, 0.22, 0.03),
            TerrainType.Coastline => (0.20, 0.45, 0.35),
            TerrainType.Plains => (0.20, 0.50, 0.30),
            TerrainType.Savanna => (0.25, 0.48, 0.27), // flat, buildable — nearly as settleable as Plains
            TerrainType.Desert => (0.65, 0.28, 0.07), // harsh, water-scarce — mostly lone camps
            TerrainType.Tundra => (0.70, 0.25, 0.05), // cold, short growing season — rarely a town
            TerrainType.Jungle => (0.60, 0.32, 0.08), // dense, disease-prone — hard to grow into a town
            _ => (0.55, 0.38, 0.07), // Forest
        };

        var roll = rng.NextDouble() * (campWeight + villageWeight + townWeight);
        if (roll < campWeight) return SettlementTier.Camp;
        return roll < campWeight + villageWeight ? SettlementTier.Village : SettlementTier.Town;
    }

    private static string DisplayName(TerrainType biome, SettlementTier tier)
    {
        var adjectives = BiomeAdjectives[biome];
        var adjective = tier switch
        {
            SettlementTier.Camp => adjectives.Masc,
            SettlementTier.Village => adjectives.Fem,
            SettlementTier.Town => adjectives.Neut,
            _ => adjectives.Masc,
        };
        return $"{adjective} {TierNoun[tier]}";
    }

    private static (int Capacity, bool AllowsPrivacy, double BaseNoise, double NoisePerPerson) TierProfile(SettlementTier tier) => tier switch
    {
        SettlementTier.Camp => (6, false, 0.10, 0.03),
        SettlementTier.Village => (18, true, 0.18, 0.025),
        SettlementTier.Town => (40, true, 0.30, 0.02),
        _ => (6, false, 0.10, 0.03),
    };

    private static double BaseDangerLevel(TerrainType biome) => biome switch
    {
        TerrainType.Mountain => 0.30,
        TerrainType.Coastline => 0.12,
        TerrainType.Plains => 0.08,
        TerrainType.Savanna => 0.10,
        TerrainType.Desert => 0.20, // dehydration/exposure risk
        TerrainType.Tundra => 0.25, // cold-exposure risk
        TerrainType.Jungle => 0.28, // dense terrain, disease, wildlife
        _ => 0.15, // Forest
    };

    /// <summary>Extra danger near a convergent (collision — earthquakes/volcanism) or divergent
    /// (rift) plate boundary. Cubed influence to match the same "band, not a gradient spanning
    /// half the plate" shaping TerraGen's own PlanetNoise applies to boundary uplift.</summary>
    private static double TectonicDangerBonus(TectonicPlates.Plate[]? plates, double x, double y, double planetRadiusMeters)
    {
        if (plates is null) return 0.0;

        var (lat, lon) = PlanetGeometry.OffsetToLatLon(x, y, planetRadiusMeters);
        var (ux, uy, uz) = PlanetGeometry.LatLonToUnitVector(lat, lon);
        var sample = TectonicPlates.Sample(plates, ux, uy, uz);
        var belt = Math.Pow(sample.BoundaryInfluence, 3.0);

        return sample.Boundary switch
        {
            TectonicPlates.BoundaryType.Convergent => belt * 0.25,
            TectonicPlates.BoundaryType.Divergent => belt * 0.15,
            _ => 0.0,
        };
    }

    /// <summary>Builds the road network as a small settlement-hierarchy graph grammar rather than
    /// pure nearest-neighbor: (1) Towns form a backbone via minimum spanning tree — the fewest
    /// edges that still guarantee every Town reaches every other one, instead of a "each connects
    /// to its own nearest Town" star that could leave two Towns stranded from each other if their
    /// mutual nearest choices don't happen to chain up; (2) each Village joins the network through
    /// its single nearest Town (its regional hub); (3) each Camp joins through whichever is
    /// nearer, a Village or a Town — a remote camp shouldn't have to route all the way to a town
    /// when a closer village exists; (4) every location ALSO gets up to
    /// <see cref="Options.ConnectionsPerLocation"/> short lateral links to its nearest SAME-tier
    /// peers, so local clusters (camp-to-camp, village-to-village) aren't forced to route
    /// everything through a hub. All distances use a terrain-aware walking cost (see
    /// <see cref="RoadDistance"/>) rather than a straight line. Doesn't touch any pre-existing
    /// locations already in world.db.</summary>
    internal static int ConnectNearestNeighbors(SqliteWorldDatabase worldDb, List<Placed> placed, Options options)
    {
        var madePairs = new HashSet<string>();
        var created = 0;

        void Connect(Placed from, Placed to)
        {
            var pairKey = string.CompareOrdinal(from.Id, to.Id) <= 0 ? $"{from.Id}|{to.Id}" : $"{to.Id}|{from.Id}";
            if (!madePairs.Add(pairKey)) return;

            var distance = RoadDistance(from, to);
            worldDb.InsertConnection(from.Id, to.Id, distance);
            worldDb.InsertConnection(to.Id, from.Id, distance);
            created++;
        }

        if (options.ConnectionsPerLocation > 0)
        {
            var towns = placed.Where(p => p.Tier == SettlementTier.Town).ToList();
            var villages = placed.Where(p => p.Tier == SettlementTier.Village).ToList();
            var camps = placed.Where(p => p.Tier == SettlementTier.Camp).ToList();

            ConnectMinimumSpanningTree(towns, Connect);

            foreach (var village in villages)
            {
                var hub = NearestOf(village, towns);
                if (hub is not null) Connect(village, hub);
            }

            var villagesAndTowns = villages.Concat(towns).ToList();
            foreach (var camp in camps)
            {
                var hub = NearestOf(camp, villagesAndTowns);
                if (hub is not null) Connect(camp, hub);
            }
        }

        foreach (var tierGroup in new[]
                 {
                     placed.Where(p => p.Tier == SettlementTier.Town).ToList(),
                     placed.Where(p => p.Tier == SettlementTier.Village).ToList(),
                     placed.Where(p => p.Tier == SettlementTier.Camp).ToList(),
                 })
        {
            foreach (var from in tierGroup)
            {
                var nearestSameTier = tierGroup
                    .Where(p => p.Id != from.Id)
                    .OrderBy(p => DistanceSquared(from, p))
                    .Take(Math.Max(0, options.ConnectionsPerLocation));
                foreach (var to in nearestSameTier) Connect(from, to);
            }
        }

        return created;
    }

    /// <summary>Prim's algorithm over straight-line distance (the actual stored connection
    /// distance still goes through <paramref name="connect"/>'s own <see cref="RoadDistance"/>
    /// call) — <paramref name="nodes"/> is expected to be small (the Town-tier count for one run),
    /// so the O(n³) worst case here is never actually a concern in practice.</summary>
    private static void ConnectMinimumSpanningTree(List<Placed> nodes, Action<Placed, Placed> connect)
    {
        if (nodes.Count < 2) return;

        var inTree = new bool[nodes.Count];
        inTree[0] = true;
        var remaining = nodes.Count - 1;

        while (remaining > 0)
        {
            var bestFrom = -1;
            var bestTo = -1;
            var bestDistance = double.MaxValue;

            for (var i = 0; i < nodes.Count; i++)
            {
                if (!inTree[i]) continue;
                for (var j = 0; j < nodes.Count; j++)
                {
                    if (inTree[j]) continue;
                    var distance = DistanceSquared(nodes[i], nodes[j]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFrom = i;
                        bestTo = j;
                    }
                }
            }

            if (bestTo < 0) break; // unreachable given the loop invariant, but never hang over it
            connect(nodes[bestFrom], nodes[bestTo]);
            inTree[bestTo] = true;
            remaining--;
        }
    }

    private static Placed? NearestOf(Placed from, List<Placed> candidates) =>
        candidates.Where(p => p.Id != from.Id).OrderBy(p => DistanceSquared(from, p)).FirstOrDefault();

    /// <summary>Terrain-aware walking distance via <see cref="RoadPathfinder"/> when both
    /// locations share the same tile (the pathfinder only ever sees one tile's own grid); a
    /// straight line otherwise — WorldGen doesn't stitch tiles into one grid for pathfinding, so a
    /// cross-tile road cost is necessarily an approximation.</summary>
    private static double RoadDistance(Placed from, Placed to)
    {
        if (ReferenceEquals(from.Tile, to.Tile))
        {
            var path = RoadPathfinder.FindPath(from.Tile, from.X, from.Y, to.X, to.Y);
            if (path is not null) return path.LengthMeters;
        }
        return Math.Sqrt(DistanceSquared(from, to));
    }

    private static double DistanceSquared(Placed a, Placed b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Radial streets a settlement's houses are laid out along, by tier — Village gets a
    /// handful, Town more (Camp is gated out entirely by <see cref="AddHouses"/>'s own tier
    /// switch, same as before this existed).</summary>
    private static int StreetCountForTier(SettlementTier tier) => tier switch
    {
        SettlementTier.Village => 3,
        SettlementTier.Town => 5,
        _ => 1,
    };

    /// <summary>Adds <see cref="Options.HousesPerVillage"/>/<see cref="Options.HousesPerTown"/>
    /// <see cref="LocationType.Rest"/> sub-locations around each placed Village/Town settlement
    /// (Camp tier is too small/transient to bother) via a small settlement-layout grammar —
    /// SETTLEMENT → SQUARE? STREET+, STREET → HOUSE+ — instead of the old pure-random circular
    /// scatter: <see cref="StreetCountForTier"/> evenly-spaced radial streets (with a little angle
    /// jitter, so they don't read as robotic spokes), each carrying a CHAIN of houses at
    /// increasing distance — a house connects to the next one down its own street (not straight
    /// back to the settlement), the same "walk down the street, not teleport to the town hall"
    /// topology a real settlement has. Town tier additionally gets one Social "square" sub-location
    /// (see <see cref="AddTownSquare"/>) that the streets radiate from instead of the settlement's
    /// own raw point. Returns the number of connections created.</summary>
    private static int AddHouses(SqliteWorldDatabase worldDb, List<Placed> placed, Options options, Random rng)
    {
        var created = 0;
        foreach (var parent in placed)
        {
            var houseCount = parent.Tier switch
            {
                SettlementTier.Village => options.HousesPerVillage,
                SettlementTier.Town => options.HousesPerTown,
                _ => 0,
            };
            if (houseCount <= 0) continue;

            var (hubId, hubX, hubY) = parent.Tier == SettlementTier.Town
                ? AddTownSquare(worldDb, parent, options, ref created)
                : (parent.Id, parent.X, parent.Y);

            var streetCount = StreetCountForTier(parent.Tier);
            var streetAngleStep = 2 * Math.PI / streetCount;
            var houseIndex = 0;

            for (var streetNum = 0; streetNum < streetCount; streetNum++)
            {
                var streetAngle = streetNum * streetAngleStep + (rng.NextDouble() - 0.5) * 0.3;
                var housesOnThisStreet = houseCount / streetCount + (streetNum < houseCount % streetCount ? 1 : 0);

                var backId = hubId;
                var backX = hubX;
                var backY = hubY;

                for (var alongStreet = 1; alongStreet <= housesOnThisStreet; alongStreet++)
                {
                    houseIndex++;
                    var radius = 15.0 * alongStreet + rng.NextDouble() * 8.0;
                    var lateralJitter = (rng.NextDouble() - 0.5) * 6.0; // small perpendicular offset — a street isn't a ruler-straight line
                    var perpAngle = streetAngle + Math.PI / 2;
                    var hx = hubX + Math.Cos(streetAngle) * radius + Math.Cos(perpAngle) * lateralJitter;
                    var hy = hubY + Math.Sin(streetAngle) * radius + Math.Sin(perpAngle) * lateralJitter;
                    var houseId = $"{parent.Id}_house_{houseIndex:D2}";

                    var descriptor = new LocationDescriptor(
                        Id: houseId,
                        DisplayName: $"Dům {houseIndex:D2}",
                        BaseNoise: 0.05,
                        NoisePerPerson: 0.03,
                        Capacity: 3,
                        AllowsPrivacy: true,
                        Type: LocationType.Rest,
                        Terrain: TerrainType.Indoor,
                        DangerLevel: 0.0,
                        AllowsPickup: true,
                        NormId: null,
                        X: hx,
                        Y: hy,
                        AltitudeMeters: parent.Tile.SampleAt(hx, hy));
                    worldDb.InsertLocation(descriptor, options.Region);

                    var dx = hx - backX;
                    var dy = hy - backY;
                    var distance = Math.Sqrt(dx * dx + dy * dy);
                    worldDb.InsertConnection(houseId, backId, distance);
                    worldDb.InsertConnection(backId, houseId, distance);
                    created++;

                    backId = houseId;
                    backX = hx;
                    backY = hy;
                }
            }
        }
        return created;
    }

    /// <summary>Creates the one <see cref="LocationType.Social"/> "square" sub-location a Town
    /// gets (a real gathering place slightly off the settlement's own abstract point, not layered
    /// directly on top of it), connects it back to the settlement, and increments
    /// <paramref name="connectionsCreated"/> for that one edge. Returns where the settlement's
    /// streets should radiate FROM (the square's position), not the settlement's own X/Y.</summary>
    private static (string HubId, double HubX, double HubY) AddTownSquare(
        SqliteWorldDatabase worldDb, Placed parent, Options options, ref int connectionsCreated)
    {
        const double offset = 8.0;
        var squareId = $"{parent.Id}_square";
        var sx = parent.X + offset;
        var sy = parent.Y + offset;

        var descriptor = new LocationDescriptor(
            Id: squareId,
            DisplayName: "Náměstí",
            BaseNoise: 0.35,
            NoisePerPerson: 0.02,
            Capacity: 60,
            AllowsPrivacy: false,
            Type: LocationType.Social,
            Terrain: TerrainType.Courtyard,
            DangerLevel: 0.0,
            AllowsPickup: true,
            NormId: null,
            X: sx,
            Y: sy,
            AltitudeMeters: parent.Tile.SampleAt(sx, sy));
        worldDb.InsertLocation(descriptor, options.Region);

        var distance = offset * 1.4142135623730951; // sqrt(2) — the diagonal offset's own length
        worldDb.InsertConnection(squareId, parent.Id, distance);
        worldDb.InsertConnection(parent.Id, squareId, distance);
        connectionsCreated++;

        return (squareId, sx, sy);
    }

    /// <summary>Creates a single <see cref="LocationType.Public"/> cemetery location at a
    /// deterministic id (<c>"{Region}_cemetery"</c>, so callers can wire
    /// <c>SceneOrchestratorOptions.CemeteryLocationId</c> without inspecting <see cref="Result"/>),
    /// positioned near whichever placed settlement is largest-tier, and connected to its
    /// <see cref="Options.ConnectionsPerLocation"/> nearest settlements (always including that
    /// anchor). Returns <c>(null, 0)</c> when nothing was placed this run.</summary>
    private static (string? Id, int ConnectionsCreated) AddCemetery(
        SqliteWorldDatabase worldDb, List<Placed> placed, Options options, Random rng)
    {
        if (placed.Count == 0) return (null, 0);

        var anchor = placed.OrderByDescending(p => (int)p.Tier).First();
        var angle = rng.NextDouble() * 2 * Math.PI;
        var radius = 80.0 + rng.NextDouble() * 70.0;
        var cx = anchor.X + Math.Cos(angle) * radius;
        var cy = anchor.Y + Math.Sin(angle) * radius;
        var cemeteryId = $"{options.Region}_cemetery";

        var descriptor = new LocationDescriptor(
            Id: cemeteryId,
            DisplayName: "Hřbitov",
            BaseNoise: 0.05,
            NoisePerPerson: 0.0,
            Capacity: 200,
            AllowsPrivacy: false,
            Type: LocationType.Public,
            Terrain: TerrainType.Courtyard,
            DangerLevel: 0.0,
            AllowsPickup: true,
            NormId: null,
            X: cx,
            Y: cy,
            AltitudeMeters: anchor.Tile.SampleAt(cx, cy));
        worldDb.InsertLocation(descriptor, options.Region);

        var cemeteryPlaced = new Placed(cemeteryId, cx, cy, anchor.Tile, anchor.Tier);
        var nearest = placed
            .OrderBy(p => DistanceSquared(cemeteryPlaced, p))
            .Take(Math.Max(1, options.ConnectionsPerLocation))
            .ToHashSet();
        nearest.Add(anchor); // always reachable from its own anchor settlement

        var created = 0;
        foreach (var target in nearest)
        {
            var distance = RoadDistance(cemeteryPlaced, target);
            worldDb.InsertConnection(cemeteryId, target.Id, distance);
            worldDb.InsertConnection(target.Id, cemeteryId, distance);
            created++;
        }

        return (cemeteryId, created);
    }

    /// <summary>Attaches the field→mill→bakery <see cref="ProductionSiteFactory"/> fixture chain to
    /// the single largest-tier settlement placed this run (Town preferred, else Village) — skipped
    /// entirely when only Camps were placed (too small to support a production chain).</summary>
    private static int AddProductionChain(SqliteWorldDatabase worldDb, List<Placed> placed)
    {
        var anchor = placed
            .Where(p => p.Tier is SettlementTier.Town or SettlementTier.Village)
            .OrderByDescending(p => (int)p.Tier)
            .FirstOrDefault();
        if (anchor is null) return 0;

        worldDb.AddObject(ProductionSiteFactory.Create($"{anchor.Id}_field", anchor.Id, PickupItemKind.Grain, "Obilné pole"));
        worldDb.AddObject(ProductionSiteFactory.Create($"{anchor.Id}_mill", anchor.Id, PickupItemKind.Flour, "Mlýn"));
        worldDb.AddObject(ProductionSiteFactory.Create($"{anchor.Id}_bakery", anchor.Id, PickupItemKind.Bread, "Pekárna"));
        return 3;
    }

    /// <summary>Adds up to <see cref="Options.ForcedTier"/>-driven object depth per need (1 for
    /// Camp, 2 for Village, 3 for Town — see <see cref="ObjectDepthForTier"/>) so a settlement
    /// isn't a dead end for NPC need-satisfaction — same role CastleVillageSeed's hand-authored
    /// objects play for the Castle/Village content. Town tier also gets one Social object (a
    /// market/gathering-place affordance) when the catalog has one. Objects come from
    /// <see cref="FoodTemplate"/> rows whose <see cref="FoodTemplate.Biome"/> matches this
    /// location's biome (or is <c>"Any"</c>) — a need with no matching template in the catalog is
    /// simply skipped, not an error.</summary>
    private static int AddCatalogObjects(
        SqliteWorldDatabase worldDb, string locationId, TerrainType biome, SettlementTier tier,
        IReadOnlyList<FoodTemplate> catalog, Random rng)
    {
        var created = 0;
        var biomeName = biome.ToString();
        var depth = ObjectDepthForTier(tier);
        AffordanceType[] needs = tier == SettlementTier.Town
            ? [.. BaseNeeds, AffordanceType.Social]
            : BaseNeeds;

        foreach (var need in needs)
        {
            var candidates = catalog
                .Where(t => t.AffordanceType == need &&
                    (string.Equals(t.Biome, biomeName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(t.Biome, "Any", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0) continue;

            var take = need == AffordanceType.Social ? 1 : depth;
            var slot = 0;
            foreach (var template in PickDistinct(candidates, Math.Min(take, candidates.Count), rng))
            {
                slot++;
                worldDb.AddObject(new WorldObject
                {
                    Category = CategoryFor(template.AffordanceType),
                    Id = $"{locationId}_{template.TemplateId}_{slot:D2}",
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
        }

        return created;
    }

    private static int ObjectDepthForTier(SettlementTier tier) => tier switch
    {
        SettlementTier.Camp => 1,
        SettlementTier.Village => 2,
        SettlementTier.Town => 3,
        _ => 1,
    };

    /// <summary>Picks up to <paramref name="count"/> DISTINCT templates from
    /// <paramref name="candidates"/> without replacement — fewer than <paramref name="count"/>
    /// candidates simply returns all of them.</summary>
    private static List<FoodTemplate> PickDistinct(List<FoodTemplate> candidates, int count, Random rng)
    {
        var pool = new List<FoodTemplate>(candidates);
        var result = new List<FoodTemplate>(count);
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var idx = rng.Next(pool.Count);
            result.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return result;
    }

    private static WorldObjectCategory CategoryFor(AffordanceType affordanceType) => affordanceType switch
    {
        AffordanceType.Hunger => WorldObjectCategory.Food,
        AffordanceType.Thirst => WorldObjectCategory.Drink,
        AffordanceType.Rest => WorldObjectCategory.Shelter,
        _ => WorldObjectCategory.Ambient,
    };
}
