using GameEngineTools.World.Data;
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
    public void ComputeRiverMask_UniformSlopeAlongOneRow_AreaSlopeSquaredProductGrowsLinearlyDownstream()
    {
        // Every row slopes the same way in +x (height decreases left to right by 10 per cell, so
        // real slope is exactly 10 everywhere), no variation in y — every cell's single steepest-
        // descent neighbor is straight ahead (x+1,y), so contributing area at column x is exactly
        // (x+1) cells and the area×slope² product is (x+1)*100 (slope=10, squared=100) — a simple,
        // hand-verifiable case. The LAST column (x=width-1) has no downstream neighbor of its own to
        // judge (grid edge), but once ANY upstream column becomes a channel, downstream propagation
        // (see ComputeRiverMask's remarks) carries that mark all the way to the edge regardless — a
        // channel doesn't stop existing just because there's no further column left to evaluate.
        const int width = 6, height = 3;
        var grid = MakeGrid(width, height, 1.0, (x, _) => (width - x) * 10f);

        // The minimum possible product (column 0: area=1 cell, slope²=100) marks every column,
        // including the last one via propagation from column width-2.
        var maskLow = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 100.0));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                Assert.AreEqual(1, maskLow[y * width + x], $"Mismatch at ({x},{y}).");
        }

        // The maximum possible product among cells that can judge their OWN slope is at column
        // width-2 (area=width-1, slope²=100) — a threshold set exactly there marks only that column
        // AND whatever it propagates to (here, just the last column, its sole downstream cell).
        var maskHigh = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: (width - 1) * 100.0));
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
    public void ComputeRiverMask_LowSlopeHighArea_RequiresMuchLargerAreaThanLinearModelWould()
    {
        // Regression test for the exact bug this squared-slope fix corrects: under the OLD (buggy)
        // linear model (area×slope), halving the slope only requires DOUBLING the area to still
        // qualify for the same threshold. Under the correct area×slope² model, halving the slope
        // requires QUADRUPLING the area — a regression back to the unsquared formula would mark the
        // "doubled area" case below, which the correct model must NOT.
        //
        // Two independent uniform-slope lines (steep at slope=10, shallow at exactly half, slope=5),
        // both starting fresh at column 0 (area=1) so a column's own area is just (column index + 1)
        // — the same simple, hand-verifiable setup the uniform-slope test above uses. Threshold is
        // fixed at 1000 = area(10)×slope(10)² (the steep line's OWN qualifying point, at column 9,
        // area=10) for both lines, so the shallow line's columns are judged against the exact same
        // bar the steep line needs to clear.
        const int width = 45, height = 1;
        const double threshold = 1000.0; // = 10 (area) * 10² (slope), the steep line's own bar

        TerrainHeightmap MakeUniformSlopeLine(float slope)
        {
            var values = new float[width];
            var h = 10_000f;
            for (var x = 0; x < width; x++)
            {
                values[x] = h;
                h -= slope;
            }
            return new TerrainHeightmap("test", 0.0, 0.0, 1.0, width, height, values);
        }

        var steepMask = TileHydrology.ComputeRiverMask(MakeUniformSlopeLine(10f), new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: threshold));
        // Column 9: area=10, slope=10 -> 10*100=1000 >= threshold — qualifies, confirming the
        // threshold is exactly where this line's own bar sits (not accidentally too high/low).
        Assert.AreEqual(1, steepMask[9], "Steep line's own qualifying point (area=10, slope=10) should mark at threshold=1000.");

        var shallowMask = TileHydrology.ComputeRiverMask(MakeUniformSlopeLine(5f), new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: threshold));
        // Column 19: area=20 (DOUBLE the steep line's qualifying area), slope=5 (HALF the steep
        // line's slope) -> 20*25=500 < 1000 — must NOT qualify. A linear (unsquared) model would
        // instead compare 20*5=100 against a linear-model threshold of 10*10=100 and WRONGLY mark
        // it (equal, not less) — this is exactly the case a regression to the linear formula would
        // flip.
        Assert.AreEqual(0, shallowMask[19], "Doubled area at half the slope must NOT qualify under the correct squared model.");
        // Column 39: area=40 (QUADRUPLE the steep line's qualifying area), slope=5 -> 40*25=1000 >=
        // threshold — DOES qualify, confirming quadrupling (not doubling) area is what compensates
        // for halving the slope.
        Assert.AreEqual(1, shallowMask[39], "Quadrupled area at half the slope SHOULD qualify under the correct squared model.");
    }

    [TestMethod]
    public void ComputeRiverMask_HigherThreshold_NeverProducesMoreRiverCellsThanLowerThreshold()
    {
        var rng = new Random(7);
        var grid = MakeGrid(15, 15, 1.0, (_, _) => (float)(rng.NextDouble() * 100));

        var low = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 20));
        var high = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 400));

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

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 0.001));

        Assert.IsTrue(mask.All(b => b == 0), "A perfectly flat grid has zero real slope everywhere and should never be marked as river.");
    }

    [TestMethod]
    public void ComputeRiverMask_SameInputs_IsDeterministic()
    {
        var rng = new Random(3);
        var grid = MakeGrid(20, 20, 2.5, (_, _) => (float)(rng.NextDouble() * 50));

        var a = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 30));
        var b = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 30));

        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeRiverMask_SingleBowlShapedValley_MarksTheValleyFloorNotTheRidges()
    {
        // A V-shaped cross-section (height rises with distance from the center column), PLUS a
        // gentle downhill trend along y so the valley floor actually has somewhere to drain — a
        // valley whose floor is perfectly level along its own length (no y-term at all) has zero
        // REAL slope there under the area×slope² criterion (which measures slope on raw elevation,
        // not a synthetic tie-break), so it would never channelize, same as any other flat ground;
        // real valley floors slope toward their outlet, which is what the small y-term models. The
        // cross-valley slope (5/cell) still dominates the y-trend (0.3/cell) enough that ridge cells
        // still drain toward the center first, same as before.
        // The outer two ridge columns never receive any upstream contribution (nothing further out
        // to feed them — area is always exactly 1 cell there), so even their steep local
        // cross-valley slope (5, an exact value here: the y-term cancels in a same-row horizontal
        // comparison) can never cross a threshold set above 1*5² = 25 on its own. Columns BETWEEN
        // the ridge and center do accumulate some lateral inflow, so this only asserts the clean-cut
        // case (isolated steep cell, no area behind it, must NOT count) rather than every column.
        const int width = 11, height = 5;
        const int center = width / 2;
        var grid = MakeGrid(width, height, 1.0, (x, y) => Math.Abs(x - center) * 5f + y * 0.3f);

        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 30.0));

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
        // Threshold set low specifically to still exercise this on the area×slope² criterion despite
        // this floor's real relief being tiny (deliberately, to simulate realistic post-erosion
        // float noise, no uniform trend) — a higher, realistic threshold would trivially mark
        // ~nothing here and not actually test the shape-of-the-network regression this guards
        // against. Retuned for the squared model (typical noise slope here is well under 1, so
        // squaring shrinks the product by several more orders of magnitude than the old linear
        // threshold assumed). Downstream propagation (see ComputeRiverMask's remarks) means the
        // assertion below only needs "not a wide sheet", not "not connected" — propagation is what
        // MAKES a marked stretch connected now, that's not the failure mode this test targets.
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
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 0.001));

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
        var mask = TileHydrology.ComputeRiverMask(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 2.0));

        for (var x = 1; x < width - 1; x++)
            Assert.AreEqual(1, mask[x], $"Column {x} (flatStep={flatSteps.Contains(x)}) should stay marked — " +
                "an established channel must not un-mark itself over a locally flat step.");
    }

    [TestMethod]
    public void ComputeDiagnostics_StrahlerOrder_HeadwatersAreAlwaysOrderOne()
    {
        // Definitional: a channel head (a river cell with no river cell flowing INTO it) has never
        // had a tributary merge into it, so by Strahler's own definition it must be order 1 — true
        // regardless of terrain shape, so this checks the property directly rather than hand-predicting
        // values for one specific hand-built confluence.
        var (mask, _, _, downstream, order, strahlerOrder, _) = ComputeOnAConfluenceRichGrid();
        var upstreamCount = BuildUpstreamCounts(mask, downstream, order.Length);

        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            if (upstreamCount[i] == 0)
                Assert.AreEqual(1, strahlerOrder[i], $"Headwater cell {i} should be Strahler order 1.");
        }
    }

    [TestMethod]
    public void ComputeDiagnostics_StrahlerOrder_NeverDecreasesDownstream()
    {
        // Definitional: Strahler order can only stay the same or increase moving downstream —
        // merging a smaller tributary into a bigger reach never shrinks the bigger reach's own
        // classification.
        var (mask, _, _, downstream, _, strahlerOrder, _) = ComputeOnAConfluenceRichGrid();

        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            var next = downstream[i];
            if (next < 0 || mask[next] == 0) continue;
            Assert.IsTrue(strahlerOrder[next] >= strahlerOrder[i],
                $"Order dropped from {strahlerOrder[i]} at {i} to {strahlerOrder[next]} downstream at {next} — Strahler order must never decrease.");
        }
    }

    [TestMethod]
    public void ComputeDiagnostics_StrahlerOrder_OnlyIncreasesWhenTwoEqualOrdersMerge()
    {
        // Definitional: at a confluence (2+ river cells flowing into the same cell), the merged
        // cell's order is max(upstream orders)+1 ONLY if that max is shared by at least two
        // tributaries; a single dominant tributary absorbing a smaller one keeps its own order
        // unchanged — a big river doesn't bump up in classification just because a trickle joins it.
        var (mask, _, _, downstream, order, strahlerOrder, _) = ComputeOnAConfluenceRichGrid();
        var upstream = BuildUpstreamLists(mask, downstream, order.Length);

        var confluencesChecked = 0;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            var ups = upstream[i];
            if (ups.Count < 2) continue;

            var upstreamOrders = ups.Select(u => (int)strahlerOrder[u]).ToList();
            var maxOrder = upstreamOrders.Max();
            var countAtMax = upstreamOrders.Count(o => o == maxOrder);
            var expected = countAtMax >= 2 ? maxOrder + 1 : maxOrder;

            Assert.AreEqual(expected, strahlerOrder[i],
                $"Confluence at {i} merging orders [{string.Join(",", upstreamOrders)}] should be order {expected}.");
            confluencesChecked++;
        }

        Assert.IsTrue(confluencesChecked > 0, "Test terrain should contain at least one real confluence to check.");
    }

    [TestMethod]
    public void ComputeDiagnostics_ShreveMagnitude_HeadwatersAreAlwaysMagnitudeOne()
    {
        // Definitional (Shreve 1966): a channel head with no river tributary feeding it has no
        // upstream contributor to sum, so its magnitude defaults to 1 — true regardless of terrain
        // shape, same property the Strahler headwater test above checks for order.
        var (mask, _, _, downstream, order, _, shreveMagnitude) = ComputeOnAConfluenceRichGrid();
        var upstreamCount = BuildUpstreamCounts(mask, downstream, order.Length);

        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            if (upstreamCount[i] == 0)
                Assert.AreEqual(1, shreveMagnitude[i], $"Headwater cell {i} should have Shreve magnitude 1.");
        }
    }

    [TestMethod]
    public void ComputeDiagnostics_ShreveMagnitude_IsAdditiveAtConfluences_UnlikeStrahlerOrder()
    {
        // Definitional (Shreve 1966): at EVERY confluence, magnitude is the SUM of every upstream
        // river contributor's own magnitude — never capped, never conditional on the contributors
        // being equal, unlike Strahler order (see the order-only-increases-on-equal-merge test
        // above). This checks the additive rule at every real confluence in the same
        // confluence-rich basin the Strahler tests use, and specifically locates at least one
        // confluence where the two rules diverge (unequal-order tributaries) to prove magnitude
        // really is tracking something different from order, not just coincidentally agreeing.
        var (mask, _, _, downstream, order, strahlerOrder, shreveMagnitude) = ComputeOnAConfluenceRichGrid();
        var upstream = BuildUpstreamLists(mask, downstream, order.Length);

        var confluencesChecked = 0;
        var foundDivergentConfluence = false;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;
            var ups = upstream[i];
            if (ups.Count < 2) continue;

            var expectedMagnitude = ups.Sum(u => shreveMagnitude[u]);
            Assert.AreEqual(expectedMagnitude, shreveMagnitude[i],
                $"Confluence at {i} merging magnitudes [{string.Join(",", ups.Select(u => shreveMagnitude[u]))}] should sum to {expectedMagnitude}.");
            confluencesChecked++;

            // Reproduce the Strahler rule exactly (same formula the OnlyIncreasesWhenTwoEqualOrdersMerge
            // test above already validates independently) so this test can compare it directly
            // against what magnitude does at the SAME confluence, rather than re-deriving its own
            // (and getting it wrong for a 3+-way confluence where two contributors happen to tie
            // at the max alongside a third that doesn't).
            var upstreamOrders = ups.Select(u => (int)strahlerOrder[u]).ToList();
            var maxOrder = upstreamOrders.Max();
            var countAtMax = upstreamOrders.Count(o => o == maxOrder);
            var strahlerStayedAtMax = countAtMax < 2; // per the production rule: only increments on a 2+-way tie at the max

            if (strahlerStayedAtMax && upstreamOrders.Distinct().Count() > 1)
            {
                // A genuinely mixed-order confluence where Strahler stayed at the dominant
                // tributary's own order — magnitude, in contrast, is ALWAYS the full sum of every
                // contributor (never just the dominant one's), so the two are visibly different
                // numbers here, not just different names for the same rule.
                Assert.AreEqual(maxOrder, strahlerOrder[i],
                    $"Confluence at {i} with upstream orders {string.Join(",", upstreamOrders)} (no 2-way tie at the max) should keep Strahler order at the max.");
                Assert.AreNotEqual(ups.Select(u => shreveMagnitude[u]).Max(), shreveMagnitude[i],
                    $"Confluence at {i} should sum magnitudes, not just take the dominant tributary's own magnitude.");
                foundDivergentConfluence = true;
            }
        }

        Assert.IsTrue(confluencesChecked > 0, "Test terrain should contain at least one real confluence to check.");
        Assert.IsTrue(foundDivergentConfluence,
            "Test terrain should contain at least one confluence where Strahler order and Shreve magnitude visibly diverge (unequal-order tributaries).");
    }

    [TestMethod]
    public void ComputeDiagnostics_ShreveMagnitude_NeverDecreasesDownstream_AndConservesTotalAtEveryMouth()
    {
        // Definitional (Shreve 1966): magnitude only ever grows or stays the same moving
        // downstream (mirrors the Strahler never-decreases test above) — AND, because every unit
        // of magnitude traces back to exactly one headwater and is never dropped or double-counted
        // along the way, the sum of magnitude at every network "mouth" (a river cell with no
        // downstream river cell after it) must equal the total number of headwaters — a global
        // conservation check that a hand-picked single confluence or one linear chain can't offer,
        // confirming magnitude "accumulates correctly to the mouth" across the WHOLE network, not
        // just one path through it.
        var (mask, _, _, downstream, order, _, shreveMagnitude) = ComputeOnAConfluenceRichGrid();
        var upstreamCount = BuildUpstreamCounts(mask, downstream, order.Length);

        var headwaterCount = 0;
        var mouthMagnitudeTotal = 0;
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0) continue;

            if (upstreamCount[i] == 0) headwaterCount++;

            var next = downstream[i];
            if (next < 0 || mask[next] == 0) mouthMagnitudeTotal += shreveMagnitude[i];

            if (next < 0 || mask[next] == 0) continue;
            Assert.IsTrue(shreveMagnitude[next] >= shreveMagnitude[i],
                $"Magnitude dropped from {shreveMagnitude[i]} at {i} to {shreveMagnitude[next]} downstream at {next} — Shreve magnitude must never decrease.");
        }

        Assert.IsTrue(headwaterCount > 0, "Test terrain should contain at least one headwater.");
        Assert.AreEqual(headwaterCount, mouthMagnitudeTotal,
            "Total magnitude flowing out every network mouth should equal the total number of headwaters — every headwater's unit of magnitude must reach exactly one mouth, undropped and undoubled.");
    }

    /// <summary>A wide, gently-sloped, irregularly-walled basin — the same style of terrain used
    /// throughout this file to exercise realistic branching drainage — with a low enough threshold
    /// that multiple independent tributaries form and merge, giving the Strahler tests above actual
    /// confluences to check instead of one single unbranched line.</summary>
    private static (byte[] Mask, int[] Accumulation, double[] Slope, int[] Downstream, int[] Order, byte[] StrahlerOrder, int[] ShreveMagnitude) ComputeOnAConfluenceRichGrid()
    {
        const int width = 200, height = 200;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var northWallDepth = 4 + (int)(3 * Math.Sin(x * 0.7) + 3 * Math.Sin(x * 0.31 + 1.7));
                var westWallDepth = 4 + (int)(3 * Math.Sin(y * 0.53) + 3 * Math.Sin(y * 0.19 + 0.9));
                var eastWallDepth = 4 + (int)(3 * Math.Sin(y * 0.41 + 2.1) + 3 * Math.Sin(y * 0.27));
                var wall = y < northWallDepth || x < westWallDepth || x >= width - eastWallDepth;
                values[y * width + x] = wall
                    ? 100f
                    : 10f - y * 0.02f + ((x * 37 + y * 17) % 5) * 0.0001f;
            }
        }

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        // Threshold retuned for the area×slope² model (was 15.0 under the old area×slope model) —
        // this basin's real interior slope is gentle (~0.004, from the y*0.02 term at 5m cells), so
        // squaring shrinks it far more than the old linear threshold assumed; retuned empirically to
        // keep this basin producing the same kind of branching, multi-confluence network the
        // Strahler tests above need.
        return TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 1.5));
    }

    private static int[] BuildUpstreamCounts(byte[] mask, int[] downstream, int count)
    {
        var upstreamCount = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (mask[i] == 0) continue;
            var next = downstream[i];
            if (next >= 0 && mask[next] != 0) upstreamCount[next]++;
        }
        return upstreamCount;
    }

    private static List<int>[] BuildUpstreamLists(byte[] mask, int[] downstream, int count)
    {
        var upstream = new List<int>[count];
        for (var i = 0; i < count; i++) upstream[i] = new List<int>();
        for (var i = 0; i < count; i++)
        {
            if (mask[i] == 0) continue;
            var next = downstream[i];
            if (next >= 0 && mask[next] != 0) upstream[next].Add(i);
        }
        return upstream;
    }

    #region D-infinity (Stage 2)

    /// <summary>A perfect inclined plane tilted at <paramref name="thetaRadians"/> off the +x axis —
    /// exercises D-infinity's facet math directly without any real terrain generation, since a
    /// plane's true gradient direction is known exactly in advance (the standard validation
    /// technique for D-infinity implementations — see e.g. Tarboton's own paper).</summary>
    private static float[] MakePlane(int width, int height, double thetaRadians)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                // Negative so elevation DECREASES in the (cosθ, sinθ) direction — that's the downhill way.
                values[y * width + x] = (float)(-(x * Math.Cos(thetaRadians) + y * Math.Sin(thetaRadians)));
        return values;
    }

    [TestMethod]
    public void ComputeDInfinityDirections_FlowExactlyAlignedWithGridDirection_ProducesSingleNeighborFullWeight()
    {
        // A plane tilted at exactly 0° (pure +x gradient) — the true steepest-descent direction IS
        // the grid-aligned +x (E) neighbor, so D-infinity should degenerate fully to a single
        // neighbor at full weight, matching what SteepestDescentNeighbor (D8) would pick for the
        // same surface.
        const int width = 9, height = 9;
        var routed = MakePlane(width, height, 0.0);
        var (_, neighborA, neighborB, weightA) = TileHydrology.ComputeDInfinityDirections(routed, width, height);

        const int cx = 4, cy = 4;
        var center = cy * width + cx;
        var expectedNeighbor = cy * width + (cx + 1); // due "E"

        Assert.AreEqual(1.0, weightA[center], 1e-9);
        Assert.AreEqual(-1, neighborB[center]);
        Assert.AreEqual(expectedNeighbor, neighborA[center]);
    }

    [TestMethod]
    public void ComputeDInfinityDirections_FlowAtFacetMidpoint_SplitsWeightEvenlyBetweenBothNeighbors()
    {
        // A plane tilted at exactly 22.5° — halfway across the facet bounded by the "E" (0°) and
        // "NE" (45°) neighbors — so Tarboton's method should split flow evenly between them.
        const int width = 9, height = 9;
        var routed = MakePlane(width, height, Math.PI / 8.0);
        var (_, neighborA, neighborB, weightA) = TileHydrology.ComputeDInfinityDirections(routed, width, height);

        const int cx = 4, cy = 4;
        var center = cy * width + cx;
        var eNeighbor = cy * width + (cx + 1);
        var neNeighbor = (cy + 1) * width + (cx + 1);

        Assert.AreEqual(0.5, weightA[center], 1e-6);
        var chosen = new HashSet<int> { neighborA[center], neighborB[center] };
        CollectionAssert.AreEquivalent(new[] { eNeighbor, neNeighbor }, chosen.ToArray());
    }

    [TestMethod]
    public void ComputeDInfinityAccumulation_ConservesTotalRainfallMass_AcrossRealTerrain()
    {
        // Tarboton (1997)'s accumulation must conserve mass: every cell starts with exactly 1 unit
        // of "rainfall", and since every interior cell forwards 100% of what it receives onward
        // (split fractionally, never dropped), the total should reappear undiminished at whatever
        // cells have no downhill facet at all (outlets/edges) — the same conservation property real
        // flow accumulation must have, and the property Tarboton's own reported lower bias/RMSE
        // (vs. D8) on test surfaces of known contributing area presupposes actually holding exactly,
        // not approximately.
        var grid = MakeGrid(60, 60, 5.0, (x, y) =>
        {
            var northWallDepth = 4 + (int)(3 * Math.Sin(x * 0.7) + 3 * Math.Sin(x * 0.31 + 1.7));
            var westWallDepth = 4 + (int)(3 * Math.Sin(y * 0.53) + 3 * Math.Sin(y * 0.19 + 0.9));
            var eastWallDepth = 4 + (int)(3 * Math.Sin(y * 0.41 + 2.1) + 3 * Math.Sin(y * 0.27));
            var wall = y < northWallDepth || x < westWallDepth || x >= 60 - eastWallDepth;
            return wall ? 100f : 10f - y * 0.02f + ((x * 37 + y * 17) % 5) * 0.0001f;
        });

        var (_, neighborA, neighborB, weightA, accumulation) = TileHydrology.ComputeDInfinityDiagnostics(grid);

        var outletMass = 0.0;
        for (var i = 0; i < accumulation.Length; i++)
            if (neighborA[i] < 0) outletMass += accumulation[i];

        Assert.AreEqual(grid.Width * grid.Height, outletMass, 1e-6,
            "Total accumulated mass at every outlet (cells with no downhill D-infinity facet) should equal the grid's total cell count — one unit of rainfall per cell, none dropped or duplicated.");

        // Sanity: every non-outlet cell actually forwarded something (accumulation strictly positive
        // everywhere, since it starts at 1 and only ever grows).
        foreach (var a in accumulation)
            Assert.IsTrue(a >= 1.0, "Accumulation should never fall below its own starting rainfall unit.");
    }

    #endregion D-infinity (Stage 2)
}
