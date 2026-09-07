using TerraGen;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class LandmassNoiseCalibrationTests
{
    /// <summary>Empirically measures the realized land fraction for a given TargetOceanFraction, cross-checking PlanetNoise's calibrated bias against real generated output. One point per seed -- points within one seed share the low-frequency octave structure, so many-points-few-seeds badly understates variance (a real bug caught this way before landing, see PlanetNoise's own calibration comment).</summary>
    private static double MeasureLandFraction(double targetOceanFraction, int seeds = 20000)
    {
        var rng = new Random(2026_09_08);
        var landCount = 0;

        for (var s = 0; s < seeds; s++)
        {
            var p = new PlanetNoise.Parameters(Seed: rng.Next(), TargetOceanFraction: targetOceanFraction);
            var lat = rng.NextDouble() * 180.0 - 90.0;
            var lon = rng.NextDouble() * 360.0 - 180.0;
            if (PlanetNoise.SampleLandmass(lat, lon, p, 6_378_100.0) >= 0) landCount++;
        }

        return landCount / (double)seeds;
    }

    [TestMethod]
    public void SampleLandmass_DefaultTargetOceanFraction_RealizesApproximately29PercentLand()
    {
        var landFraction = MeasureLandFraction(targetOceanFraction: 0.71);

        Assert.AreEqual(0.29, landFraction, 0.03,
            "Default TargetOceanFraction=0.71 should realize close to 29% land across many seeds/points.");
    }

    [TestMethod]
    public void SampleLandmass_MostlyOceanTarget_RealizesLessLandThanDefault()
    {
        var mostlyOcean = MeasureLandFraction(targetOceanFraction: 0.9);
        var earthLike = MeasureLandFraction(targetOceanFraction: 0.71);

        Assert.IsTrue(mostlyOcean < earthLike);
    }

    [TestMethod]
    public void SampleLandmass_MostlyLandTarget_RealizesMoreLandThanDefault()
    {
        var mostlyLand = MeasureLandFraction(targetOceanFraction: 0.3);
        var earthLike = MeasureLandFraction(targetOceanFraction: 0.71);

        Assert.IsTrue(mostlyLand > earthLike);
    }
}
