using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class IsostasyTests
{
    [TestMethod]
    public void AiryRootDepth_MatchesTheStandardFormula()
    {
        var root = Isostasy.AiryRootDepth(topographicHeightM: 1000.0, crustDensity: 2670.0, mantleDensity: 3300.0);
        Assert.AreEqual(1000.0 * 2670.0 / (3300.0 - 2670.0), root, 1e-9);
    }

    [TestMethod]
    public void ErosionalReboundHeight_MatchesTheStandardFormula()
    {
        var rebound = Isostasy.ErosionalReboundHeight(erodedHeightM: 1000.0, crustDensity: 2670.0, mantleDensity: 3300.0);
        Assert.AreEqual(1000.0 * 2670.0 / 3300.0, rebound, 1e-9);
    }

    [TestMethod]
    public void ErosionalReboundHeight_IsAlwaysLessThanTheErodedAmount()
    {
        // Regression for the actual field bug: ErosionalReboundHeight's fraction (crustDensity/mantleDensity)
        // must always be < 1, unlike AiryRootDepth's ratio (crustDensity/(mantleDensity-crustDensity)), which
        // is always > 1 and diverges to +Infinity if mistakenly used for the erosion-rebound step instead.
        foreach (var crustDensity in new[] { 2350.0, 2670.0, 2900.0 })
        {
            var rebound = Isostasy.ErosionalReboundHeight(erodedHeightM: 500.0, crustDensity, mantleDensity: 3300.0);
            Assert.IsTrue(rebound < 500.0, $"Rebound ({rebound}) should always be less than the eroded amount for crustDensity={crustDensity}.");
        }
    }

    [TestMethod]
    public void Erode_WithoutIsostasyParams_LeavesUpliftArrayUnmutated()
    {
        // Byte-identical-when-disabled regression: the whole isostasy feedback is opt-in.
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 100.0, 15, 15, new float[15 * 15]);
        var uplift = new double[15 * 15];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.002;
        var upliftBefore = (double[])uplift.Clone();

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 50), uplift, isostasyParams: null);

        CollectionAssert.AreEqual(upliftBefore, uplift);
    }

    [TestMethod]
    public void Erode_WithIsostasy_NeverMutatesTheUpliftArray()
    {
        // Regression: rebound used to compound into upliftMetersPerYear forever, diverging to +Infinity on real --spim --rock-types --isostasy runs (confirmed live) — must stay a one-time height correction to the grid instead.
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 100.0, 15, 15, new float[15 * 15]);
        var uplift = new double[15 * 15];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.002;
        var upliftBefore = (double[])uplift.Clone();

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 50), uplift,
            isostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 5));

        CollectionAssert.AreEqual(upliftBefore, uplift);
    }

    [TestMethod]
    public void Erode_WithIsostasy_ProducesMoreReliefThanWithoutIt()
    {
        // The whole point of Stage 3.1: rebound partially compensates erosion, so terrain should
        // sit measurably HIGHER at steady state with isostasy on than with it off, same uplift/K.
        const int size = 20;
        var p = new StreamPowerErosion.Parameters(Iterations: 300, TimestepYears: 2.5e5);
        var uplift = () =>
        {
            var u = new double[size * size];
            for (var i = 0; i < u.Length; i++) u[i] = 0.002;
            return u;
        };

        var gridWithout = new TerrainHeightmap("test", 0.0, 0.0, 100.0, size, size, new float[size * size]);
        StreamPowerErosion.Erode(gridWithout, p, uplift());

        var gridWith = new TerrainHeightmap("test", 0.0, 0.0, 100.0, size, size, new float[size * size]);
        StreamPowerErosion.Erode(gridWith, p, uplift(), isostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 5));

        Assert.IsTrue(gridWith.Values.Max() > gridWithout.Values.Max(),
            $"Expected isostatic rebound to leave MORE relief than without it (without={gridWithout.Values.Max():F2}, with={gridWith.Values.Max():F2}).");
    }

    [TestMethod]
    public void Erode_WithIsostasyAndRockTypeCrustDensity_AllValuesStayFinite()
    {
        const int size = 15;
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 100.0, size, size, new float[size * size]);
        var uplift = new double[size * size];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.001;
        var crustDensity = new double[size * size];
        for (var i = 0; i < crustDensity.Length; i++) crustDensity[i] = RockPropertiesTable.Values[RockType.Granite].DensityKgM3;

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 60), uplift,
            isostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 7), crustDensityPerCell: crustDensity);

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v));
            Assert.IsFalse(float.IsInfinity(v));
        }
    }

    [TestMethod]
    public void Erode_WithIsostasyAndHighestErodibilityRock_AtProductionIterationCount_StaysFinite()
    {
        // Direct regression for the reported crash: Schist has the HIGHEST erodibility K in the
        // table (fastest to erode, so fastest to accumulate isostatic rebound), run at the real
        // default Iterations=200 (not a shortened test count) with the same combination the bug
        // report used (--spim --rock-types --isostasy).
        const int size = 20;
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 2.5, size, size, new float[size * size]);
        var uplift = new double[size * size];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = StreamPowerErosion.MaxUpliftMmPerYear / 1000.0; // worst-case sustained uplift
        var erodibility = new double[size * size];
        var crustDensity = new double[size * size];
        for (var i = 0; i < erodibility.Length; i++)
        {
            erodibility[i] = RockPropertiesTable.Values[RockType.Schist].ErodibilityK;
            crustDensity[i] = RockPropertiesTable.Values[RockType.Schist].DensityKgM3;
        }

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 200), uplift,
            erodibilityPerCell: erodibility,
            isostasyParams: new Isostasy.Parameters(RecomputeIntervalIterations: 10),
            crustDensityPerCell: crustDensity);

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v), "Terrain went to NaN — the isostasy feedback diverged.");
            Assert.IsFalse(float.IsInfinity(v), "Terrain went to +/-Infinity — the isostasy feedback diverged.");
        }
    }
}
