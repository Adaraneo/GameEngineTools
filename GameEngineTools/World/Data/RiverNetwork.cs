// RiverNetwork.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;

    /// <summary>What a <see cref="RiverNode"/> represents topologically.</summary>
    public enum RiverNodeKind
    {
        /// <summary>A headwater — no upstream reach feeds this node.</summary>
        Source,

        /// <summary>Two or more reaches merge here into one.</summary>
        Confluence,

        /// <summary>Terminal node — the reach ending here drains out of the network.</summary>
        Mouth
    }

    /// <summary>A source/confluence/mouth point in a <see cref="RiverReach"/> graph — see
    /// docs/plans/river-network-graph-model.md. Reach-interior points live in <see cref="RiverReach.Polyline"/>.</summary>
    public sealed record RiverNode(string Id, string NetworkId, double X, double Y, RiverNodeKind Kind);

    /// <summary>A continuous, resolution-independent stretch of river between two <see cref="RiverNode"/>s
    /// — the graph replacement for a run of RiverMask/ShreveMagnitude raster cells.</summary>
    public sealed record RiverReach(
        string Id,
        string NetworkId,
        string FromNodeId,
        string ToNodeId,
        IReadOnlyList<(double X, double Y)> Polyline,
        int StrahlerOrder,
        int ShreveMagnitude,
        double WidthMeters)
    {
        /// <summary>Packs a polyline into a little-endian float64 byte buffer for BLOB storage.</summary>
        public static byte[] PolylineToBytes(IReadOnlyList<(double X, double Y)> polyline)
        {
            var bytes = new byte[polyline.Count * 2 * sizeof(double)];
            var offset = 0;
            foreach (var (x, y) in polyline)
            {
                BitConverter.TryWriteBytes(bytes.AsSpan(offset), x);
                BitConverter.TryWriteBytes(bytes.AsSpan(offset + sizeof(double)), y);
                offset += 2 * sizeof(double);
            }
            return bytes;
        }

        /// <summary>Unpacks a buffer produced by <see cref="PolylineToBytes"/> back into a polyline.</summary>
        public static IReadOnlyList<(double X, double Y)> PolylineFromBytes(byte[] bytes)
        {
            if (bytes.Length % (2 * sizeof(double)) != 0)
                throw new ArgumentException(
                    $"Polyline byte buffer length {bytes.Length} is not a multiple of {2 * sizeof(double)} (two float64s per point).",
                    nameof(bytes));

            var points = new (double X, double Y)[bytes.Length / (2 * sizeof(double))];
            for (var i = 0; i < points.Length; i++)
            {
                var offset = i * 2 * sizeof(double);
                var x = BitConverter.ToDouble(bytes, offset);
                var y = BitConverter.ToDouble(bytes, offset + sizeof(double));
                points[i] = (x, y);
            }
            return points;
        }
    }

    /// <summary>A still-water loop severed by a meander neck cutoff — the graph counterpart of
    /// <see cref="TerrainHeightmap.OxbowMask"/>; no flow, no node, no order/magnitude.</summary>
    public sealed record OxbowLoop(string Id, string NetworkId, IReadOnlyList<(double X, double Y)> Polyline);

    /// <summary>Every <see cref="RiverNode"/>, <see cref="RiverReach"/> and <see cref="OxbowLoop"/>
    /// generated together as one hydrology chunk, keyed by <see cref="NetworkId"/>.</summary>
    public sealed record RiverNetwork(
        string NetworkId,
        IReadOnlyList<RiverNode> Nodes,
        IReadOnlyList<RiverReach> Reaches,
        IReadOnlyList<OxbowLoop> Oxbows);
}
