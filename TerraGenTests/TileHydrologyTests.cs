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
    public void ComputeRiverMask_UniformSlopeAlongOneRow_AreaSlopeProductGrowsLinearlyDownstream()
    {
        // Every row slopes the same way in +x (height decreases left to right by 10 per cell, so
        // real slope is exactly 10 everywhere), no variation in y — every cell's single steepest-
        // descent neighbor is straight ahead (x+1,y), so contributing area at column x is exactly
        // (x+1) cells and the area×slope product is (x+1)*10 — a simple, hand-verifiable case. The
        // LAST column (x=width-1) has no downstream neighbor of its own to judge (grid edge), but
        // once ANY upstream column becomes a channel, downstream propagation (see
        // ComputeRiverMask's remarks) carries that mark all the way to the edge regardless — a
        // channel doesn't stop existing just because there's no further column left to evaluate.
        const int width = 6, height = 3;
        var grid = MakeGrid(width, height, 1.0, (x, _) => (width - x) * 10f);

        // The minimum possible product (column 0: area=1 cell, slope=10) marks every column,
        // including the last one via propagation from column width-2.
        var maskLow = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 10.0));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                Assert.AreEqual(1, maskLow[y * width + x], $"Mismatch at ({x},{y}).");
        }

        // The maximum possible product among cells that can judge their OWN slope is at column
        // width-2 (area=width-1, slope=10) — a threshold set exactly there marks only that column
        // AND whatever it propagates to (here, just the last column, its sole downstream cell).
        var maskHigh = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: (width - 1) * 10.0));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = x >= width - 2 ? 1 : 0;
                Assert.AreEqual(expected, maskHigh[y * width + x], $"Mismatch at ({x},{y}).");
            }
        }
    }

    [TestMethod]
    public void ComputeRiverMask_HigherThreshold_NeverProducesMoreRiverCellsThanLowerThreshold()
    {
        var rng = new Random(7);
        var grid = MakeGrid(15, 15, 1.0, (_, _) => (float)(rng.NextDouble() * 100));

        var low = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 20));
        var high = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 400));

        var lowCount = low.Count(b => b == 1);
        var highCount = high.Count(b => b == 1);
        Assert.IsTrue(highCount <= lowCount, $"Expected higher threshold ({highCount} cells) to mark no more cells than lower threshold ({lowCount}).");
    }

    [TestMethod]
    public void ComputeRiverMask_FlatGrid_NeverThrows_AndMarksNothing()
    {
        // Perfectly flat: FillDepressions' epsilon nudge still resolves SOME drain direction so
        // accumulation can route at all (see FillDepressions/ResolveFlats), but the channel-
        // initiation slope test is measured on the RAW elevation, not that synthetic epsilon
        // gradient — and raw slope really is exactly 0 everywhere on a flat grid. No cell should
        // ever cross a positive threshold, matching real hydrology: flat ground doesn't
        // spontaneously carve a channel, no matter how much area drains across it.
        var grid = MakeGrid(6, 6, 1.0, (_, _) => 5f);

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 0.001));

        Assert.IsTrue(mask.All(b => b == 0), "A perfectly flat grid has zero real slope everywhere and should never be marked as river.");
    }

    [TestMethod]
    public void ComputeRiverMask_SameInputs_IsDeterministic()
    {
        var rng = new Random(3);
        var grid = MakeGrid(20, 20, 2.5, (_, _) => (float)(rng.NextDouble() * 50));

        var a = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 30));
        var b = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 30));

        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeRiverMask_SingleBowlShapedValley_MarksTheValleyFloorNotTheRidges()
    {
        // A V-shaped cross-section (height rises with distance from the center column), PLUS a
        // gentle downhill trend along y so the valley floor actually has somewhere to drain — a
        // valley whose floor is perfectly level along its own length (no y-term at all) has zero
        // REAL slope there under the new area×slope criterion (which measures slope on raw
        // elevation, not a synthetic tie-break), so it would never channelize, same as any other
        // flat ground; real valley floors slope toward their outlet, which is what the small y-term
        // models. The cross-valley slope (5/cell) still dominates the y-trend (0.3/cell) enough
        // that ridge cells still drain toward the center first, same as before.
        // The outer two ridge columns never receive any upstream contribution (nothing further out
        // to feed them — area is always exactly 1 cell there), so even their steep local
        // cross-valley slope (5, an exact value here: the y-term cancels in a same-row horizontal
        // comparison) can never cross a threshold set above 1*5 on its own. Columns BETWEEN the
        // ridge and center do accumulate some lateral inflow, so this only asserts the clean-cut
        // case (isolated steep cell, no area behind it, must NOT count) rather than every column.
        const int width = 11, height = 5;
        const int center = width / 2;
        var grid = MakeGrid(width, height, 1.0, (x, y) => Math.Abs(x - center) * 5f + y * 0.3f);

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 8.0));

        for (var y = 0; y < height; y++)
        {
            Assert.AreEqual(0, mask[y * width + 0], $"Outer ridge cell (0,{y}) should not be marked (area=1 alone can't cross the threshold).");
            Assert.AreEqual(0, mask[y * width + (width - 1)], $"Outer ridge cell ({width - 1},{y}) should not be marked.");
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
        // Threshold set low specifically to still exercise this on the area×slope criterion despite
        // this floor's real relief being tiny (deliberately, to simulate realistic post-erosion
        // float noise, no uniform trend) — a higher, realistic threshold would trivially mark
        // ~nothing here and not actually test the shape-of-the-network regression this guards
        // against. Downstream propagation (see ComputeRiverMask's remarks) means the assertion below
        // only needs "not a wide sheet", not "not connected" — propagation is what MAKES a marked
        // stretch connected now, that's not the failure mode this test targets.
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
                    // Tiny deterministic per-cell jitter simulating realistic post-erosion float
                    // noise on otherwise near-flat ground — no uniform trend, so ANY marking here
                    // has to come from the noise itself plus accumulated area, not a built-in slope.
                    : 10f + ((x * 37 + y * 17) % 5) * 0.0001f;
            }
        }

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 1.0));

        var flatCells = 0;
        var riverCells = 0;
        for (var i = 0; i < isWall.Length; i++)
        {
            if (isWall[i]) continue;
            flatCells++;
            if (mask[i] != 0) riverCells++;
        }

        var pct = 100.0 * riverCells / flatCells;
        Assert.IsTrue(pct is > 0.0 and < 10.0, $"Expected the valley floor to funnel into a thin channel network " +
            $"(0%<pct<10%, non-trivial but not a wide sheet), not smear as a wide sheet — got {pct:F2}% ({riverCells}/{flatCells}).");
    }

    [TestMethod]
    public void ComputeRiverMask_ChannelContinuesThroughLocallyFlatSteps_DoesNotFlicker()
    {
        // Regression test for a real generation defect (found live via TileHydrology's internal
        // ComputeDiagnostics on production terrain): the area×slope criterion is for CHANNEL
        // INITIATION, not a per-point validity check repeated at every cell along an established
        // channel — evaluated pointwise, it flickers wherever two adjacent post-erosion cells
        // happen to have a real drop that rounds to ~0 (meter-scale erosion-granularity noise, not
        // the river actually leveling out). Confirmed live: a single mainstem trunk with
        // accumulation in the thousands marked barely half its own cells, on/off roughly every
        // other one. ComputeRiverMask now propagates a mark downstream once a channel is
        // established, so every cell after the first that clears the threshold stays marked
        // regardless of locally flat noise.
        const int width = 20;
        var values = new float[width];
        // Steps at 8, 9, 13, 14, 15 have ZERO drop to the next column (a flat run of several
        // cells in a row), simulating post-erosion quantization noise on an otherwise real slope.
        var flatSteps = new HashSet<int> { 8, 9, 13, 14, 15 };
        var h = (float)width;
        for (var x = 0; x < width; x++)
        {
            values[x] = h;
            if (!flatSteps.Contains(x)) h -= 1f;
        }
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 1.0, width, 1, values);

        // Threshold low enough the channel initiates almost immediately (column 1: area=2 cells,
        // slope=1 -> product=2) — every flat step's OWN local slope is exactly 0, which would fail
        // ANY positive threshold if evaluated pointwise instead of propagated.
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(AreaSlopeThreshold: 2.0));

        for (var x = 1; x < width - 1; x++)
            Assert.AreEqual(1, mask[x], $"Column {x} (flatStep={flatSteps.Contains(x)}) should stay marked — " +
                "an established channel must not un-mark itself over a locally flat step.");
    }
}
