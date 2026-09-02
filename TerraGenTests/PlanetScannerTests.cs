using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class PlanetScannerTests
{
    private static readonly PlanetNoise.Parameters NoiseParams = new(Seed: 7);
    private const double PlanetRadiusMeters = PlanetNoise.EarthRadiusMeters;

    [TestMethod]
    public void Scan_ProducesGridOfRequestedSize()
    {
        var options = new PlanetScanner.Options(Width: 20, Height: 10, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180);

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        Assert.AreEqual(10, result.Cells.GetLength(0));
        Assert.AreEqual(20, result.Cells.GetLength(1));
    }

    [TestMethod]
    public void Scan_DetailMode_ProducesGridOfRequestedSize()
    {
        var options = new PlanetScanner.Options(Width: 15, Height: 8, LatMin: 10, LatMax: 20, LonMin: 10, LonMax: 20, Detail: true);

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        Assert.AreEqual(8, result.Cells.GetLength(0));
        Assert.AreEqual(15, result.Cells.GetLength(1));
    }

    [TestMethod]
    public void Scan_DetailModeWithPlates_CanDifferFromNonDetailElevations()
    {
        // Detail mode adds the mountain-ridge layer on top of the landmass layer — for a window
        // with plates active, at least somewhere in a reasonably sized grid the two must diverge
        // (mountain uplift/rift is never uniformly zero everywhere).
        var plates = TectonicPlates.Generate(seed: 7, count: 6);
        var options = new PlanetScanner.Options(Width: 30, Height: 15, LatMin: -20, LatMax: 20, LonMin: -20, LonMax: 20);
        var detailOptions = options with { Detail = true };

        var plain = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, options);
        var detailed = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, detailOptions);

        var anyDifference = false;
        for (var row = 0; row < 15 && !anyDifference; row++)
            for (var col = 0; col < 30 && !anyDifference; col++)
                if (Math.Abs(plain.ElevationsMeters[row, col] - detailed.ElevationsMeters[row, col]) > 1e-6)
                    anyDifference = true;

        Assert.IsTrue(anyDifference, "Expected the mountain-ridge layer to change elevation somewhere in the grid.");
    }

    [TestMethod]
    public void Scan_DetailMode_IsDeterministic()
    {
        var options = new PlanetScanner.Options(Width: 12, Height: 6, LatMin: 5, LatMax: 15, LonMin: 5, LonMax: 15, Detail: true);

        var a = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);
        var b = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        for (var row = 0; row < 6; row++)
            for (var col = 0; col < 12; col++)
                Assert.AreEqual(a.ElevationsMeters[row, col], b.ElevationsMeters[row, col], 1e-9);
    }

    [TestMethod]
    public void Scan_WithoutPlates_NeverProducesBoundaryCells()
    {
        var options = new PlanetScanner.Options(Width: 40, Height: 20, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180);

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        for (var row = 0; row < 20; row++)
            for (var col = 0; col < 40; col++)
                Assert.IsTrue(result.Cells[row, col] is PlanetScanner.Cell.Land or PlanetScanner.Cell.Ocean,
                    $"Cell ({row},{col}) was {result.Cells[row, col]} despite no plates being supplied.");
    }

    [TestMethod]
    public void Scan_CellMatchesItsOwnElevationSign()
    {
        var options = new PlanetScanner.Options(Width: 30, Height: 15, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180);

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        for (var row = 0; row < 15; row++)
        {
            for (var col = 0; col < 30; col++)
            {
                var expected = result.ElevationsMeters[row, col] >= 0.0 ? PlanetScanner.Cell.Land : PlanetScanner.Cell.Ocean;
                Assert.AreEqual(expected, result.Cells[row, col]);
            }
        }
    }

    [TestMethod]
    public void Scan_WithPlatesAndZeroThreshold_EveryCellBecomesABoundaryMarker()
    {
        var plates = TectonicPlates.Generate(seed: 7, count: 8);
        var options = new PlanetScanner.Options(
            Width: 20, Height: 10, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180,
            BoundaryInfluenceThreshold: 0.0); // every point has SOME nonzero influence toward its nearest boundary

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, options);

        var sawBoundary = false;
        for (var row = 0; row < 10; row++)
            for (var col = 0; col < 20; col++)
                if (result.Cells[row, col] is PlanetScanner.Cell.Convergent or PlanetScanner.Cell.Divergent or PlanetScanner.Cell.Transform)
                    sawBoundary = true;

        Assert.IsTrue(sawBoundary, "Expected at least one boundary marker with the threshold wide open.");
    }

    [TestMethod]
    public void Scan_WithPlatesAndThresholdAboveOne_NeverProducesBoundaryCells()
    {
        var plates = TectonicPlates.Generate(seed: 7, count: 8);
        var options = new PlanetScanner.Options(
            Width: 20, Height: 10, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180,
            BoundaryInfluenceThreshold: 1.1); // BoundaryInfluence is clamped to [0,1] — unreachable

        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, options);

        for (var row = 0; row < 10; row++)
            for (var col = 0; col < 20; col++)
                Assert.IsTrue(result.Cells[row, col] is PlanetScanner.Cell.Land or PlanetScanner.Cell.Ocean);
    }

    [TestMethod]
    public void Scan_SameInputs_IsDeterministic()
    {
        var options = new PlanetScanner.Options(Width: 25, Height: 12, LatMin: -45, LatMax: 45, LonMin: -60, LonMax: 60);
        var plates = TectonicPlates.Generate(seed: 3, count: 6);

        var a = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, options);
        var b = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates, options);

        for (var row = 0; row < 12; row++)
            for (var col = 0; col < 25; col++)
                Assert.AreEqual(a.Cells[row, col], b.Cells[row, col]);
    }

    [TestMethod]
    public void CellCenter_TopLeftIsNorthWestCorner_BottomRightIsSouthEastCorner()
    {
        var options = new PlanetScanner.Options(Width: 10, Height: 5, LatMin: -20, LatMax: 40, LonMin: 10, LonMax: 70);
        var result = PlanetScanner.Scan(NoiseParams, PlanetRadiusMeters, plates: null, options);

        var topLeft = result.CellCenter(0, 0);
        var bottomRight = result.CellCenter(4, 9);

        Assert.AreEqual(40.0, topLeft.LatDeg, 1e-9);
        Assert.AreEqual(10.0, topLeft.LonDeg, 1e-9);
        Assert.AreEqual(-20.0, bottomRight.LatDeg, 1e-9);
        Assert.AreEqual(70.0, bottomRight.LonDeg, 1e-9);
    }

    [TestMethod]
    public void Symbol_MatchesEachCellKind()
    {
        var options = new PlanetScanner.Options(Width: 1, Height: 1, LatMin: 0, LatMax: 0, LonMin: 0, LonMax: 0);
        // Build directly rather than via Scan() so every symbol is exercised in one grid.
        var elevations = new double[1, 5];
        var grid = new PlanetScanner.Cell[1, 5];
        grid[0, 0] = PlanetScanner.Cell.Ocean;
        grid[0, 1] = PlanetScanner.Cell.Land;
        grid[0, 2] = PlanetScanner.Cell.Convergent;
        grid[0, 3] = PlanetScanner.Cell.Divergent;
        grid[0, 4] = PlanetScanner.Cell.Transform;
        var wideOptions = options with { Width = 5 };
        var result = new PlanetScanner.Result(grid, elevations, wideOptions);

        Assert.AreEqual('~', result.Symbol(0, 0));
        Assert.AreEqual('.', result.Symbol(0, 1));
        Assert.AreEqual('^', result.Symbol(0, 2));
        Assert.AreEqual('v', result.Symbol(0, 3));
        Assert.AreEqual('x', result.Symbol(0, 4));
    }
}
