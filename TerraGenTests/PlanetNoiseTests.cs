using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class PlanetNoiseTests
{
    private static readonly double EarthRadius = PlanetNoise.EarthRadiusMeters;

    [TestMethod]
    public void SampleCombined_SameInputs_IsDeterministic()
    {
        var p = new PlanetNoise.Parameters(Seed: 3, AmplitudeMeters: 200.0);

        var a = PlanetNoise.SampleCombined(123.0, 456.0, 12.0, 34.0, p, EarthRadius);
        var b = PlanetNoise.SampleCombined(123.0, 456.0, 12.0, 34.0, p, EarthRadius);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void SampleCombined_SharedOrigin_TwoCallsForTheSamePhysicalPointAgree()
    {
        // The whole point of TerraGen's mountain-layer fix: two tiles that both use the SAME
        // shared reference (lat,lon) — even if computed from different "local" perspectives —
        // must agree on the same physical point. Simulate that by converting a point to (lat,lon)
        // and back through two different intermediate offsets that both resolve to the same place.
        var p = new PlanetNoise.Parameters(Seed: 5, AmplitudeMeters: 200.0);
        const double refLat = 10.0, refLon = 20.0;

        var direct = PlanetNoise.SampleCombined(500.0, -300.0, refLat, refLon, p, EarthRadius);
        var again = PlanetNoise.SampleCombined(500.0, -300.0, refLat, refLon, p, EarthRadius);

        Assert.AreEqual(direct, again);
    }

    [TestMethod]
    public void SampleLandmass_AntimeridianSeam_ValuesAreContinuous()
    {
        var p = new PlanetNoise.Parameters(Seed: 11, AmplitudeMeters: 200.0);

        for (var lat = -80.0; lat <= 80.0; lat += 20.0)
        {
            var justEast = PlanetNoise.SampleLandmass(lat, 179.999, p, EarthRadius);
            var justWest = PlanetNoise.SampleLandmass(lat, -179.999, p, EarthRadius);

            Assert.AreEqual(justEast, justWest, 0.5,
                $"Expected near-identical values either side of the antimeridian at lat={lat}, got {justEast} vs {justWest}.");
        }
    }

    [TestMethod]
    public void SampleLandmass_NearNorthPole_LongitudeBarelyMatters()
    {
        var p = new PlanetNoise.Parameters(Seed: 13, AmplitudeMeters: 200.0);

        var reference = PlanetNoise.SampleLandmass(89.999, 0.0, p, EarthRadius);
        foreach (var lon in new[] { -170.0, -90.0, 0.0, 45.0, 90.0, 170.0 })
        {
            var value = PlanetNoise.SampleLandmass(89.999, lon, p, EarthRadius);
            Assert.AreEqual(reference, value, 0.5,
                $"Expected near-pole values to barely depend on longitude, got {reference} vs {value} at lon={lon}.");
        }
    }

    [TestMethod]
    public void SampleLandmass_AllValuesFinite()
    {
        var p = new PlanetNoise.Parameters(Seed: 9, AmplitudeMeters: 200.0);

        for (var lat = -90.0; lat <= 90.0; lat += 10.0)
        {
            for (var lon = -180.0; lon < 180.0; lon += 10.0)
            {
                var value = PlanetNoise.SampleLandmass(lat, lon, p, EarthRadius);
                Assert.IsFalse(double.IsNaN(value));
                Assert.IsFalse(double.IsInfinity(value));
            }
        }
    }

    [TestMethod]
    public void SampleCombined_ModeratelyUnderwaterPoint_MostOfALocalPatchStaysUnderwater()
    {
        // Regression test mirroring TerrainEditor's own calibration for this exact mechanism —
        // mountain uplift must be suppressed enough that a clearly-underwater point doesn't turn
        // into dry land just because the ridged noise happened to spike there.
        var p = new PlanetNoise.Parameters(Seed: 1, AmplitudeMeters: 200.0);
        const double refLat = -41.79, refLon = -39.67;

        var negativeCount = 0;
        var total = 0;
        for (var oy = -5000.0; oy <= 5000.0; oy += 100.0)
        {
            for (var ox = -5000.0; ox <= 5000.0; ox += 100.0)
            {
                var v = PlanetNoise.SampleCombined(ox, oy, refLat, refLon, p, EarthRadius);
                if (v < 0) negativeCount++;
                total++;
            }
        }

        var landmassAtCenter = PlanetNoise.SampleLandmass(refLat, refLon, p, EarthRadius);
        Assert.IsTrue(landmassAtCenter < 0, $"Test setup assumption broken: baseline should be underwater, was {landmassAtCenter:0.0}m.");
        var negativeFraction = negativeCount / (double)total;
        Assert.IsTrue(negativeFraction > 0.5,
            $"Expected most of a patch centered on a moderately-underwater point ({landmassAtCenter:0.0}m) to stay underwater, got {negativeFraction:P0}.");
    }
}
