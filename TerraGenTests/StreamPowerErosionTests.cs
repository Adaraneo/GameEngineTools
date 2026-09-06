using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class StreamPowerErosionTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, double cellSize, Func<int, int, float> seedFn)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = seedFn(x, y);
        return new TerrainHeightmap("test", 0.0, 0.0, cellSize, width, height, values);
    }

    [TestMethod]
    public void Erode_ZeroIterations_LeavesGridUnchanged()
    {
        var grid = MakeGrid(10, 10, 1.0, (x, y) => (x + y) * 2.5f);
        var before = (float[])grid.Values.Clone();
        var uplift = new double[10 * 10];

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 0), uplift);

        CollectionAssert.AreEqual(before, grid.Values);
    }

    [TestMethod]
    public void Erode_NonFiniteInputHeight_FailsFastWithDiagnosticInsteadOfPropagatingSilently()
    {
        // Regression for a reported crash: a non-finite (NaN/Infinity) height reaching this method
        // used to propagate silently into the rest of the grid, only surfacing much later as an
        // inscrutable IndexOutOfRangeException deep inside TileErosion. Erode must instead fail
        // fast, right at the cell/iteration where it first sees the bad value, with a diagnostic.
        const int size = 10;
        var grid = MakeGrid(size, size, 1.0, (x, y) => (x + y) * 2.5f);
        grid.Values[size * size / 2] = float.PositiveInfinity; // simulates corruption from an earlier step
        var uplift = new double[size * size];

        var diagnostics = new List<string>();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 5), uplift, onDiagnostic: diagnostics.Add));

        StringAssert.Contains(ex.Message, "non-finite");
        Assert.IsTrue(diagnostics.Count > 0, "Expected the diagnostic callback to fire before the exception was thrown.");
    }

    [TestMethod]
    public void Erode_LockedCells_AreNeverWritten()
    {
        const int size = 12;
        var grid = MakeGrid(size, size, 1.0, (x, y) => (x + y) * 1.5f);
        var before = (float[])grid.Values.Clone();
        var uplift = new double[size * size];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.01;

        var locked = new bool[size * size];
        for (var i = 0; i < locked.Length; i++) locked[i] = true;

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 20), uplift, locked);

        CollectionAssert.AreEqual(before, grid.Values);
    }

    [TestMethod]
    public void Erode_ManyIterations_AllValuesStayFinite()
    {
        var grid = MakeGrid(15, 15, 5.0, (x, y) => (x - y) * 0.7f);
        var uplift = new double[15 * 15];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.005;

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 50), uplift);

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v));
            Assert.IsFalse(float.IsInfinity(v));
        }
    }

    // ⚠ Substitutes Task 1.3's literal h_max≈2.244·U/K (Cordonnier et al. 2016, fit to their own unspecified test geometry) with the governing equation's own U/K-proportionality at steady state.
    [TestMethod]
    public void Erode_DoublingUplift_RoughlyDoublesSteadyStateMaxRelief()
    {
        const int size = 20;
        var p = new StreamPowerErosion.Parameters(M: 0.5, N: 1.0, K: 5.61e-7, Iterations: 400, TimestepYears: 2.5e5);

        double RunToSteadyState(double upliftMetersPerYear)
        {
            var grid = MakeGrid(size, size, 100.0, (_, _) => 0f);
            var uplift = new double[size * size];
            for (var i = 0; i < uplift.Length; i++) uplift[i] = upliftMetersPerYear;
            StreamPowerErosion.Erode(grid, p, uplift);
            return grid.Values.Max();
        }

        var hLow = RunToSteadyState(0.001);
        var hHigh = RunToSteadyState(0.002);

        Assert.IsTrue(hLow > 0.0, "Expected uplift to build up nonzero relief.");
        var ratio = hHigh / hLow;
        Assert.IsTrue(ratio is > 1.5 and < 2.5,
            $"Expected doubling U to roughly double steady-state max relief (got ratio {ratio:F2}, hLow={hLow:F2}, hHigh={hHigh:F2}).");
    }

    [TestMethod]
    public void UpliftFieldFromPlates_NoPlates_ReturnsAllZero()
    {
        var grid = MakeGrid(10, 10, 100.0, (_, _) => 0f);
        var uplift = StreamPowerErosion.UpliftFieldFromPlates(grid, plates: null, refLatDeg: 0, refLonDeg: 0, planetRadiusMeters: PlanetNoise.EarthRadiusMeters);

        Assert.IsTrue(uplift.All(u => u == 0.0));
    }

    [TestMethod]
    public void UpliftFieldFromPlates_WithPlates_StaysWithinEmpiricalMmPerYearBoundsInMeters()
    {
        var plates = TectonicPlates.Generate(seed: 3, count: 12);
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5000.0, 20, 20, new float[20 * 20]);

        var uplift = StreamPowerErosion.UpliftFieldFromPlates(grid, plates, refLatDeg: 0, refLonDeg: 0, planetRadiusMeters: PlanetNoise.EarthRadiusMeters);

        var maxAbsMeters = StreamPowerErosion.MaxUpliftMmPerYear / 1000.0;
        foreach (var u in uplift)
        {
            Assert.IsFalse(double.IsNaN(u));
            Assert.IsTrue(Math.Abs(u) <= maxAbsMeters + 1e-9,
                $"Uplift {u} m/yr exceeds the clamped empirical bound of ±{maxAbsMeters} m/yr.");
        }
    }
}
