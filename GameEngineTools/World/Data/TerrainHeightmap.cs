// TerrainHeightmap.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;

    /// <summary>
    /// A rectangular grid of elevation samples authored by the standalone TerrainEditor tool.
    /// Locations' <see cref="GameEngineTools.World.Location.LocationDescriptor.AltitudeMeters"/>
    /// is baked once by sampling this grid at the location's (X, Y); the running simulation
    /// never needs to load the full grid itself.
    /// </summary>
    /// <param name="Id">Grid identifier — <c>"default"</c> for the world's single terrain today.</param>
    /// <param name="OriginX">World-space X (meters) of grid cell (0, 0).</param>
    /// <param name="OriginY">World-space Y (meters) of grid cell (0, 0).</param>
    /// <param name="CellSizeMeters">Distance in meters between adjacent grid samples.</param>
    /// <param name="Width">Number of samples along X.</param>
    /// <param name="Height">Number of samples along Y.</param>
    /// <param name="Values">
    /// Row-major elevation samples in meters; length must equal
    /// <paramref name="Width"/> * <paramref name="Height"/>.
    /// </param>
    /// <param name="RiverMask">
    /// Optional row-major byte flags, same length as <paramref name="Values"/>, marking river
    /// cells — painted separately from elevation so a river reads as freshwater even far from the
    /// coast (e.g. a mountain valley above sea level), instead of only "below 0m = water" being
    /// renderable as water. <c>0</c> means not a river; a non-zero cell IS a river, and the value
    /// itself carries its Strahler stream order (TerraGen's <c>TileHydrology</c>/<c>RiverMeander</c>
    /// — a headwater creek with no river tributary is order 1; order increases by 1 only where two
    /// reaches of the SAME order merge, so a creek staying a creek after a much smaller trickle
    /// joins it doesn't jump — matching how real drainage networks are classified, and giving a
    /// direct signal for "how big a river is this" beyond just "is it one"). Hand-painted rivers
    /// (TerrainEditor's brush/spring-trace/lake tools) always write plain <c>1</c> — a real river's
    /// bigger-downstream-of-a-merge structure isn't something a manual brush stroke can express,
    /// so those tools don't try to. <c>null</c> means no river data has been painted yet.
    /// </param>
    public sealed record TerrainHeightmap(
        string Id,
        double OriginX,
        double OriginY,
        double CellSizeMeters,
        int Width,
        int Height,
        float[] Values,
        byte[]? RiverMask = null)
    {
        /// <summary>True when (gx, gy) — clamped to the grid — has been painted as a river cell,
        /// of any Strahler order. Use <see cref="RiverOrder"/> to distinguish a headwater creek
        /// from a large trunk river.</summary>
        public bool IsRiver(int gx, int gy) => RiverOrder(gx, gy) > 0;

        /// <summary>Strahler stream order at (gx, gy) — clamped to the grid — or 0 if it's not a
        /// river cell at all. See <see cref="RiverMask"/>'s remarks for what the order means.</summary>
        public byte RiverOrder(int gx, int gy)
        {
            if (RiverMask is null) return 0;
            var cx = Math.Clamp(gx, 0, Width - 1);
            var cy = Math.Clamp(gy, 0, Height - 1);
            return RiverMask[cy * Width + cx];
        }

        /// <summary>
        /// Bilinearly samples elevation at an arbitrary world-space (x, y). Coordinates outside
        /// the grid clamp to the nearest edge rather than throwing.
        /// </summary>
        public double SampleAt(double worldX, double worldY)
        {
            var gx = (worldX - OriginX) / CellSizeMeters;
            var gy = (worldY - OriginY) / CellSizeMeters;

            var x0 = (int)Math.Floor(gx);
            var y0 = (int)Math.Floor(gy);

            var tx = gx - x0;
            var ty = gy - y0;

            var v00 = At(x0, y0);
            var v10 = At(x0 + 1, y0);
            var v01 = At(x0, y0 + 1);
            var v11 = At(x0 + 1, y0 + 1);

            var top = v00 + (v10 - v00) * tx;
            var bottom = v01 + (v11 - v01) * tx;
            return top + (bottom - top) * ty;
        }

        private double At(int x, int y)
        {
            var cx = Math.Clamp(x, 0, Width - 1);
            var cy = Math.Clamp(y, 0, Height - 1);
            return Values[cy * Width + cx];
        }

        /// <summary>Packs <see cref="Values"/> into a little-endian float32 byte buffer for BLOB storage.</summary>
        public byte[] ToBytes()
        {
            var bytes = new byte[Values.Length * sizeof(float)];
            Buffer.BlockCopy(Values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>Unpacks a little-endian float32 byte buffer (as produced by <see cref="ToBytes"/>) back into samples.</summary>
        public static float[] ValuesFromBytes(byte[] bytes, int width, int height)
        {
            var expected = width * height * sizeof(float);
            if (bytes.Length != expected)
                throw new ArgumentException(
                    $"Heightmap byte buffer length {bytes.Length} does not match {width}x{height} floats ({expected} bytes).",
                    nameof(bytes));

            var values = new float[width * height];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }
    }

    /// <summary>
    /// Lightweight metadata for one <see cref="TerrainHeightmap"/> row — everything except the
    /// actual elevation/river data — for listing many heightmaps (e.g. tiles saved by a batch
    /// generator) without paying to load and unpack every one's full sample array.
    /// </summary>
    public sealed record TerrainHeightmapSummary(
        string Id, double OriginX, double OriginY, double CellSizeMeters, int Width, int Height);
}
