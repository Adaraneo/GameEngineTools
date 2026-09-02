using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class LandmassDetectorTests
{
    private const double PlanetRadiusMeters = PlanetNoise.EarthRadiusMeters;

    private static PlanetScanner.Result BuildResult(PlanetScanner.Cell[,] cells, PlanetScanner.Options options)
    {
        var height = cells.GetLength(0);
        var width = cells.GetLength(1);
        var elevations = new double[height, width];
        for (var row = 0; row < height; row++)
            for (var col = 0; col < width; col++)
                elevations[row, col] = cells[row, col] == PlanetScanner.Cell.Ocean ? -10.0 : 10.0;

        return new PlanetScanner.Result(cells, elevations, options);
    }

    [TestMethod]
    public void Detect_SingleIsolatedLandCell_IsOneLandmass()
    {
        var cells = new PlanetScanner.Cell[5, 5];
        for (var r = 0; r < 5; r++) for (var c = 0; c < 5; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[2, 2] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 5, Height: 5, LatMin: -40, LatMax: 40, LonMin: -40, LonMax: 40);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(1, detection.Landmasses.Count);
        Assert.AreEqual(1, detection.Landmasses[0].CellCount);
        Assert.AreEqual(1, detection.Landmasses[0].Rank);
    }

    [TestMethod]
    public void Detect_TwoDiagonallyAdjacentLandCells_AreSeparateLandmasses()
    {
        // 4-connectivity only — a shared corner does NOT count as connected.
        var cells = new PlanetScanner.Cell[4, 4];
        for (var r = 0; r < 4; r++) for (var c = 0; c < 4; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[1, 1] = PlanetScanner.Cell.Land;
        cells[2, 2] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 4, Height: 4, LatMin: -40, LatMax: 40, LonMin: -40, LonMax: 40);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(2, detection.Landmasses.Count);
    }

    [TestMethod]
    public void Detect_ContiguousBlock_MergesIntoOneLandmassWithCorrectCellCount()
    {
        var cells = new PlanetScanner.Cell[6, 6];
        for (var r = 0; r < 6; r++) for (var c = 0; c < 6; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        for (var r = 1; r <= 3; r++) for (var c = 1; c <= 3; c++) cells[r, c] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 6, Height: 6, LatMin: -30, LatMax: 30, LonMin: -30, LonMax: 30);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(1, detection.Landmasses.Count);
        Assert.AreEqual(9, detection.Landmasses[0].CellCount);
    }

    [TestMethod]
    public void Detect_LargerLandmass_RanksBeforeSmallerOne()
    {
        var cells = new PlanetScanner.Cell[10, 10];
        for (var r = 0; r < 10; r++) for (var c = 0; c < 10; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        // Big block: 3x3 in the corner.
        for (var r = 0; r < 3; r++) for (var c = 0; c < 3; c++) cells[r, c] = PlanetScanner.Cell.Land;
        // Small block: a single cell far away.
        cells[8, 8] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 10, Height: 10, LatMin: -45, LatMax: 45, LonMin: -45, LonMax: 45);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(2, detection.Landmasses.Count);
        Assert.AreEqual(1, detection.Landmasses[0].Rank);
        Assert.IsTrue(detection.Landmasses[0].AreaKm2 > detection.Landmasses[1].AreaKm2);
        Assert.AreEqual(9, detection.Landmasses[0].CellCount);
        Assert.AreEqual(1, detection.Landmasses[1].CellCount);
    }

    [TestMethod]
    public void Detect_NoLand_ReturnsEmptyLandmassList()
    {
        var cells = new PlanetScanner.Cell[5, 5];
        for (var r = 0; r < 5; r++) for (var c = 0; c < 5; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        var options = new PlanetScanner.Options(Width: 5, Height: 5, LatMin: -20, LatMax: 20, LonMin: -20, LonMax: 20);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(0, detection.Landmasses.Count);
    }

    [TestMethod]
    public void Detect_LandmassRankByCell_MatchesEachCellsActualLandmass()
    {
        var cells = new PlanetScanner.Cell[6, 6];
        for (var r = 0; r < 6; r++) for (var c = 0; c < 6; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[1, 1] = PlanetScanner.Cell.Land;
        cells[4, 4] = PlanetScanner.Cell.Land;
        cells[4, 5] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 6, Height: 6, LatMin: -30, LatMax: 30, LonMin: -30, LonMax: 30);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        // The 2-cell landmass has more area than the 1-cell one, so it must be rank 1.
        var twoCellRank = detection.LandmassRankByCell[4, 4];
        Assert.AreEqual(twoCellRank, detection.LandmassRankByCell[4, 5]);
        Assert.AreEqual(1, twoCellRank);
        Assert.AreEqual(2, detection.LandmassRankByCell[1, 1]);
        Assert.AreEqual(0, detection.LandmassRankByCell[0, 0]); // ocean cell — no landmass
    }

    [TestMethod]
    public void Detect_FullGlobeWindow_WrapsLandAcrossTheAntimeridianSeam()
    {
        // A full 360° window: land in the last column and land in the first column of the SAME
        // row must merge into one landmass, since physically they're adjacent across the seam.
        var cells = new PlanetScanner.Cell[3, 10];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 10; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[1, 0] = PlanetScanner.Cell.Land;
        cells[1, 9] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 10, Height: 3, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(1, detection.Landmasses.Count, "Land at both edges of a full 360° window should merge across the seam.");
        Assert.AreEqual(2, detection.Landmasses[0].CellCount);
    }

    [TestMethod]
    public void Detect_PartialWindow_DoesNotWrapEvenWhenLandTouchesBothEdges()
    {
        // Same layout as the wrap test, but the window is NOT a full 360° — there's nothing on
        // the other side of these edges, so the two cells must stay separate landmasses.
        var cells = new PlanetScanner.Cell[3, 10];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 10; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[1, 0] = PlanetScanner.Cell.Land;
        cells[1, 9] = PlanetScanner.Cell.Land;
        var options = new PlanetScanner.Options(Width: 10, Height: 3, LatMin: -10, LatMax: 10, LonMin: 0, LonMax: 90);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(2, detection.Landmasses.Count);
    }

    [TestMethod]
    public void Detect_SeamCrossingLandmass_ReportsNarrowLonRangeNotFullGlobe()
    {
        // A landmass straddling the seam (near lon=180/-180 on both sides) must report a tight
        // bounding box around the seam, NOT the naive [-180, 180] a min/max-without-unwrapping
        // bug would produce.
        var cells = new PlanetScanner.Cell[3, 12];
        for (var r = 0; r < 3; r++) for (var c = 0; c < 12; c++) cells[r, c] = PlanetScanner.Cell.Ocean;
        cells[1, 0] = PlanetScanner.Cell.Land;  // lon = -180
        cells[1, 1] = PlanetScanner.Cell.Land;  // lon just east of -180
        cells[1, 10] = PlanetScanner.Cell.Land; // lon just west of +180
        cells[1, 11] = PlanetScanner.Cell.Land; // lon = +180 (== -180 on a full window)
        var options = new PlanetScanner.Options(Width: 12, Height: 3, LatMin: -10, LatMax: 10, LonMin: -180, LonMax: 180);

        var detection = LandmassDetector.Detect(BuildResult(cells, options), PlanetRadiusMeters);

        Assert.AreEqual(1, detection.Landmasses.Count);
        var lm = detection.Landmasses[0];
        var lonSpan = lm.LonMax - lm.LonMin;
        Assert.IsTrue(lonSpan < 180.0, $"Expected a narrow seam-hugging span, got [{lm.LonMin}, {lm.LonMax}] (span {lonSpan}).");
    }

    [TestMethod]
    public void Detect_SameInputs_IsDeterministic()
    {
        var cells = new PlanetScanner.Cell[8, 8];
        for (var r = 0; r < 8; r++) for (var c = 0; c < 8; c++) cells[r, c] = (r + c) % 3 == 0 ? PlanetScanner.Cell.Land : PlanetScanner.Cell.Ocean;
        var options = new PlanetScanner.Options(Width: 8, Height: 8, LatMin: -60, LatMax: 60, LonMin: -60, LonMax: 60);
        var result = BuildResult(cells, options);

        var a = LandmassDetector.Detect(result, PlanetRadiusMeters);
        var b = LandmassDetector.Detect(result, PlanetRadiusMeters);

        Assert.AreEqual(a.Landmasses.Count, b.Landmasses.Count);
        for (var i = 0; i < a.Landmasses.Count; i++)
            Assert.AreEqual(a.Landmasses[i], b.Landmasses[i]);
    }
}
