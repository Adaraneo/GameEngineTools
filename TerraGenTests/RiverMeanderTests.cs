using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class RiverMeanderTests
{
    [TestMethod]
    public void ApplyMeander_ReturnsMaskSameLengthAsInput()
    {
        const int width = 10, height = 10;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = (height - y) * 1f; // gentle uniform slope in +y

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude) = TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 1.0));
        var (meandered, _) = RiverMeander.ApplyMeander(grid, mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude, new RiverMeander.Parameters());

        Assert.AreEqual(mask.Length, meandered.Length);
    }

    [TestMethod]
    public void ApplyMeander_SteepSlope_StaysOnTheStraightPath()
    {
        // A steep, uniform slope (0.5 rise/run at 5m cells = 2.5m drop/cell — well past
        // SlopeSuppressedAbove's default 0.08) should get zero meander amplitude everywhere, so the
        // meandered mask should be IDENTICAL to the straight D8 mask, cell for cell — matching real
        // mountain streams, which run straight because they have enough stream power to just plow
        // downhill instead of migrating sideways (Leopold & Wolman 1957).
        const int width = 10, height = 20;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = (height - y) * 2.5f;

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude) = TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 1.0));
        var (meandered, _) = RiverMeander.ApplyMeander(grid, mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude, new RiverMeander.Parameters());

        CollectionAssert.AreEqual(mask, meandered, "A steep, uniform slope should suppress meandering entirely — the path should be unchanged.");
    }

    [TestMethod]
    public void ApplyMeander_GentleSlope_LeavesTheStraightPathAtLeastOnce()
    {
        // A gentle, low-relief slope (well below SlopeFullMeanderBelow's default 0.01) with a
        // sizeable contributing area (so channel width, and hence amplitude, isn't ~0 either —
        // needs a grid this large, since amplitude scales with sqrt(area) and a small synthetic
        // catchment never accumulates enough to clear even a couple meters) should meander — at
        // least SOME marked cell must land off the dead-straight D8 line.
        const int width = 200, height = 300;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                // Slope 0.001 at 5m cells = 0.005m/cell — gentle, plus a converging cross-slope so
                // a decent amount of contributing area funnels into one central channel. The cross-
                // slope has to stay gentle too (0.05, not steep) — a steep cross-slope makes the
                // APPROACH toward the channel exceed SlopeSuppressedAbove and suppress itself, even
                // though the channel's own along-valley slope (the 0.001 term) is plenty gentle.
                values[y * width + x] = (height - y) * 0.005f + Math.Abs(x - width / 2) * 0.05f;

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude) = TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 0.5));
        var (meandered, _) = RiverMeander.ApplyMeander(grid, mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude, new RiverMeander.Parameters());

        var straightSet = new HashSet<int>(Enumerable.Range(0, mask.Length).Where(i => mask[i] != 0));
        var meanderedSet = new HashSet<int>(Enumerable.Range(0, meandered.Length).Where(i => meandered[i] != 0));

        Assert.IsTrue(meanderedSet.Except(straightSet).Any(),
            "Expected the meandered path to leave the original dead-straight D8 line at least once on gentle, low-relief terrain.");
    }

    [TestMethod]
    public void ApplyMeander_StaysWithinEightConnectedNeighborsAlongTheWholeChannel()
    {
        // A meander swing can move a cell several grid columns sideways between one step and the
        // next; DrawLine's Bresenham rasterization is what's supposed to keep every step of the
        // redrawn channel 8-connected regardless — verify it actually does, since a gap here would
        // reintroduce exactly the discontinuity problem the propagation fix already solved for the
        // straight case.
        const int width = 60, height = 120;
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = (height - y) * 0.005f + Math.Abs(x - width / 2) * 0.5f;

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
        var (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude) = TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 0.1));
        var (meandered, _) = RiverMeander.ApplyMeander(grid, mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude, new RiverMeander.Parameters());

        Assert.IsTrue(meandered.Count(b => b != 0) > 0, "Test setup should produce at least some river cells.");

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (meandered[y * width + x] == 0) continue;
                var hasNeighbor = false;
                for (var dy = -1; dy <= 1 && !hasNeighbor; dy++)
                {
                    for (var dx = -1; dx <= 1 && !hasNeighbor; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        var ny = y + dy;
                        if (nx < 0 || nx >= width || ny < 0 || ny >= height) continue;
                        if (meandered[ny * width + nx] != 0) hasNeighbor = true;
                    }
                }
                // A cell with accumulation==1 and no downstream at all (a true isolated dead end)
                // can legitimately stand alone; anything else must connect to a neighbor.
                if (!hasNeighbor)
                    Assert.Fail($"River cell ({x},{y}) has no 8-connected river neighbor — the channel broke.");
            }
        }
    }

    [TestMethod]
    public void ApplyMeander_LargerContributingArea_ProducesLargerAmplitude()
    {
        // Two otherwise-identical gentle channels, one carrying far more accumulated area than the
        // other, should show visibly different meander scale — bigger rivers meander in bigger
        // loops (Leopold 1994: wavelength ≈ 11× channel width, and width grows with area). Measured
        // here as the max lateral spread (in cells) any marked cell reaches from the dead-straight
        // vertical line at its own column's origin — a crude but robust amplitude proxy.
        const int width = 400, height = 120;

        int MaxSpread(double crossSlope)
        {
            var values = new float[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    values[y * width + x] = (float)((height - y) * 0.002 + Math.Abs(x - width / 2) * crossSlope);
            var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, values);
            var (mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude) = TileHydrology.ComputeDiagnostics(grid, new TileHydrology.Parameters(ChannelInitiationAreaSlopeSquaredThreshold: 0.01));
            var (meandered, _) = RiverMeander.ApplyMeander(grid, mask, accumulation, slope, downstream, order, strahlerOrder, shreveMagnitude, new RiverMeander.Parameters());

            var maxDx = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (meandered[y * width + x] != 0)
                        maxDx = Math.Max(maxDx, Math.Abs(x - width / 2));
            return maxDx;
        }

        // A GENTLER cross-valley slope funnels lateral terrain into the central channel more
        // gradually (each column reaches the channel only after draining further downhill first),
        // giving the channel LESS accumulated area/width by the time it reaches a given row than a
        // steeper cross-slope, which converges everything almost immediately.
        var small = MaxSpread(0.05);
        var large = MaxSpread(0.5);

        Assert.IsTrue(large > small, $"Expected the larger-catchment channel to swing wider (got small={small}, large={large}).");
    }

    [TestMethod]
    public void RiverMeanderParameters_DefaultScourFactor_IsWithinIkedaParkerSawaiRange()
    {
        var p = new RiverMeander.Parameters();

        Assert.IsTrue(p.ScourFactor is >= 2.5 and <= 6.0,
            $"Default ScourFactor {p.ScourFactor} should be within Ikeda/Parker/Sawai (1981)'s cited range [2.5, 6].");
    }

    [TestMethod]
    public void RiverMeanderParameters_DefaultBankErosionCoefficient_IsWithinFieldCalibratedRange()
    {
        var p = new RiverMeander.Parameters();

        Assert.IsTrue(p.BankErosionCoefficientE is >= 1e-8 and <= 1e-7,
            $"Default BankErosionCoefficientE {p.BankErosionCoefficientE} should be within the field-calibrated range [1e-8, 1e-7].");
    }

    [TestMethod]
    public void ComputeCurvatureMemoryDecayLength_GivenDepthAndFriction_MatchesEdwardsSmithFormula()
    {
        // Unit tests the D = H/(2·C_f) formula (Edwards & Smith 2002) directly against
        // hand-calculated values for a few (H, C_f) pairs spanning the cited friction-coefficient
        // range [0.003, 0.03].
        Assert.AreEqual(10.0 / (2.0 * 0.005), RiverMeander.CurvatureMemoryLengthMeters(channelDepthMeters: 10.0, frictionCoefficient: 0.005), 1e-9);
        Assert.AreEqual(2.0 / (2.0 * 0.03), RiverMeander.CurvatureMemoryLengthMeters(channelDepthMeters: 2.0, frictionCoefficient: 0.03), 1e-9);
        Assert.AreEqual(1.5 / (2.0 * 0.003), RiverMeander.CurvatureMemoryLengthMeters(channelDepthMeters: 1.5, frictionCoefficient: 0.003), 1e-9);
    }

    #region Stream-power meander suppression (Stage 2)

    /// <summary>Builds a single-column channel with ONE deliberate kink (straight down, then a
    /// one-cell sideways jog, then straight down again) — a perfectly straight line has exactly
    /// zero curvature everywhere regardless of erodibility (nothing for suppression to actually
    /// suppress, making "no migration" trivially true either way), so the two stream-power tests
    /// below need this real bend to meaningfully distinguish "suppressed" from "not suppressed."
    /// <see cref="RiverMeander.Parameters.InitialPerturbationPerWidth"/> is set to 0 by both callers
    /// specifically to remove the OTHER confound: that seed perturbation scales with channel width,
    /// which these tests deliberately push to extremes, and would otherwise move the point on its
    /// own regardless of migration suppression.</summary>
    private static (byte[] Mask, int[] Accumulation, double[] Slope, int[] Downstream, int[] Order, int KinkIndex) BuildKinkedColumn(
        int width, int height, int accumulation, double slope)
    {
        var mask = new byte[width * height];
        var accumulationArr = new int[width * height];
        var slopeArr = new double[width * height];
        var downstream = new int[width * height];
        Array.Fill(downstream, -1);
        var order = new List<int>();

        const int x = 5;
        var kinkRow = height / 2;
        var kinkIndex = -1;
        for (var y = 0; y < height; y++)
        {
            var thisX = y > kinkRow ? x + 1 : x; // one-cell sideways jog partway down
            var idx = y * width + thisX;
            mask[idx] = 1;
            accumulationArr[idx] = accumulation;
            slopeArr[idx] = slope;
            if (y == kinkRow) kinkIndex = idx;
            if (y + 1 < height)
            {
                var nextX = y + 1 > kinkRow ? x + 1 : x;
                downstream[idx] = (y + 1) * width + nextX;
            }
            order.Add(idx);
        }
        for (var i = 0; i < mask.Length; i++) if (mask[i] == 0) order.Add(i);

        return (mask, accumulationArr, slopeArr, downstream, order.ToArray(), kinkIndex);
    }

    [TestMethod]
    public void ComputeStreamPower_KnownDischargeSlopeWidth_MatchesHandCalculatedValue()
    {
        // ω = ρ·g·Q·S/w, ρ=1000, g=9.81, Q=10, S=0.01, w=20 -> 1000*9.81*10*0.01/20 = 49.05 W/m².
        const double q = 10.0, s = 0.01, w = 20.0;
        var expected = 1000.0 * 9.81 * q * s / w;

        Assert.AreEqual(expected, RiverMeander.ComputeSpecificStreamPowerWPerM2(q, s, w), 1e-9);
    }

    [TestMethod]
    public void ComputeOffsets_HighStreamPowerLowSlope_StillSuppressesMigrationDespitePassingFlatSlopeCheck()
    {
        // Modest accumulation (~3m channel width, keeping the seed/grid scale sane) with slope well
        // below SlopeFullMeanderBelow — the OLD flat-slope check alone would allow full-strength
        // migration at the kink — but a deliberately large test-local DischargePerContributingAreaM2
        // (NOT the production default, which is too tiny to reach any threshold at this accumulation
        // scale — see its own remarks) drives stream power comfortably above the default threshold.
        // Proves the new gate actually adds behavior, not dead code shadowed by the slope check.
        const int width = 10, height = 40;
        var (mask, accumulation, slope, downstream, order, kinkIndex) =
            BuildKinkedColumn(width, height, accumulation: 1000, slope: 0.005);

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, new float[width * height]);
        var p = new RiverMeander.Parameters(InitialPerturbationPerWidth: 0.0, DischargePerContributingAreaM2: 1.0);
        var (offsetX, offsetY) = RiverMeander.ComputeOffsets(grid, mask, accumulation, slope, downstream, order, p);

        var kinkX = kinkIndex % width;
        var kinkY = kinkIndex / width;
        Assert.AreEqual(kinkX, offsetX[kinkIndex], "The kink cell (the only one with real curvature to amplify) should not have migrated — stream power should have suppressed it despite the gentle slope.");
        Assert.AreEqual(kinkY, offsetY[kinkIndex]);
    }

    [TestMethod]
    public void ComputeOffsets_LowStreamPowerHighSlope_StillSuppressedByExistingSlopeCheck()
    {
        // Companion regression guard: the SAME kinked shape, but steep (above SlopeSuppressedAbove)
        // with a tiny, realistic accumulation and the PRODUCTION-DEFAULT DischargePerContributingAreaM2
        // (stream power nowhere near the threshold) — migration should still be suppressed via the
        // ORIGINAL flat-slope check alone, proving the new gate is purely additive and never
        // accidentally loosens the pre-existing behavior.
        const int width = 10, height = 40;
        var (mask, accumulation, slope, downstream, order, kinkIndex) =
            BuildKinkedColumn(width, height, accumulation: 5, slope: 0.5);

        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5.0, width, height, new float[width * height]);
        var p = new RiverMeander.Parameters(InitialPerturbationPerWidth: 0.0);
        var (offsetX, offsetY) = RiverMeander.ComputeOffsets(grid, mask, accumulation, slope, downstream, order, p);

        var kinkX = kinkIndex % width;
        var kinkY = kinkIndex / width;
        Assert.AreEqual(kinkX, offsetX[kinkIndex], "The kink cell should not have migrated — the pre-existing steep-slope check should have suppressed it.");
        Assert.AreEqual(kinkY, offsetY[kinkIndex]);
    }

    #endregion Stream-power meander suppression (Stage 2)
}
