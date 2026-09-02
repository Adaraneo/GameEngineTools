using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileHydrologyTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, double cellSize, Func<int, int, float> heightFn)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = heightFn(x, y);
        return new TerrainHeightmap("test", 0.0, 0.0, cellSize, width, height, values);
    }

    [TestMethod]
    public void ComputeRiverMask_ReturnsMaskSameLengthAsGrid()
    {
        var grid = MakeGrid(10, 8, 1.0, (x, y) => (10 - x) * 2f);
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters());

        Assert.AreEqual(grid.Values.Length, mask.Length);
    }

    [TestMethod]
    public void ComputeRiverMask_UniformSlopeAlongOneRow_AccumulationGrowsLinearlyDownstream()
    {
        // Every row slopes the same way in +x (height decreases left to right), no variation in
        // y — every cell's single steepest-descent neighbor is straight ahead (x+1,y), so
        // accumulation at column x should be exactly x+1 (itself plus every column before it in
        // the same row) — a simple, hand-verifiable case for the D8 accumulation algorithm.
        const int width = 6, height = 3;
        var grid = MakeGrid(width, height, 1.0, (x, _) => (width - x) * 10f);

        // Threshold 1 marks every cell with any accumulation — use it to recover the exact
        // accumulation-derived pattern via a very low, then very high, threshold comparison.
        var maskThreshold1 = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 1));
        for (var i = 0; i < maskThreshold1.Length; i++)
            Assert.AreEqual(1, maskThreshold1[i], $"Threshold 1 should mark every cell (index {i}).");

        // Only the last column in each row has accumulation == width (everything upstream in
        // that row funnels through it) — a threshold of exactly `width` should mark ONLY those.
        var maskThresholdWidth = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: width));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = x == width - 1 ? 1 : 0;
                Assert.AreEqual(expected, maskThresholdWidth[y * width + x], $"Mismatch at ({x},{y}).");
            }
        }
    }

    [TestMethod]
    public void ComputeRiverMask_HigherThreshold_NeverProducesMoreRiverCellsThanLowerThreshold()
    {
        var rng = new Random(7);
        var grid = MakeGrid(15, 15, 1.0, (_, _) => (float)(rng.NextDouble() * 100));

        var low = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 2));
        var high = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 20));

        var lowCount = low.Count(b => b == 1);
        var highCount = high.Count(b => b == 1);
        Assert.IsTrue(highCount <= lowCount, $"Expected higher threshold ({highCount} cells) to mark no more cells than lower threshold ({lowCount}).");
    }

    [TestMethod]
    public void ComputeRiverMask_FlatGrid_NeverThrows_AndThresholdAboveOneMarksNothing()
    {
        var grid = MakeGrid(6, 6, 1.0, (_, _) => 5f); // perfectly flat — no strictly-downhill neighbor anywhere

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 2));

        Assert.IsTrue(mask.All(b => b == 0), "A flat grid has no drainage anywhere — accumulation should stay at 1 (the cell itself) everywhere.");
    }

    [TestMethod]
    public void ComputeRiverMask_SameInputs_IsDeterministic()
    {
        var rng = new Random(3);
        var grid = MakeGrid(20, 20, 2.5, (_, _) => (float)(rng.NextDouble() * 50));

        var a = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 10));
        var b = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 10));

        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeRiverMask_SingleBowlShapedValley_MarksTheValleyFloorNotTheRidges()
    {
        // A V-shaped cross-section (height rises with distance from the center column) — every
        // row's drainage should converge on column `center`, and nowhere else, once the
        // threshold is high enough to require the whole row's worth of accumulation.
        const int width = 11, height = 5;
        const int center = width / 2;
        var grid = MakeGrid(width, height, 1.0, (x, _) => Math.Abs(x - center) * 5f);

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: width));

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x == center) continue; // may or may not clear threshold depending on exact diagonal routing — checked separately below
                Assert.AreEqual(0, mask[y * width + x], $"Ridge cell ({x},{y}) should not be marked as river.");
            }
        }

        var centerColumnHasRiver = Enumerable.Range(0, height).Any(y => mask[y * width + center] == 1);
        Assert.IsTrue(centerColumnHasRiver, "Expected at least one river cell along the valley floor.");
    }
}
