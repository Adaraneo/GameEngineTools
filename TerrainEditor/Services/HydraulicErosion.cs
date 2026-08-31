using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Droplet-based hydraulic erosion (Beyer 2015 / the commonly reimplemented "virtual pipes"
/// simplification of it) — simulates individual water droplets flowing downhill across the
/// heightmap, eroding where they speed up and depositing where they slow down. Run as a
/// post-process after <see cref="TerrainGenerator.Generate"/> to turn raw noise into something
/// that looks weathered: carved valleys, sediment fans, softened ridgelines.
/// </summary>
/// <remarks>
/// The simulation constants (gravity, erosion/deposit speed, sediment capacity factor) are the
/// usual empirically-tuned unitless values used in heightmap erosion — not physically calibrated
/// SI units — same as every other implementation of this well-known technique. Heights are read
/// in meters (the grid's own unit); droplet positions are tracked in continuous grid-cell space.
/// </remarks>
public static class HydraulicErosion
{
    public sealed record Parameters(
        int Seed = 1,
        /// <summary>How many droplets to simulate — more droplets erode more of the map, at
        /// proportionally higher cost. The caller typically scales this with the grid's cell count.</summary>
        int DropletCount = 20000,
        /// <summary>Maximum steps a single droplet takes before being retired even if it hasn't
        /// reached the edge of the map or run out of water.</summary>
        int MaxDropletLifetime = 30,
        /// <summary>How strongly a droplet keeps its previous direction (0 = always turns straight
        /// downhill; 1 = never turns, ignores the terrain entirely). Real water has momentum, so a
        /// small nonzero value carves straighter, more natural-looking channels.</summary>
        double Inertia = 0.05,
        /// <summary>How much sediment a droplet can carry, scaled by its speed × water × slope.</summary>
        double SedimentCapacityFactor = 4.0,
        /// <summary>Floor on sediment capacity even on flat ground — without this a droplet on
        /// dead-flat terrain could neither erode nor deposit and would just carry sediment forever.</summary>
        double MinSedimentCapacity = 0.01,
        /// <summary>Fraction of the capacity gap eroded per step when the droplet is carrying less
        /// than its capacity.</summary>
        double ErodeSpeed = 0.3,
        /// <summary>Fraction of the excess sediment deposited per step when the droplet is
        /// carrying more than its capacity (or flowing uphill, which can't happen physically).</summary>
        double DepositSpeed = 0.3,
        /// <summary>Fraction of its water a droplet loses per step — droplets that evaporate away
        /// entirely stop early, capping how far a single droplet can travel.</summary>
        double EvaporateSpeed = 0.01,
        double Gravity = 4.0,
        double InitialSpeed = 1.0,
        double InitialWater = 1.0,
        /// <summary>A droplet retires once its remaining water drops below this.</summary>
        double MinWaterToContinue = 0.01);

    /// <summary>Erodes <paramref name="grid"/> in place — mutates <c>grid.Values</c> directly,
    /// same convention as <see cref="LocationProtector"/>/<see cref="HydrologyGenerator"/>.
    /// A no-op when <see cref="Parameters.DropletCount"/> is 0 or the grid is too small (&lt; 3
    /// cells per side — gradient sampling needs a full neighboring cell in every direction).</summary>
    public static void Erode(TerrainHeightmap grid, Parameters p)
    {
        if (p.DropletCount <= 0 || grid.Width < 3 || grid.Height < 3) return;

        var rng = new Random(p.Seed);
        var width = grid.Width;
        var height = grid.Height;

        for (var i = 0; i < p.DropletCount; i++)
        {
            // Keep a 1-cell margin so gradient sampling (which reads x+1/y+1) never falls off
            // the grid at the spawn point itself.
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
                    // Shouldn't normally happen (the two exits below already keep pos in bounds),
                    // but if it does, don't silently drop whatever sediment is still carried.
                    DepositAtBilinearCorners(grid, Math.Clamp(nodeX, 0, width - 2), Math.Clamp(nodeY, 0, height - 2), 0, 0, sediment);
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
                    // Deposit whatever's left right before leaving the map, rather than losing it.
                    DepositAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, sediment);
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
                    DepositAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, amountToDeposit);
                }
                else
                {
                    var amountToErode = Math.Min((capacity - sediment) * p.ErodeSpeed, -deltaHeight);
                    ErodeAtBilinearCorners(grid, nodeX, nodeY, posX - nodeX, posY - nodeY, amountToErode);
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
                        DepositAtBilinearCorners(grid, finalNodeX, finalNodeY, posX - finalNodeX, posY - finalNodeY, sediment);
                    sediment = 0;
                    break;
                }
            }

            // The droplet also dies of old age (MaxDropletLifetime reached) without ever hitting
            // one of the breaks above — that carried sediment must still go back into the terrain,
            // not vanish, or the total elevation mass slowly drains away over many droplets.
            if (step == p.MaxDropletLifetime && sediment > 0)
            {
                var finalNodeX = (int)Math.Floor(posX);
                var finalNodeY = (int)Math.Floor(posY);
                if (finalNodeX >= 0 && finalNodeX < width - 1 && finalNodeY >= 0 && finalNodeY < height - 1)
                    DepositAtBilinearCorners(grid, finalNodeX, finalNodeY, posX - finalNodeX, posY - finalNodeY, sediment);
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
    /// the same bilinear weights <see cref="TerrainHeightmap.SampleAt"/> would read it back with —
    /// keeps the deposited mass positioned exactly where the droplet actually was within the cell.</summary>
    private static void DepositAtBilinearCorners(TerrainHeightmap grid, int nodeX, int nodeY, double fx, double fy, double amount)
    {
        if (amount <= 0) return;
        var w = grid.Width;
        grid.Values[nodeY * w + nodeX] += (float)(amount * (1 - fx) * (1 - fy));
        grid.Values[nodeY * w + nodeX + 1] += (float)(amount * fx * (1 - fy));
        grid.Values[(nodeY + 1) * w + nodeX] += (float)(amount * (1 - fx) * fy);
        grid.Values[(nodeY + 1) * w + nodeX + 1] += (float)(amount * fx * fy);
    }

    private static void ErodeAtBilinearCorners(TerrainHeightmap grid, int nodeX, int nodeY, double fx, double fy, double amount)
    {
        if (amount <= 0) return;
        var w = grid.Width;
        grid.Values[nodeY * w + nodeX] -= (float)(amount * (1 - fx) * (1 - fy));
        grid.Values[nodeY * w + nodeX + 1] -= (float)(amount * fx * (1 - fy));
        grid.Values[(nodeY + 1) * w + nodeX] -= (float)(amount * (1 - fx) * fy);
        grid.Values[(nodeY + 1) * w + nodeX + 1] -= (float)(amount * fx * fy);
    }
}
