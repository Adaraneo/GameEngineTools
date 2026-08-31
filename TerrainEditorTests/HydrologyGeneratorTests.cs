using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class HydrologyGeneratorTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, Func<int, int, float> heightAt)
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
            RiverMask: new byte[width * height]);
    }

    /// <summary>
    /// Regression test for the exact bug reported: on a freshly-created (all-zero, perfectly
    /// flat) grid — the default TerrainEditor.MainWindow.CreateDefaultGrid state a user would
    /// actually test the feature against before painting any terrain — a spring must still
    /// trace visibly instead of stopping after a single cell.
    /// </summary>
    [TestMethod]
    public void TraceFromSpring_FlatTerrain_TracesMoreThanOneCell()
    {
        var grid = MakeGrid(20, 20, (_, _) => 0f);

        var traced = HydrologyGenerator.TraceFromSpring(grid, springWorldX: 100, springWorldY: 100);

        Assert.IsTrue(traced > 1, $"Expected the trace to progress past the spring cell on flat terrain, got {traced}.");
    }

    /// <summary>
    /// Carving the spring cell below 0m must never itself be mistaken for "reached the sea" —
    /// only a cell that was ALREADY at/below sea level before this trace touched it counts.
    /// </summary>
    [TestMethod]
    public void TraceFromSpring_SpringAtSeaLevel_DoesNotImmediatelyStop()
    {
        // Spring exactly at 0m — carving it (-1.5m) must not short-circuit the sea check.
        var grid = MakeGrid(10, 10, (_, _) => 0f);

        var traced = HydrologyGenerator.TraceFromSpring(grid, springWorldX: 50, springWorldY: 50);

        Assert.IsTrue(traced > 1);
    }

    [TestMethod]
    public void TraceFromSpring_SlopedTerrain_FollowsSteepestDescentToTheSea()
    {
        // Height decreases 5m per cell along X: 50 at x=0, 0 at x=10, -5 at x=11.
        const int width = 15, heightRows = 3;
        var grid = MakeGrid(width, heightRows, (x, _) => 50f - 5f * x);

        var traced = HydrologyGenerator.TraceFromSpring(grid, springWorldX: 0, springWorldY: 10);

        // x=0..10 are dry (>=0, all traced+carved), x=11 is the first cell already <0 — traced
        // and then the trace stops. That's 12 cells (x=0 through x=11 inclusive).
        Assert.AreEqual(12, traced);

        for (var x = 0; x <= 11; x++)
            Assert.IsTrue(grid.IsRiver(x, 1), $"Expected (x={x}, y=1) to be marked as river.");

        // Never drifted off its row (height has no y-gradient, so E is always the unique
        // steepest-descent direction) and never reached cells beyond the coastline.
        Assert.IsFalse(grid.IsRiver(0, 0));
        Assert.IsFalse(grid.IsRiver(0, 2));
        Assert.IsFalse(grid.IsRiver(12, 1));
    }

    [TestMethod]
    public void TraceFromSpring_ReachedSeaCell_IsNotCarvedFurther()
    {
        const int width = 15, heightRows = 3;
        var grid = MakeGrid(width, heightRows, (x, _) => 50f - 5f * x);

        HydrologyGenerator.TraceFromSpring(grid, springWorldX: 0, springWorldY: 10);

        // x=11 started at -5m and must be left exactly as found (no further carve applied
        // once the "already below sea level" check fires).
        Assert.AreEqual(-5f, grid.Values[1 * width + 11], 1e-6);
    }

    [TestMethod]
    public void TraceFromSpring_Basin_StopsAtLocalMinimumInsteadOfLoopingForever()
    {
        // A paraboloid bowl, minimum (0) at the center — nowhere lower exists once you reach it.
        const int size = 11;
        var grid = MakeGrid(size, size, (x, y) => (float)((x - 5) * (x - 5) + (y - 5) * (y - 5)));

        var traced = HydrologyGenerator.TraceFromSpring(grid, springWorldX: 0, springWorldY: 0);

        Assert.IsTrue(traced > 0);
        Assert.IsTrue(traced < size * size, "Trace must terminate well before exhausting the whole grid.");
        Assert.IsTrue(grid.IsRiver(5, 5), "Expected the trace to reach the basin's true minimum at the center.");
    }

    [TestMethod]
    public void TraceFromSpring_MarksRiverMaskButLeavesUnvisitedCellsAlone()
    {
        const int width = 15, heightRows = 3;
        var grid = MakeGrid(width, heightRows, (x, _) => 50f - 5f * x);

        HydrologyGenerator.TraceFromSpring(grid, springWorldX: 0, springWorldY: 10);

        Assert.IsFalse(grid.IsRiver(14, 1)); // far downstream cell, never reached
        // Untouched cells keep their original (uncarved) elevation.
        Assert.AreEqual(50f - 5f * 14, grid.Values[1 * width + 14], 1e-6);
    }
}
