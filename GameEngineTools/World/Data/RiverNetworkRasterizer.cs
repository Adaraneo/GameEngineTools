// RiverNetworkRasterizer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;

    /// <summary>Stamps a persisted river graph onto ANY target grid, not necessarily the one it was
    /// generated at — see docs/plans/river-network-graph-model.md, Stage 3.</summary>
    public static class RiverNetworkRasterizer
    {
        /// <summary>Rasterizes <paramref name="reaches"/>/<paramref name="oxbows"/> onto
        /// <paramref name="targetGrid"/>'s own origin/size/cell size, same output shape as
        /// <see cref="TerrainHeightmap.RiverMask"/>/<see cref="TerrainHeightmap.ShreveMagnitude"/>/
        /// <see cref="TerrainHeightmap.OxbowMask"/>.</summary>
        public static (byte[] RiverMask, int[] ShreveMagnitude, byte[] OxbowMask) Rasterize(
            IReadOnlyList<RiverReach> reaches, IReadOnlyList<OxbowLoop> oxbows, TerrainHeightmap targetGrid)
        {
            var width = targetGrid.Width;
            var height = targetGrid.Height;
            var cellSize = targetGrid.CellSizeMeters;
            var mask = new byte[width * height];
            var magnitude = new int[width * height];

            int GridX(double worldX) => (int)Math.Round((worldX - targetGrid.OriginX) / cellSize);
            int GridY(double worldY) => (int)Math.Round((worldY - targetGrid.OriginY) / cellSize);

            foreach (var reach in reaches)
            {
                if (reach.Polyline.Count == 0 || !Overlaps(reach.Polyline, targetGrid)) continue;
                var order = (byte)Math.Clamp(reach.StrahlerOrder, 0, 255);

                if (reach.Polyline.Count == 1)
                {
                    var (x, y) = reach.Polyline[0];
                    StampIfInBounds(mask, magnitude, width, height, GridX(x), GridY(y), order, reach.ShreveMagnitude);
                    continue;
                }

                for (var k = 0; k < reach.Polyline.Count - 1; k++)
                {
                    var (x0, y0) = reach.Polyline[k];
                    var (x1, y1) = reach.Polyline[k + 1];
                    ForEachLinePoint(width, height, GridX(x0), GridY(y0), GridX(x1), GridY(y1),
                        idx => StampMax(mask, magnitude, idx, order, reach.ShreveMagnitude));
                }
            }

            var oxbowMask = new byte[width * height];
            foreach (var oxbow in oxbows)
            {
                if (oxbow.Polyline.Count == 0 || !Overlaps(oxbow.Polyline, targetGrid)) continue;
                for (var k = 0; k < oxbow.Polyline.Count; k++)
                {
                    var (x, y) = oxbow.Polyline[k];
                    var gx = GridX(x);
                    var gy = GridY(y);
                    if (gx >= 0 && gx < width && gy >= 0 && gy < height) oxbowMask[gy * width + gx] = 1;
                    if (k + 1 >= oxbow.Polyline.Count) continue;
                    var (nx, ny) = oxbow.Polyline[k + 1];
                    ForEachLinePoint(width, height, gx, gy, GridX(nx), GridY(ny), idx => oxbowMask[idx] = 1);
                }
            }

            return (mask, magnitude, oxbowMask);
        }

        /// <summary>Cheap bounding-box reject — grid coords deliberately aren't clamped elsewhere
        /// (a clamped endpoint would draw a false line to the tile edge), so this keeps a far-away
        /// polyline from making <see cref="ForEachLinePoint"/> walk a huge unrelated distance.</summary>
        private static bool Overlaps(IReadOnlyList<(double X, double Y)> polyline, TerrainHeightmap grid)
        {
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
            var minY = double.PositiveInfinity; var maxY = double.NegativeInfinity;
            foreach (var (x, y) in polyline)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            var gridMaxX = grid.OriginX + grid.Width * grid.CellSizeMeters;
            var gridMaxY = grid.OriginY + grid.Height * grid.CellSizeMeters;
            return maxX >= grid.OriginX && minX <= gridMaxX && maxY >= grid.OriginY && minY <= gridMaxY;
        }

        private static void StampIfInBounds(byte[] mask, int[] magnitude, int width, int height, int x, int y, byte order, int mag)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            StampMax(mask, magnitude, y * width + x, order, mag);
        }

        /// <summary>Keeps whichever order (and co-indexed magnitude) is bigger when reaches overlap a pixel.</summary>
        private static void StampMax(byte[] mask, int[] magnitude, int idx, byte value, int mag)
        {
            if (value <= mask[idx]) return;
            mask[idx] = value;
            magnitude[idx] = mag;
        }

        /// <summary>Bresenham; endpoints outside [0,width)x[0,height) are fine — only in-bounds points get plotted.</summary>
        private static void ForEachLinePoint(int width, int height, int x0, int y0, int x1, int y1, Action<int> plot)
        {
            var dx = Math.Abs(x1 - x0);
            var dy = -Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;

            var x = x0;
            var y = y0;
            while (true)
            {
                if (x >= 0 && x < width && y >= 0 && y < height)
                    plot(y * width + x);
                if (x == x1 && y == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
