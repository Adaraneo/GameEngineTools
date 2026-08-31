using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Droplet-based hydraulic erosion (Beyer 2015 / the commonly reimplemented "virtual pipes"
/// simplification) — independent implementation for TerraGen, built from day one with a
/// <c>locked</c> cell mask so tiles can carry their already-finalized neighbors' edges forward as
/// a fixed boundary condition (see <see cref="TileGenerator"/>) instead of eroding in isolation.
/// </summary>
public static class TileErosion
{
    public sealed record Parameters(
        int Seed = 1,
        int DropletCount = 20000,
        /// <summary>Maximum steps a single droplet takes — also used by <see cref="TileGenerator"/>
        /// to size the margin a tile pads its working grid with, since it's the real maximum
        /// distance a droplet can travel and affect terrain.</summary>
        int MaxDropletLifetime = 30,
        double Inertia = 0.05,
        double SedimentCapacityFactor = 4.0,
        double MinSedimentCapacity = 0.01,
        double ErodeSpeed = 0.3,
        double DepositSpeed = 0.3,
        double EvaporateSpeed = 0.01,
        double Gravity = 4.0,
        double InitialSpeed = 1.0,
        double InitialWater = 1.0,
        double MinWaterToContinue = 0.01);

    /// <summary>
    /// Erodes <paramref name="grid"/> in place. <paramref name="locked"/> (same length as
    /// <c>grid.Values</c>, or <c>null</c> for no locking) marks cells that must never be written —
    /// they're still read normally for gradient/height, just never eroded or deposited into. Used
    /// to hold an already-saved neighbor tile's edge fixed while this tile's own erosion runs, so
    /// two independently-eroded tiles still agree exactly along their shared border.
    /// </summary>
    public static void Erode(TerrainHeightmap grid, Parameters p, bool[]? locked = null)
    {
        if (p.DropletCount <= 0 || grid.Width < 3 || grid.Height < 3) return;

        var rng = new Random(p.Seed);
        var width = grid.Width;
        var height = grid.Height;

        for (var i = 0; i < p.DropletCount; i++)
        {
            var posX = rng.NextDouble() * (width - 1.001);
            var posY = rng.NextDouble() * (height - 1.001);
            double dirX = 0, dirY = 0;
            var speed = p.InitialSpeed;
            var water = p.InitialWater;
            var sediment = 0.0;

            var step = 0;
            for (; step < p.MaxDropletLifetime; step++)
            {
                var nodeX = (int)Math.Floor(posX);
                var nodeY = (int)Math.Floor(posY);
                if (nodeX < 0 || nodeX >= width - 1 || nodeY < 0 || nodeY >= height - 1)
                {
                    DepositAtBilinearCorners(grid, Math.Clamp(nodeX, 0, width - 2), Math.Clamp(nodeY, 0, height - 2), 0, 0, sediment, locked);
                    sediment = 0;
                    break;
                }

                var (oldHeight, gradX, gradY) = HeightAndGradient(grid, posX, posY, nodeX, nodeY);

                dirX = dirX * p.Inertia - gradX * (1 - p.Inertia);
                dirY = dirY * p.Inertia - gradY * (1 - p.Inertia);
                var dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
                if (dirLen < 1e-8)
                {
                    var randomAngle = rng.NextDouble() * Math.PI * 2;
                    dirX = Math.Cos(randomAngle);
                    dirY = Math.Sin(randomAngle);
                }
                else
                {
                    dirX /= dirLen;
                    dirY /= dirLen;
                }

                var newPosX = posX + dirX;
                var newPosY = posY + dirY;
                if (newPosX < 0 || newPosX >= width - 1 || newPosY < 0 || newPosY >= height - 1)
                {
                    DepositAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, sediment, locked);
                    sediment = 0;
                    break;
                }

                var newHeight = SampleHeight(grid, newPosX, newPosY);
                var deltaHeight = newHeight - oldHeight;

                var capacity = Math.Max(-deltaHeight * speed * water * p.SedimentCapacityFactor, p.MinSedimentCapacity);

                if (sediment > capacity || deltaHeight > 0)
                {
                    var amountToDeposit = deltaHeight > 0
                        ? Math.Min(deltaHeight, sediment)
                        : (sediment - capacity) * p.DepositSpeed;
                    sediment -= amountToDeposit;
                    DepositAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, amountToDeposit, locked);
                }
                else
                {
                    var amountToErode = Math.Min((capacity - sediment) * p.ErodeSpeed, -deltaHeight);
                    ErodeAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, amountToErode, locked);
                    sediment += amountToErode;
                }

                speed = Math.Sqrt(Math.Max(0, speed * speed + deltaHeight * p.Gravity));
                water *= 1 - p.EvaporateSpeed;

                posX = newPosX;
                posY = newPosY;

                if (water < p.MinWaterToContinue)
                {
                    var finalNodeX = (int)Math.Floor(posX);
                    var finalNodeY = (int)Math.Floor(posY);
                    if (finalNodeX >= 0 && finalNodeX < width - 1 && finalNodeY >= 0 && finalNodeY < height - 1)
                        DepositAtBilinearCorners(grid, finalNodeX, finalNodeY, posX - finalNodeX, posY - finalNodeY, sediment, locked);
                    sediment = 0;
                    break;
                }
            }

            if (step == p.MaxDropletLifetime && sediment > 0)
            {
                var finalNodeX = (int)Math.Floor(posX);
                var finalNodeY = (int)Math.Floor(posY);
                if (finalNodeX >= 0 && finalNodeX < width - 1 && finalNodeY >= 0 && finalNodeY < height - 1)
                    DepositAtBilinearCorners(grid, finalNodeX, finalNodeY, posX - finalNodeX, posY - finalNodeY, sediment, locked);
            }
        }
    }

    private static (double height, double gradX, double gradY)
        HeightAndGradient(TerrainHeightmap grid, double posX, double posY, int nodeX, int nodeY)
    {
        var fx = posX - nodeX;
        var fy = posY - nodeY;
        var w = grid.Width;

        var h00 = grid.Values[nodeY * w + nodeX];
        var h10 = grid.Values[nodeY * w + nodeX + 1];
        var h01 = grid.Values[(nodeY + 1) * w + nodeX];
        var h11 = grid.Values[(nodeY + 1) * w + nodeX + 1];

        var gradX = (h10 - h00) * (1 - fy) + (h11 - h01) * fy;
        var gradY = (h01 - h00) * (1 - fx) + (h11 - h10) * fx;
        var heightHere = h00 * (1 - fx) * (1 - fy) + h10 * fx * (1 - fy) + h01 * (1 - fx) * fy + h11 * fx * fy;

        return (heightHere, gradX, gradY);
    }

    private static double SampleHeight(TerrainHeightmap grid, double posX, double posY)
    {
        var nodeX = (int)Math.Floor(posX);
        var nodeY = (int)Math.Floor(posY);
        var fx = posX - nodeX;
        var fy = posY - nodeY;
        var w = grid.Width;

        var h00 = grid.Values[nodeY * w + nodeX];
        var h10 = grid.Values[nodeY * w + nodeX + 1];
        var h01 = grid.Values[(nodeY + 1) * w + nodeX];
        var h11 = grid.Values[(nodeY + 1) * w + nodeX + 1];
        return h00 * (1 - fx) * (1 - fy) + h10 * fx * (1 - fy) + h01 * (1 - fx) * fy + h11 * fx * fy;
    }

    /// <summary>Distributes <paramref name="amount"/> across the droplet's cell's 4 corners with
    /// bilinear weights, skipping any corner marked in <paramref name="locked"/> — a small amount
    /// of mass is lost on partially-locked splats, an accepted tradeoff (the grid already loses/
    /// gains mass asymmetrically at the map edge today).</summary>
    private static void DepositAtBilinearCorners(TerrainHeightmap grid, int nodeX, int nodeY, double fx, double fy, double amount, bool[]? locked)
    {
        if (amount <= 0) return;
        var w = grid.Width;
        void Add(int idx, double weight)
        {
            if (locked is null || !locked[idx])
                grid.Values[idx] += (float)(amount * weight);
        }
        Add(nodeY * w + nodeX, (1 - fx) * (1 - fy));
        Add(nodeY * w + nodeX + 1, fx * (1 - fy));
        Add((nodeY + 1) * w + nodeX, (1 - fx) * fy);
        Add((nodeY + 1) * w + nodeX + 1, fx * fy);
    }

    private static void ErodeAtBilinearCorners(TerrainHeightmap grid, int nodeX, int nodeY, double fx, double fy, double amount, bool[]? locked)
    {
        if (amount <= 0) return;
        var w = grid.Width;
        void Subtract(int idx, double weight)
        {
            if (locked is null || !locked[idx])
                grid.Values[idx] -= (float)(amount * weight);
        }
        Subtract(nodeY * w + nodeX, (1 - fx) * (1 - fy));
        Subtract(nodeY * w + nodeX + 1, fx * (1 - fy));
        Subtract((nodeY + 1) * w + nodeX, (1 - fx) * fy);
        Subtract((nodeY + 1) * w + nodeX + 1, fx * fy);
    }
}
