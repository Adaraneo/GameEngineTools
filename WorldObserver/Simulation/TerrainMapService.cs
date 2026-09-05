// TerrainMapService.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using GameEngineTools.World.Data;
    using TerraGen;
    using TerraGen.Generation;
    using WorldObserver.Dtos;

    /// <summary>
    /// Backs the real geographic terrain map: opens (or creates) a PERSISTENT <c>terrain.db</c> —
    /// a sibling of the ephemeral <c>world.db</c> that <see cref="WorldHostedService"/> deletes and
    /// re-seeds on every startup, but this file is never touched by that logic, so generated tiles
    /// survive restarts — and generates whatever tiles a requested viewport needs, on demand.
    /// </summary>
    /// <remarks>
    /// Locations' <c>X</c>/<c>Y</c> (meters) and TerraGen tiles' <c>OriginX</c>/<c>OriginY</c> are
    /// already the same flat local frame by construction (both anchored at lat=0/lon=0 via
    /// <see cref="PlanetNoise.OffsetToLatLon"/>/<see cref="PlanetNoise.LatLonToOffset"/>), so no
    /// coordinate bridging is needed — this service just converts a world-meters box to lat/lon,
    /// generates, and hands the raw grids back.
    /// <para/>
    /// Tile size/erosion tradeoff (interactive path, deliberately coarser than TerraGen's own CLI
    /// default of 1&#160;km/2.5&#160;m): 2&#160;km tiles at 5&#160;m cells — 400×400 cells, the exact
    /// same cell count (and therefore the same ~220&#160;ms/tile generation cost on this machine,
    /// under the same 150,000-droplet cap <see cref="TileErosion.Parameters"/> uses) as an original
    /// 8&#160;km/20&#160;m tile, just redistributed as 4× finer detail over 1/16th the area. Chosen
    /// to match this app's actual scale: WorldObserver's seeded locations span only a few hundred
    /// meters (a village/castle-sized settlement, not a sprawling multi-km world), so the old
    /// 20&#160;m cells read as blocky/blurry once the map auto-zoomed to fit that small a footprint —
    /// 5&#160;m cells resolve real detail at that zoom instead. The tradeoff moves the other way for a
    /// genuinely WIDE pan/zoom-out: covering the same total area now takes more (equally cheap)
    /// tile-generation calls than fewer, larger ones would.
    /// <para/>
    /// Tile boundaries are snapped to a FIXED global grid (multiples of <see cref="TileSizeMeters"/>
    /// from the world origin), not to the request's own bounding box — otherwise two overlapping-but-
    /// not-identical viewport requests would each carve the plane into a differently-offset tile grid,
    /// so the same physical ground would regenerate under a different tile id every time the viewport
    /// shifted slightly. With a fixed grid, tiles already generated for an earlier viewport are simply
    /// reused (looked up by origin) instead of regenerated.
    /// </remarks>
    public sealed class TerrainMapService
    {
        private const double TileSizeMeters = 2000.0;
        private const double CellSizeMeters = 5.0;
        private const double OriginMatchToleranceMeters = 1.0;

        private readonly object _sync = new();
        private readonly SqliteWorldDatabase _db;
        private readonly PlanetSettings.Resolved _planet;
        private readonly PlanetNoise.Parameters _noiseParams;
        private readonly TileErosion.Parameters _erosionParams;

        public TerrainMapService()
        {
            var worldDir = Path.Combine(Directory.GetCurrentDirectory(), "SourceFiles", "World");
            Directory.CreateDirectory(worldDir);
            var dbPath = Path.Combine(worldDir, "terrain.db");

            // Dedicated terrain schema — this database exists purely to hold generated heightmap
            // tiles; it doesn't even have a Locations/Connections table (that lives in the
            // ephemeral world.db WorldHostedService manages separately).
            _db = new SqliteWorldDatabase(dbPath);
            WorldDatabaseSeeder.InitializeTerrainDatabase(_db);

            _planet = PlanetSettings.Load(dbPath);
            _noiseParams = new PlanetNoise.Parameters(Seed: _planet.Seed, GravityMs2: _planet.GravityMs2,
                TectonicPlateCount: _planet.TectonicPlateCount);

            var cellsPerSide = Math.Max(1, (int)Math.Round(TileSizeMeters / CellSizeMeters));
            var droplets = Math.Min(150_000, cellsPerSide * cellsPerSide); // ~same convention as TerraGen's CLI (50% erosion)
            _erosionParams = new TileErosion.Parameters(Seed: _planet.Seed, DropletCount: droplets);
        }

        /// <summary>
        /// Returns every tile covering the requested world-meters bounding box, generating whichever
        /// ones aren't already saved. Safe to call from a request thread — serialized against
        /// concurrent viewport requests so two overlapping calls never race the generator over the
        /// same tile.
        /// </summary>
        public IReadOnlyList<TerrainTileDto> GetTiles(double minX, double minY, double maxX, double maxY)
        {
            lock (_sync)
                return GetOrGenerateTiles(minX, minY, maxX, maxY).Select(ToDto).ToList();
        }

        /// <summary>
        /// Returns ONE combined heightmap covering the requested world-meters bounding box (plus
        /// <paramref name="marginMeters"/> padding on every side), stitched from however many
        /// underlying tiles it takes — generating whichever are missing. For pathfinding
        /// (<see cref="RoadMapService"/>), which needs one continuous grid to walk, not a set of
        /// separate tiles. Stitching is pure index copying, no resampling: every tile shares the
        /// same <see cref="CellSizeMeters"/> and sits on the same fixed global grid, so a tile's
        /// place in the combined array is an exact integer offset. Returns <c>null</c> only if the
        /// requested box is degenerate or no tile could be generated for it.
        /// </summary>
        public TerrainHeightmap? GetCombinedGrid(double minX, double minY, double maxX, double maxY, double marginMeters = 50.0)
        {
            lock (_sync)
            {
                var tiles = GetOrGenerateTiles(minX - marginMeters, minY - marginMeters, maxX + marginMeters, maxY + marginMeters);
                if (tiles.Count == 0) return null;

                var combinedOriginX = tiles.Min(t => t.OriginX);
                var combinedOriginY = tiles.Min(t => t.OriginY);
                var combinedMaxX = tiles.Max(t => t.OriginX + t.Width * t.CellSizeMeters);
                var combinedMaxY = tiles.Max(t => t.OriginY + t.Height * t.CellSizeMeters);
                var combinedWidth = (int)Math.Round((combinedMaxX - combinedOriginX) / CellSizeMeters);
                var combinedHeight = (int)Math.Round((combinedMaxY - combinedOriginY) / CellSizeMeters);
                if (combinedWidth <= 0 || combinedHeight <= 0) return null;

                var values = new float[combinedWidth * combinedHeight];
                byte[]? riverMask = tiles.Any(t => t.RiverMask is not null) ? new byte[combinedWidth * combinedHeight] : null;

                foreach (var tile in tiles)
                {
                    var offsetX = (int)Math.Round((tile.OriginX - combinedOriginX) / CellSizeMeters);
                    var offsetY = (int)Math.Round((tile.OriginY - combinedOriginY) / CellSizeMeters);
                    for (var ty = 0; ty < tile.Height; ty++)
                    {
                        var srcRow = ty * tile.Width;
                        var dstRow = (offsetY + ty) * combinedWidth + offsetX;
                        Array.Copy(tile.Values, srcRow, values, dstRow, tile.Width);
                        if (riverMask is not null && tile.RiverMask is not null)
                            Array.Copy(tile.RiverMask, srcRow, riverMask, dstRow, tile.Width);
                    }
                }

                var combined = new TerrainHeightmap("combined", combinedOriginX, combinedOriginY, CellSizeMeters,
                    combinedWidth, combinedHeight, values, riverMask);
                // Stage 4: RoadMapService's RoadPathfinder needs graph-derived rivers too, not just a
                // hand-painted/legacy RiverMask column this service itself never populates.
                return RiverNetworkRasterizer.MaterializeOnto(_db.LoadAllReaches(), _db.LoadAllOxbows(), combined);
            }
        }

        /// <summary>Shared by <see cref="GetTiles"/> and <see cref="GetCombinedGrid"/> — must be
        /// called under <see cref="_sync"/>.</summary>
        private List<TerrainHeightmap> GetOrGenerateTiles(double minX, double minY, double maxX, double maxY)
        {
            var snapMinX = Math.Floor(Math.Min(minX, maxX) / TileSizeMeters) * TileSizeMeters;
            var snapMinY = Math.Floor(Math.Min(minY, maxY) / TileSizeMeters) * TileSizeMeters;
            var snapMaxX = Math.Ceiling(Math.Max(minX, maxX) / TileSizeMeters) * TileSizeMeters;
            var snapMaxY = Math.Ceiling(Math.Max(minY, maxY) / TileSizeMeters) * TileSizeMeters;

            var existing = _db.ListHeightmaps();
            var tiles = new List<TerrainHeightmap>();

            for (var oy = snapMinY; oy < snapMaxY - 1e-6; oy += TileSizeMeters)
            {
                for (var ox = snapMinX; ox < snapMaxX - 1e-6; ox += TileSizeMeters)
                {
                    var match = existing.FirstOrDefault(h =>
                        Math.Abs(h.OriginX - ox) < OriginMatchToleranceMeters &&
                        Math.Abs(h.OriginY - oy) < OriginMatchToleranceMeters);

                    var heightmap = match is not null ? _db.LoadHeightmap(match.Id) : GenerateTile(ox, oy);
                    if (heightmap is not null) tiles.Add(heightmap);
                }
            }

            return tiles;
        }

        /// <summary>Generates (and persists) exactly one tile at the given ALREADY-SNAPPED origin.</summary>
        private TerrainHeightmap? GenerateTile(double originX, double originY)
        {
            var (latA, lonA) = PlanetNoise.OffsetToLatLon(originX, originY, 0.0, 0.0, _planet.PlanetRadiusMeters);
            var (latB, lonB) = PlanetNoise.OffsetToLatLon(
                originX + TileSizeMeters, originY + TileSizeMeters, 0.0, 0.0, _planet.PlanetRadiusMeters);

            var runSettings = new TileGenerator.RunSettings(
                LatMin: Math.Min(latA, latB), LatMax: Math.Max(latA, latB),
                LonMin: Math.Min(lonA, lonB), LonMax: Math.Max(lonA, lonB),
                TileSizeMeters: TileSizeMeters, CellSizeMeters: CellSizeMeters,
                NoiseParams: _noiseParams, ErosionParams: _erosionParams,
                PlanetRadiusMeters: _planet.PlanetRadiusMeters);

            var results = TileGenerator.Run(_db, runSettings);
            var result = results.FirstOrDefault();
            return result is null ? null : _db.LoadHeightmap(result.Id);
        }

        private static TerrainTileDto ToDto(TerrainHeightmap heightmap)
        {
            var elevations = new short[heightmap.Values.Length];
            for (var i = 0; i < heightmap.Values.Length; i++)
                elevations[i] = (short)Math.Round(Math.Clamp(heightmap.Values[i], short.MinValue, short.MaxValue));

            return new TerrainTileDto(
                heightmap.Id, heightmap.OriginX, heightmap.OriginY, heightmap.CellSizeMeters,
                heightmap.Width, heightmap.Height, elevations);
        }
    }
}
