// RoadMapService.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using TerraGen.Generation;

    /// <summary>
    /// Computes and caches terrain-aware road paths between connected locations, in the
    /// background — NOT inline in the Tick loop, since a path spanning several tiles can take a
    /// moment (each missing tile alone costs ~a couple hundred ms to generate) and this shouldn't
    /// delay the ~200ms-cadence world-state push. <see cref="WorldStateProjector"/> just reads
    /// whatever's in the cache each tick; a connection with no cached path yet (or one that ever
    /// fails) simply renders as a straight line on the client until this fills in.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT persisted — a path is a deterministic function of (planet seed, the two
    /// locations' fixed X/Y), so there's no staleness risk, and the expensive part (terrain
    /// generation) is already cached in <see cref="TerrainMapService"/>'s <c>terrain.db</c>; a
    /// cache miss here only re-runs A* over already-generated tiles, which is cheap.
    /// </remarks>
    public sealed class RoadMapService
    {
        private readonly TerrainMapService _terrain;
        private readonly ConcurrentDictionary<(string From, string To), IReadOnlyList<(double X, double Y)>> _cache = new();
        private readonly ConcurrentDictionary<(string From, string To), byte> _queued = new();

        public RoadMapService(TerrainMapService terrain)
        {
            _terrain = terrain;
        }

        /// <summary>
        /// Kicks off background pathfinding for every connection that doesn't have a cached (or
        /// already-queued) result yet. Safe to call repeatedly — already-queued/cached pairs are
        /// skipped. Needs each location's world X/Y, so this can't work from ids alone.
        /// </summary>
        public void EnsureQueued(
            IReadOnlyList<(string From, string To, double Dist)> connections,
            IReadOnlyDictionary<string, (double X, double Y)> locationPositions)
        {
            foreach (var (from, to, _) in connections)
            {
                var key = (from, to);
                if (_cache.ContainsKey(key)) continue;
                if (!_queued.TryAdd(key, 0)) continue; // already in flight

                if (!locationPositions.TryGetValue(from, out var a) || !locationPositions.TryGetValue(to, out var b))
                {
                    _queued.TryRemove(key, out _);
                    continue;
                }

                _ = Task.Run(() => Compute(key, a, b));
            }
        }

        /// <summary>Non-blocking cache read — <c>null</c> means "not ready yet (or never
        /// succeeded)", the caller falls back to a straight line.</summary>
        public IReadOnlyList<(double X, double Y)>? TryGetPath(string from, string to)
            => _cache.TryGetValue((from, to), out var path) ? path : null;

        private void Compute((string From, string To) key, (double X, double Y) a, (double X, double Y) b)
        {
            try
            {
                var minX = Math.Min(a.X, b.X);
                var minY = Math.Min(a.Y, b.Y);
                var maxX = Math.Max(a.X, b.X);
                var maxY = Math.Max(a.Y, b.Y);

                var grid = _terrain.GetCombinedGrid(minX, minY, maxX, maxY);
                if (grid is null) return;

                var path = RoadPathfinder.FindPath(grid, a.X, a.Y, b.X, b.Y);
                if (path is not null)
                    _cache[key] = path.WorldPoints.ToList();
            }
            catch
            {
                // Never let a failed/slow pathfind take down the background worker — the
                // connection just keeps rendering as a straight line.
            }
            finally
            {
                _queued.TryRemove(key, out _);
            }
        }
    }
}
