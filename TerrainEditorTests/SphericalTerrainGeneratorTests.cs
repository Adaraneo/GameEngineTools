using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class SphericalTerrainGeneratorTests
{
    private static TerrainHeightmap MakeBlankGrid(int width = 21, int height = 21, double cellSize = 10.0)
        => new(
            Id: "test",
            OriginX: 0.0,
            OriginY: 0.0,
            CellSizeMeters: cellSize,
            Width: width,
            Height: height,
            Values: new float[width * height]);

    private static readonly double EarthRadius = TerrainGenerator.EarthRadiusMeters;

    [TestMethod]
    public void SampleSphereLandmass_SameInputs_IsDeterministic()
    {
        var p = new TerrainGenerator.Parameters(Seed: 7, LandmassWavelengthMeters: 500_000.0);

        var a = TerrainGenerator.SampleSphereLandmass(12.5, 34.7, p, EarthRadius);
        var b = TerrainGenerator.SampleSphereLandmass(12.5, 34.7, p, EarthRadius);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SampleSphereLandmass_DifferentSeeds_ProduceDifferentValues()
    {
        var p1 = new TerrainGenerator.Parameters(Seed: 1, LandmassWavelengthMeters: 500_000.0);
        var p2 = new TerrainGenerator.Parameters(Seed: 2, LandmassWavelengthMeters: 500_000.0);

        var a = TerrainGenerator.SampleSphereLandmass(12.5, 34.7, p1, EarthRadius);
        var b = TerrainGenerator.SampleSphereLandmass(12.5, 34.7, p2, EarthRadius);

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void SampleSphereLandmass_StaysWithinDeclaredBound()
    {
        var p = new TerrainGenerator.Parameters(Seed: 3, AmplitudeMeters: 200.0,
            LandmassAmplitudeFraction: 0.65, LandmassWavelengthMeters: 300_000.0);
        var bound = 200.0 * 0.65 / 2.0;

        for (var lat = -90.0; lat <= 90.0; lat += 15.0)
        {
            for (var lon = -180.0; lon < 180.0; lon += 20.0)
            {
                var value = TerrainGenerator.SampleSphereLandmass(lat, lon, p, EarthRadius);
                Assert.IsTrue(Math.Abs(value) <= bound + 1e-6,
                    $"Value {value} at ({lat},{lon}) exceeds declared bound ±{bound}.");
            }
        }
    }

    [TestMethod]
    public void SampleSphereLandmass_AverageLandFractionAcrossSeeds_IsRoughlyEarthlike()
    {
        // Regression test for a real reported issue: the default "Earth" planet came out mostly
        // land with a few small seas — pure luck-of-the-seed, since raw symmetric fBm noise splits
        // land/sea roughly 50/50 on average. ContinentSeaBias shifts that average toward Earth's
        // actual ~29% land / 71% ocean. Any ONE seed still varies (that's real coastline variety),
        // so this checks the AVERAGE over many seeds, not any single one.
        var earthRadius = TerrainGenerator.EarthRadiusMeters;
        var negativeCount = 0;
        var totalCount = 0;

        for (var seed = 100; seed < 120; seed++)
        {
            var p = new TerrainGenerator.Parameters(Seed: seed, AmplitudeMeters: 200.0);
            for (var lat = -85.0; lat <= 85.0; lat += 5.0)
            {
                for (var lon = -175.0; lon <= 175.0; lon += 5.0)
                {
                    var value = TerrainGenerator.SampleSphereLandmass(lat, lon, p, earthRadius);
                    if (value < 0) negativeCount++;
                    totalCount++;
                }
            }
        }

        var oceanFraction = negativeCount / (double)totalCount;
        Assert.IsTrue(oceanFraction is > 0.55 and < 0.85,
            $"Expected an Earth-like ocean-heavy average (roughly 0.71) across many seeds, got {oceanFraction:P0}.");
    }

    [TestMethod]
    public void SampleSphereLandmass_AllValuesFinite()
    {
        var p = new TerrainGenerator.Parameters(Seed: 9, LandmassWavelengthMeters: 400_000.0);

        for (var lat = -90.0; lat <= 90.0; lat += 10.0)
        {
            for (var lon = -180.0; lon < 180.0; lon += 10.0)
            {
                var value = TerrainGenerator.SampleSphereLandmass(lat, lon, p, EarthRadius);
                Assert.IsFalse(double.IsNaN(value));
                Assert.IsFalse(double.IsInfinity(value));
            }
        }
    }

    [TestMethod]
    public void SampleSphereLandmass_AntimeridianSeam_ValuesAreContinuous()
    {
        // 3D-sphere sampling must not jump at lon=±180° — a 2D equirectangular noise field would.
        var p = new TerrainGenerator.Parameters(Seed: 11, AmplitudeMeters: 200.0, LandmassWavelengthMeters: 500_000.0);

        for (var lat = -80.0; lat <= 80.0; lat += 20.0)
        {
            var justEast = TerrainGenerator.SampleSphereLandmass(lat, 179.999, p, EarthRadius);
            var justWest = TerrainGenerator.SampleSphereLandmass(lat, -179.999, p, EarthRadius);

            Assert.AreEqual(justEast, justWest, 0.5,
                $"Expected near-identical values either side of the antimeridian at lat={lat}, got {justEast} vs {justWest}.");
        }
    }

    [TestMethod]
    public void SampleSphereLandmass_NearNorthPole_LongitudeBarelyMatters()
    {
        // At lat≈90°, every longitude is nearly the same physical point on the sphere — a naive
        // 2D lat/lon grid would show wild variation here (the classic "pole singularity"); true
        // 3D sphere sampling must not.
        var p = new TerrainGenerator.Parameters(Seed: 13, AmplitudeMeters: 200.0, LandmassWavelengthMeters: 500_000.0);

        var reference = TerrainGenerator.SampleSphereLandmass(89.999, 0.0, p, EarthRadius);
        foreach (var lon in new[] { -170.0, -90.0, 0.0, 45.0, 90.0, 170.0 })
        {
            var value = TerrainGenerator.SampleSphereLandmass(89.999, lon, p, EarthRadius);
            Assert.AreEqual(reference, value, 0.5,
                $"Expected near-pole values to barely depend on longitude, got {reference} vs {value} at lon={lon}.");
        }
    }

    [TestMethod]
    public void GenerateSphere_CellValue_MatchesDirectSphereSampleAtThatCellsLatLon()
    {
        // LandmassAmplitudeFraction=1 isolates the landmass term (mountain contributes nothing),
        // so this proves GenerateSphere's per-cell equirectangular projection feeds the exact
        // same (lat,lon) into SampleSphereLandmass that the overview map would show there.
        var grid = MakeBlankGrid();
        var p = new TerrainGenerator.Parameters(Seed: 5, AmplitudeMeters: 200.0,
            LandmassAmplitudeFraction: 1.0, LandmassWavelengthMeters: 500_000.0);
        const double centerLat = 12.3, centerLon = 45.6;

        TerrainGenerator.GenerateSphere(grid, centerLat, centerLon, p, EarthRadius);

        const int gx = 7, gy = 13;
        var worldX = grid.OriginX + gx * grid.CellSizeMeters;
        var worldY = grid.OriginY + gy * grid.CellSizeMeters;
        var centerWorldX = grid.OriginX + grid.Width / 2.0 * grid.CellSizeMeters;
        var centerWorldY = grid.OriginY + grid.Height / 2.0 * grid.CellSizeMeters;
        var offsetX = worldX - centerWorldX;
        var offsetY = worldY - centerWorldY;
        var centerLatRad = centerLat * Math.PI / 180.0;
        var lat = centerLat + offsetY / EarthRadius * (180.0 / Math.PI);
        var lon = centerLon + offsetX / (EarthRadius * Math.Cos(centerLatRad)) * (180.0 / Math.PI);

        var expected = TerrainGenerator.SampleSphereLandmass(lat, lon, p, EarthRadius);
        var actual = grid.Values[gy * grid.Width + gx];

        Assert.AreEqual(expected, actual, 1e-3);
    }

    [TestMethod]
    public void GenerateSphere_AllValuesFinite()
    {
        var grid = MakeBlankGrid(40, 40, 10.0);
        var p = new TerrainGenerator.Parameters(Seed: 21, AmplitudeMeters: 200.0, LandmassWavelengthMeters: 500_000.0);

        TerrainGenerator.GenerateSphere(grid, 51.5, -0.1, p, EarthRadius);

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v));
            Assert.IsFalse(float.IsInfinity(v));
        }
    }

    [TestMethod]
    public void GenerateSphere_SameSeedAndCenter_IsDeterministic()
    {
        var gridA = MakeBlankGrid();
        var gridB = MakeBlankGrid();
        var p = new TerrainGenerator.Parameters(Seed: 8, AmplitudeMeters: 200.0, LandmassWavelengthMeters: 500_000.0);

        TerrainGenerator.GenerateSphere(gridA, 30.0, 60.0, p, EarthRadius);
        TerrainGenerator.GenerateSphere(gridB, 30.0, 60.0, p, EarthRadius);

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void GenerateSphere_ModeratelyUnderwaterPoint_MostOfTheWindowStaysUnderwater()
    {
        // Regression test reproducing a real reported bug: picking a point the overview showed as
        // sea (-13m landmass baseline) generated a local window where only 17 of 22,801 cells
        // stayed underwater — mountain uplift (+25m at the center alone) applied at full strength
        // regardless of depth, turning "clearly sea" into "almost entirely dry land". MountainSuppression
        // fixes this by damping uplift the deeper the landmass baseline already is.
        var p = new TerrainGenerator.Parameters(Seed: 1, AmplitudeMeters: 200.0);
        const double lat = -41.79, lon = -39.67; // the exact point from the bug report

        var landmassAtCenter = TerrainGenerator.SampleSphereLandmass(lat, lon, p, EarthRadius);
        Assert.IsTrue(landmassAtCenter < 0,
            $"Test setup assumption broken: this point's landmass baseline should be underwater, was {landmassAtCenter:0.0}m.");

        var grid = MakeBlankGrid(151, 151, 66.67); // ~10km window, matching the bug report
        TerrainGenerator.GenerateSphere(grid, lat, lon, p, EarthRadius);

        var negativeFraction = grid.Values.Count(v => v < 0) / (double)grid.Values.Length;
        Assert.IsTrue(negativeFraction > 0.5,
            $"Expected most of a window centered on a moderately-underwater point ({landmassAtCenter:0.0}m) " +
            $"to stay underwater, got only {negativeFraction:P0} negative cells.");
    }
}
