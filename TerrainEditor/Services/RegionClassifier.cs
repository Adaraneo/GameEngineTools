using GameEngineTools.World.Data;

namespace TerrainEditor.Services;

/// <summary>
/// Suggests a Region label for a location from its terrain context (elevation, proximity to
/// water) — a starting classification the designer can freely override by hand afterward, not
/// an authoritative simulation-affecting label.
/// </summary>
public static class RegionClassifier
{
    public sealed record Parameters(
        double MountainThresholdMeters = 300.0,
        double HillThresholdMeters = 100.0,
        /// <summary>How many grid cells away still counts as "near" a river/lake/coast.</summary>
        double WaterProximityCells = 3.0);

    public static string Classify(TerrainHeightmap grid, double worldX, double worldY, Parameters parameters)
    {
        var height = grid.SampleAt(worldX, worldY);
        var nearWater = IsNearWater(grid, worldX, worldY, parameters.WaterProximityCells);

        if (height < 0) return "Coast";
        if (nearWater) return "Riverside";
        if (height >= parameters.MountainThresholdMeters) return "Mountains";
        if (height >= parameters.HillThresholdMeters) return "Hills";
        return "Lowlands";
    }

    private static bool IsNearWater(TerrainHeightmap grid, double worldX, double worldY, double radiusCells)
    {
        var gx = (int)Math.Round((worldX - grid.OriginX) / grid.CellSizeMeters);
        var gy = (int)Math.Round((worldY - grid.OriginY) / grid.CellSizeMeters);
        var r = (int)Math.Ceiling(radiusCells);

        for (var dy = -r; dy <= r; dy++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > radiusCells * radiusCells) continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= grid.Width || ny < 0 || ny >= grid.Height) continue;

                if (grid.IsRiver(nx, ny)) return true;
                if (grid.Values[ny * grid.Width + nx] < 0) return true; // adjacent to the sea
            }
        }
        return false;
    }
}
