using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Stitches whichever ALREADY-SAVED terrain.db tiles overlap a requested world-meters box into
/// one continuous <see cref="TerrainHeightmap"/> — pure per-tile index copying, no resampling
/// (same technique WorldObserver's <c>TerrainMapService.GetCombinedGrid</c> uses), since every
/// tile from one TerraGen run shares the same <c>CellSizeMeters</c> and sits on the same fixed
/// global grid. Never generates missing terrain — a gap with no saved tile is simply not covered
/// by the combined result (TerrainEditor only browses what TerraGen has already produced).
/// </summary>
public static class TileStitcher
{
    /// <summary>
    /// Loads and stitches every heightmap in <paramref name="summaries"/> whose bounds overlap
    /// the given box. <c>Combined</c> is <c>null</c> if nothing overlaps. Returns the single
    /// matching tile unchanged (its own id) when only one overlaps — the "combined" id/stitching
    /// only kicks in once there's actually more than one tile to merge, or a non-uniform
    /// <c>CellSizeMeters</c> makes index-copy stitching unsafe (falls back to the first tile
    /// rather than guessing). <c>Sources</c> lists exactly the original saved tiles (each with
    /// its own authoritative Id/Origin/Size) that <c>Combined</c> was built from — pass it to
    /// <see cref="SplitAndSave"/> to write edits back to those same tiles instead of one big
    /// "combined" blob.
    /// </summary>
    public static (TerrainHeightmap? Combined, IReadOnlyList<TerrainHeightmap> Sources) BuildCombinedGrid(
        IReadOnlyList<TerrainHeightmapSummary> summaries,
        Func<string, TerrainHeightmap?> loadTile,
        double minX, double minY, double maxX, double maxY)
    {
        var overlapping = summaries.Where(s => Overlaps(s, minX, minY, maxX, maxY)).ToList();
        if (overlapping.Count == 0) return (null, []);

        var tiles = overlapping
            .Select(s => loadTile(s.Id))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();
        if (tiles.Count == 0) return (null, []);
        if (tiles.Count == 1) return (tiles[0], tiles);

        var cellSize = tiles[0].CellSizeMeters;
        if (tiles.Any(t => Math.Abs(t.CellSizeMeters - cellSize) > 1e-6))
            return (tiles[0], [tiles[0]]); // mixed cell sizes — can't index-copy safely, fall back to one tile

        var combinedOriginX = tiles.Min(t => t.OriginX);
        var combinedOriginY = tiles.Min(t => t.OriginY);
        var combinedMaxX = tiles.Max(t => t.OriginX + t.Width * t.CellSizeMeters);
        var combinedMaxY = tiles.Max(t => t.OriginY + t.Height * t.CellSizeMeters);
        var combinedWidth = (int)Math.Round((combinedMaxX - combinedOriginX) / cellSize);
        var combinedHeight = (int)Math.Round((combinedMaxY - combinedOriginY) / cellSize);
        if (combinedWidth <= 0 || combinedHeight <= 0) return (tiles[0], [tiles[0]]);

        var values = new float[combinedWidth * combinedHeight];
        byte[]? riverMask = tiles.Any(t => t.RiverMask is not null) ? new byte[combinedWidth * combinedHeight] : null;

        foreach (var tile in tiles)
            CopyInto(tile, tile.OriginX, tile.OriginY, values, riverMask, combinedOriginX, combinedOriginY, combinedWidth, combinedHeight, cellSize);

        var combined = new TerrainHeightmap("combined", combinedOriginX, combinedOriginY, cellSize,
            combinedWidth, combinedHeight, values, riverMask);
        return (combined, tiles);
    }

    /// <summary>
    /// Writes a combined grid's current data back out to <paramref name="sources"/> — the exact
    /// original tiles it was built from (see <see cref="BuildCombinedGrid"/>) — one
    /// <paramref name="saveTile"/> call per source, at that source's own id/origin/size. Lets
    /// edits made while browsing a stitched multi-tile view (painting, erosion, lakes, ...) land
    /// back in the individual TerraGen tiles they came from instead of one throwaway "combined" row.
    /// A no-op for a source outside <paramref name="combined"/>'s current bounds (shouldn't happen
    /// in practice — every source is part of what built it — but guards against a stale sources list).
    /// </summary>
    public static void SplitAndSave(TerrainHeightmap combined, IReadOnlyList<TerrainHeightmap> sources, Action<TerrainHeightmap> saveTile)
    {
        foreach (var source in sources)
        {
            var offsetX = (int)Math.Round((source.OriginX - combined.OriginX) / combined.CellSizeMeters);
            var offsetY = (int)Math.Round((source.OriginY - combined.OriginY) / combined.CellSizeMeters);
            if (offsetX < 0 || offsetY < 0 || offsetX + source.Width > combined.Width || offsetY + source.Height > combined.Height)
                continue; // source no longer within the combined grid's bounds — nothing to extract

            var values = new float[source.Width * source.Height];
            byte[]? riverMask = combined.RiverMask is not null ? new byte[source.Width * source.Height] : null;

            for (var ty = 0; ty < source.Height; ty++)
            {
                var srcRow = (offsetY + ty) * combined.Width + offsetX;
                var dstRow = ty * source.Width;
                Array.Copy(combined.Values, srcRow, values, dstRow, source.Width);
                if (riverMask is not null)
                    Array.Copy(combined.RiverMask!, srcRow, riverMask, dstRow, source.Width);
            }

            saveTile(new TerrainHeightmap(source.Id, source.OriginX, source.OriginY, source.CellSizeMeters,
                source.Width, source.Height, values, riverMask));
        }
    }

    private static void CopyInto(
        TerrainHeightmap tile, double tileOriginX, double tileOriginY,
        float[] destValues, byte[]? destRiverMask,
        double combinedOriginX, double combinedOriginY, int combinedWidth, int combinedHeight, double cellSize)
    {
        var offsetX = (int)Math.Round((tileOriginX - combinedOriginX) / cellSize);
        var offsetY = (int)Math.Round((tileOriginY - combinedOriginY) / cellSize);

        for (var ty = 0; ty < tile.Height; ty++)
        {
            var gy = offsetY + ty;
            if (gy < 0 || gy >= combinedHeight) continue;
            if (offsetX < 0 || offsetX + tile.Width > combinedWidth) continue;

            var srcRow = ty * tile.Width;
            var dstRow = gy * combinedWidth + offsetX;
            Array.Copy(tile.Values, srcRow, destValues, dstRow, tile.Width);
            if (destRiverMask is not null && tile.RiverMask is not null)
                Array.Copy(tile.RiverMask, srcRow, destRiverMask, dstRow, tile.Width);
        }
    }

    private static bool Overlaps(TerrainHeightmapSummary s, double minX, double minY, double maxX, double maxY)
    {
        var sMaxX = s.OriginX + s.Width * s.CellSizeMeters;
        var sMaxY = s.OriginY + s.Height * s.CellSizeMeters;
        return s.OriginX < maxX && sMaxX > minX && s.OriginY < maxY && sMaxY > minY;
    }
}
