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
    public void ComputeRiverMask_FlatGrid_NeverThrows_AndFillEpsilonStillResolvesADrainDirection()
    {
        // Perfectly flat: every real elevation is tied, so without FillDepressions' epsilon nudge
        // there'd be no strictly-downhill neighbor ANYWHERE and drainage would stay at 1 (itself)
        // everywhere — exactly the "flat plateau after filling" dead-end FillDepressions exists to
        // avoid (see its remarks). The epsilon gradient resolves a real drain direction from the
        // interior toward the grid's own boundary even here, so some accumulation > 1 must occur.
        var grid = MakeGrid(6, 6, 1.0, (_, _) => 5f);

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 2));

        Assert.IsTrue(mask.Any(b => b == 1), "Expected the epsilon-induced gradient to resolve at least one real drain path on a flat grid.");
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

    [TestMethod]
    public void ComputeRiverMask_LargeValleyFunnel_DoesNotSmearFlowAsAWideSheet()
    {
        // Regression test for a real generation defect (found live on production terrain, seed
        // -1384538600 lat~7.5 lon~7.5): FillDepressions alone guarantees SOME strictly-downhill
        // neighbor everywhere, but on a genuinely low-relief valley floor its epsilon ring radiates
        // outward from the whole flooded boundary at once, so flow smears as a wide sheet across
        // most of the flat instead of converging — confirmed by temporarily bypassing
        // ResolveFlats's caller (`var routed = filled;`) on this exact scenario, which drove the
        // marked fraction from ~0.4% up to ~23% of the valley floor. ResolveFlats fixes it by
        // funneling flow toward the valley's one open (south) side instead of letting it spread.
        const int width = 200, height = 200;
        var values = new float[width * height];
        var isWall = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Irregular wall along north, east AND west, leaving only south open — a bounded
                // valley, not a synthetic straight wall (which stays translation-symmetric and
                // barely exercises the away-from-wall/toward-outlet bias at all).
                var northWallDepth = 4 + (int)(3 * Math.Sin(x * 0.7) + 3 * Math.Sin(x * 0.31 + 1.7));
                var westWallDepth = 4 + (int)(3 * Math.Sin(y * 0.53) + 3 * Math.Sin(y * 0.19 + 0.9));
                var eastWallDepth = 4 + (int)(3 * Math.Sin(y * 0.41 + 2.1) + 3 * Math.Sin(y * 0.27));
                var wall = y < northWallDepth || x < westWallDepth || x >= width - eastWallDepth;
                isWall[y * width + x] = wall;
                values[y * width + x] = wall
                    ? 100f
                    // Tiny deterministic per-cell jitter, well below FillEpsilon-multiple scale,
                    // simulating realistic post-erosion float noise on otherwise near-flat ground.
                    : 10f + ((x * 37 + y * 17) % 5) * 0.0001f;
            }
        }

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(FlowAccumulationThreshold: 50));

        var flatCells = 0;
        var riverCells = 0;
        for (var i = 0; i < isWall.Length; i++)
        {
            if (isWall[i]) continue;
            flatCells++;
            if (mask[i] != 0) riverCells++;
        }

        var pct = 100.0 * riverCells / flatCells;
        Assert.IsTrue(pct < 5.0, $"Expected the valley floor to funnel into a thin channel network " +
            $"(<5% marked), not smear as a wide sheet — got {pct:F1}% ({riverCells}/{flatCells}).");
    }
}
