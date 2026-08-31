using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Terrain-aware pathfinding between two world-space points: A* over a heightmap grid,
/// penalizing slope (roads avoid climbing steep terrain), river crossings (a moderate
/// "ford/bridge" cost), and below-sea-level cells (a much steeper cost — crossing open sea
/// makes far less sense than fording a river, so it's avoided unless there's truly no way
/// around). None of these are hard blocks. Independent port of the same algorithm
/// TerrainEditor's own RoadGenerator uses — no shared code, this project doesn't reference
/// TerrainEditor.
/// </summary>
public static class RoadPathfinder
{
    private const double SlopePenaltyFactor = 4.0; // higher = paths bend harder to avoid steep terrain
    private const double RiverCrossingCost = 30.0; // meters-equivalent added per river cell entered
    private const double SeaCrossingCost = 300.0; // meters-equivalent added per below-sea-level cell entered

    public sealed record RoadPath(IReadOnlyList<(double X, double Y)> WorldPoints, double LengthMeters);

    /// <summary>
    /// Finds a terrain-aware path from (startWorldX, startWorldY) to (endWorldX, endWorldY).
    /// Returns <c>null</c> only if the grid is degenerate (0-size) — otherwise a path always
    /// exists because slope/river/sea costs are penalties, not hard blocks.
    /// </summary>
    public static RoadPath? FindPath(TerrainHeightmap grid, double startWorldX, double startWorldY, double endWorldX, double endWorldY)
    {
        var w = grid.Width;
        var h = grid.Height;
        if (w <= 0 || h <= 0) return null;

        var (sx, sy) = ToCell(grid, startWorldX, startWorldY);
        var (ex, ey) = ToCell(grid, endWorldX, endWorldY);
        var startIdx = sy * w + sx;
        var endIdx = ey * w + ex;

        if (startIdx == endIdx)
            return new RoadPath([(startWorldX, startWorldY), (endWorldX, endWorldY)], 0.0);

        var gScore = new double[w * h];
        Array.Fill(gScore, double.PositiveInfinity);
        gScore[startIdx] = 0;
        var cameFrom = new int[w * h];
        Array.Fill(cameFrom, -1);
        var visited = new bool[w * h];

        var open = new PriorityQueue<int, double>();
        open.Enqueue(startIdx, Heuristic(grid, sx, sy, ex, ey));

        while (open.Count > 0)
        {
            var current = open.Dequeue();
            if (visited[current]) continue;
            visited[current] = true;
            if (current == endIdx) break;

            var cx = current % w;
            var cy = current / w;

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    var nIdx = ny * w + nx;
                    if (visited[nIdx]) continue;

                    var stepDist = grid.CellSizeMeters * (dx != 0 && dy != 0 ? 1.4142135623730951 : 1.0);
                    var slope = Math.Abs(grid.Values[nIdx] - grid.Values[current]) / stepDist;
                    var cost = stepDist * (1.0 + slope * SlopePenaltyFactor);
                    if (grid.IsRiver(nx, ny))
                        cost += RiverCrossingCost;
                    if (grid.Values[nIdx] < 0f)
                        cost += SeaCrossingCost;

                    var tentative = gScore[current] + cost;
                    if (tentative < gScore[nIdx])
                    {
                        gScore[nIdx] = tentative;
                        cameFrom[nIdx] = current;
                        open.Enqueue(nIdx, tentative + Heuristic(grid, nx, ny, ex, ey));
                    }
                }
            }
        }

        if (double.IsPositiveInfinity(gScore[endIdx]))
            return null; // grid is disconnected from itself, shouldn't normally happen

        var cellPath = new List<int>();
        var node = endIdx;
        while (node != -1)
        {
            cellPath.Add(node);
            if (node == startIdx) break;
            node = cameFrom[node];
        }
        cellPath.Reverse();

        var worldPoints = new List<(double X, double Y)>(cellPath.Count);
        double length = 0;
        (double X, double Y)? prev = null;
        foreach (var idx in cellPath)
        {
            var gx = idx % w;
            var gy = idx / w;
            var wx = grid.OriginX + gx * grid.CellSizeMeters;
            var wy = grid.OriginY + gy * grid.CellSizeMeters;
            if (prev is { } p)
                length += Math.Sqrt((wx - p.X) * (wx - p.X) + (wy - p.Y) * (wy - p.Y));
            worldPoints.Add((wx, wy));
            prev = (wx, wy);
        }

        return new RoadPath(worldPoints, length);
    }

    private static (int Gx, int Gy) ToCell(TerrainHeightmap grid, double worldX, double worldY)
    {
        var gx = Math.Clamp((int)Math.Round((worldX - grid.OriginX) / grid.CellSizeMeters), 0, grid.Width - 1);
        var gy = Math.Clamp((int)Math.Round((worldY - grid.OriginY) / grid.CellSizeMeters), 0, grid.Height - 1);
        return (gx, gy);
    }

    private static double Heuristic(TerrainHeightmap grid, int gx, int gy, int ex, int ey)
    {
        var dx = (gx - ex) * grid.CellSizeMeters;
        var dy = (gy - ey) * grid.CellSizeMeters;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
