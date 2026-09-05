using GameEngineTools.World.Data;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class LocationProtectorTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, Func<int, int, float> heightAt)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = heightAt(x, y);

        return new TerrainHeightmap(
            Id: "test", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 10.0,
            Width: width, Height: height, Values: values);
    }

    [TestMethod]
    public void KeepLocationsDry_SubmergedLocation_IsRaisedAboveTheFloor()
    {
        var grid = MakeGrid(21, 21, (_, _) => -10f); // entirely underwater

        LocationProtector.KeepLocationsDry(grid, [(100.0, 100.0)], minDryElevationMeters: 2.0);

        Assert.IsTrue(grid.SampleAt(100, 100) >= 2.0);
    }

    [TestMethod]
    public void KeepLocationsDry_AlreadyDryLocation_IsLeftUntouched()
    {
        var grid = MakeGrid(21, 21, (_, _) => 50f);
        var before = (float[])grid.Values.Clone();

        LocationProtector.KeepLocationsDry(grid, [(100.0, 100.0)], minDryElevationMeters: 2.0);

        CollectionAssert.AreEqual(before, grid.Values);
    }

    [TestMethod]
    public void KeepLocationsDry_RaisesWithSoftFalloff_NotAnAbruptSpike()
    {
        var grid = MakeGrid(21, 21, (_, _) => -10f);

        LocationProtector.KeepLocationsDry(grid, [(100.0, 100.0)], minDryElevationMeters: 20.0, radiusCells: 5.0);

        var centerHeight = grid.SampleAt(100, 100);
        var edgeOfRadiusHeight = grid.SampleAt(100 + 5 * grid.CellSizeMeters, 100); // right at the falloff radius edge
        var farAwayHeight = grid.SampleAt(100 + 10 * grid.CellSizeMeters, 100); // outside the radius entirely

        Assert.IsTrue(centerHeight > edgeOfRadiusHeight, "Center should be raised more than the radius edge.");
        Assert.AreEqual(-10.0, farAwayHeight, 1e-3, "Terrain outside the radius must be untouched.");
    }

    [TestMethod]
    public void KeepLocationsDry_ExactSampledPointReachesTarget_EvenAcrossAnUnevenCellBoundary()
    {
        // Cells 0-2 are barely wet (-1m), cells 3-5 are a deep trough (-100m). Querying at a
        // fractional position (grid x = 2.37) means bilinear sampling blends a "shallow" corner
        // with a "deep" one — raising only the rounded-nearest corner (the old, buggy approach)
        // leaves the deep corner's contribution to the interpolation still very negative, so the
        // exact queried point could stay underwater even though the "center" cell looked fixed.
        var grid = MakeGrid(6, 2, (x, _) => x < 3 ? -1f : -100f);
        const double worldX = 23.7; // grid-cell x = 2.37 -> corners at x=2 (-1m) and x=3 (-100m)
        const double worldY = 5.0;
        const double minDry = 2.0;

        LocationProtector.KeepLocationsDry(grid, [(worldX, worldY)], minDryElevationMeters: minDry);

        // Tolerance is float32-storage precision, not algorithmic slack: the 4 corners are stored
        // as float32 before being bilinearly recombined, so a residual of a few 1e-6 is expected
        // even though the underlying math (uniform deficit across weights summing to 1) is exact.
        Assert.AreEqual(minDry, grid.SampleAt(worldX, worldY), 1e-4,
            "The exact queried point must reach exactly the target elevation, not just the nearest rounded cell.");
    }

    [TestMethod]
    public void KeepLocationsDry_MultipleLocations_EachHandledIndependently()
    {
        var grid = MakeGrid(41, 21, (_, _) => -5f);

        LocationProtector.KeepLocationsDry(grid, [(50.0, 100.0), (350.0, 100.0)], minDryElevationMeters: 3.0);

        Assert.IsTrue(grid.SampleAt(50, 100) >= 3.0);
        Assert.IsTrue(grid.SampleAt(350, 100) >= 3.0);
    }
}
