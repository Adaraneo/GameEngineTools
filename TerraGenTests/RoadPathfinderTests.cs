using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class RoadPathfinderTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, Func<int, int, float> heightAt, byte[]? riverMask = null)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = heightAt(x, y);

        return new TerrainHeightmap(
            Id: "test",
            OriginX: 0.0,
            OriginY: 0.0,
            CellSizeMeters: 10.0,
            Width: width,
            Height: height,
            Values: values,
            RiverMask: riverMask);
    }

    [TestMethod]
    public void FindPath_SameStartAndEnd_ReturnsZeroLength()
    {
        var grid = MakeGrid(20, 20, (_, _) => 0f);

        var path = RoadPathfinder.FindPath(grid, 50, 50, 50, 50);

        Assert.IsNotNull(path);
        Assert.AreEqual(0.0, path!.LengthMeters);
    }

    [TestMethod]
    public void FindPath_FlatTerrain_LengthCloseToStraightLineDistance()
    {
        var grid = MakeGrid(30, 30, (_, _) => 0f);

        var path = RoadPathfinder.FindPath(grid, 0, 0, 200, 0);

        Assert.IsNotNull(path);
        Assert.IsTrue(path!.LengthMeters >= 200.0 - 1e-6);
        Assert.IsTrue(path.LengthMeters <= 220.0, $"Expected close to 200m on flat terrain, got {path.LengthMeters}.");
    }

    [TestMethod]
    public void FindPath_SteepRidgeWithLowPass_DetoursThroughThePassInsteadOfClimbing()
    {
        const int width = 30, gridHeight = 5;
        float HeightAt(int x, int y) => x is >= 13 and <= 16 && y != 1 ? 500f : 0f;

        var ridgeGrid = MakeGrid(width, gridHeight, HeightAt);
        var flatGrid = MakeGrid(width, gridHeight, (_, _) => 0f);

        var ridgePath = RoadPathfinder.FindPath(ridgeGrid, 0, 20, 290, 20); // row 2
        var flatPath = RoadPathfinder.FindPath(flatGrid, 0, 20, 290, 20);

        Assert.IsNotNull(ridgePath);
        Assert.IsNotNull(flatPath);
        Assert.IsTrue(ridgePath!.LengthMeters > flatPath!.LengthMeters + 5,
            $"Expected a real geometric detour toward the low pass: ridge={ridgePath.LengthMeters}, flat={flatPath.LengthMeters}.");

        var climbsRidge = ridgePath.WorldPoints.Any(p =>
        {
            var gx = (int)Math.Round(p.X / ridgeGrid.CellSizeMeters);
            var gy = (int)Math.Round(p.Y / ridgeGrid.CellSizeMeters);
            return gx is >= 13 and <= 16 && HeightAt(gx, gy) > 100f;
        });
        Assert.IsFalse(climbsRidge, "Expected the path to detour through the low pass instead of climbing the ridge.");
    }

    [TestMethod]
    public void FindPath_RiverCrossing_StillFindsAPath()
    {
        const int width = 30, gridHeight = 10;
        var mask = new byte[width * gridHeight];
        for (var y = 0; y < gridHeight; y++)
            mask[y * width + 15] = 1;

        var grid = MakeGrid(width, gridHeight, (_, _) => 0f, mask);

        var path = RoadPathfinder.FindPath(grid, 0, 50, 290, 50);

        Assert.IsNotNull(path);
        Assert.IsTrue(path!.LengthMeters > 0);
    }

    [TestMethod]
    public void FindPath_AvoidableRiverGap_DetoursThroughDryGapInsteadOfCrossing()
    {
        const int width = 30, gridHeight = 5;
        var mask = new byte[width * gridHeight];
        for (var y = 0; y < gridHeight; y++)
        {
            if (y != 1) mask[y * width + 15] = 1;
        }

        var riverGrid = MakeGrid(width, gridHeight, (_, _) => 0f, mask);
        var flatGrid = MakeGrid(width, gridHeight, (_, _) => 0f);

        var riverPath = RoadPathfinder.FindPath(riverGrid, 0, 20, 290, 20); // row 2
        var flatPath = RoadPathfinder.FindPath(flatGrid, 0, 20, 290, 20);

        Assert.IsNotNull(riverPath);
        Assert.IsNotNull(flatPath);
        Assert.IsTrue(riverPath!.LengthMeters > flatPath!.LengthMeters + 5,
            $"Expected a real geometric detour around the river: river={riverPath.LengthMeters}, flat={flatPath.LengthMeters}.");

        var crossesRiverBand = riverPath.WorldPoints.Any(p =>
        {
            var gx = (int)Math.Round(p.X / riverGrid.CellSizeMeters);
            var gy = (int)Math.Round(p.Y / riverGrid.CellSizeMeters);
            return gx == 15 && riverGrid.IsRiver(gx, gy);
        });
        Assert.IsFalse(crossesRiverBand, "Expected the path to route through the dry gap, not cross the river band.");
    }

    [TestMethod]
    public void FindPath_AvoidableSea_DetoursAroundInsteadOfCrossing()
    {
        const int width = 30, gridHeight = 5;
        float HeightAt(int x, int y) => x == 15 && y != 1 ? -10f : 0f;

        var seaGrid = MakeGrid(width, gridHeight, HeightAt);
        var flatGrid = MakeGrid(width, gridHeight, (_, _) => 0f);

        var seaPath = RoadPathfinder.FindPath(seaGrid, 0, 20, 290, 20); // row 2
        var flatPath = RoadPathfinder.FindPath(flatGrid, 0, 20, 290, 20);

        Assert.IsNotNull(seaPath);
        Assert.IsNotNull(flatPath);
        Assert.IsTrue(seaPath!.LengthMeters > flatPath!.LengthMeters + 5,
            $"Expected a real geometric detour around the sea: sea={seaPath.LengthMeters}, flat={flatPath.LengthMeters}.");

        var crossesSeaBand = seaPath.WorldPoints.Any(p =>
        {
            var gx = (int)Math.Round(p.X / seaGrid.CellSizeMeters);
            var gy = (int)Math.Round(p.Y / seaGrid.CellSizeMeters);
            return gx == 15 && HeightAt(gx, gy) < 0f;
        });
        Assert.IsFalse(crossesSeaBand, "Expected the path to route through the dry gap, not cross the sea band.");
    }

    [TestMethod]
    public void FindPath_UnavoidableSea_StillFindsAPath()
    {
        const int width = 30, gridHeight = 10;
        float HeightAt(int x, int _) => x == 15 ? -10f : 0f;

        var grid = MakeGrid(width, gridHeight, HeightAt);

        var path = RoadPathfinder.FindPath(grid, 0, 50, 290, 50);

        Assert.IsNotNull(path);
        Assert.IsTrue(path!.LengthMeters > 0);
    }
}
