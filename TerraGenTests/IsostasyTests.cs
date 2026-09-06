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
    public void ReboundRatePerYear_TimesIntervalYears_RecoversTheAiryRootDepth()
    {
        // The exact "eroded mass reappears as rebound" relation Task 3.3 asks for: by construction,
        // rate * intervalYears must equal AiryRootDepth of the eroded height.
        const double erodedHeightM = 5.0;
        const double crustDensity = 2670.0;
        const double mantleDensity = 3300.0;
        const double intervalYears = 2.5e6;

        var rate = Isostasy.ReboundRatePerYear(erodedHeightM, crustDensity, mantleDensity, intervalYears);

        Assert.AreEqual(Isostasy.AiryRootDepth(erodedHeightM, crustDensity, mantleDensity), rate * intervalYears, 1e-9);
    }

    [TestMethod]
    public void ReboundRatePerYear_ZeroInterval_ReturnsZeroInsteadOfDividingByZero()
    {
        Assert.AreEqual(0.0, Isostasy.ReboundRatePerYear(10.0, 2670.0, 3300.0, 0.0));
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
}
