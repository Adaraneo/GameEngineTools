using System.Globalization;
using WorldGen;

namespace WorldGenTests;

[TestClass]
public class PlanetSettingsTests
{
    [TestMethod]
    public void ComputeSeed_IsInvariantAcrossCommaDecimalCulture()
    {
        // Same regression as TerraGenTests' own PlanetSettingsTests — WorldGen ported this
        // formula independently, so it needed the identical fix to stay in lockstep with
        // TerraGen's (and TerrainEditor's) seed for the same planet config.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantSeed = PlanetSettings.ComputeSeed("Vigilia Insectianis", 5.9726e24, 6378.1);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
            var czechSeed = PlanetSettings.ComputeSeed("Vigilia Insectianis", 5.9726e24, 6378.1);

            Assert.AreEqual(invariantSeed, czechSeed,
                "ComputeSeed must not depend on the ambient thread culture's decimal separator.");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Load_NoSettingsFile_FallsBackToEarthDefaults()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var resolved = PlanetSettings.Load(Path.Combine(dir.FullName, "world.db"));

            Assert.AreEqual(23.44, resolved.PlanetObliquityDeg, 1e-6);
            Assert.AreEqual(0.306, resolved.PlanetAlbedo, 1e-6);
            Assert.AreEqual(33.0, resolved.PlanetGreenhouseWarmingK, 1e-6);
            Assert.AreEqual(23.9345, resolved.PlanetSiderealRotationHrs, 1e-6);
            Assert.AreEqual(3.828e26, resolved.StarLuminosityWatts, 1e20);
            Assert.AreEqual(1.000001, resolved.OrbitSemiMajorAxisAu, 1e-6);
            Assert.AreEqual(0.01671022, resolved.OrbitEccentricity, 1e-8);
            Assert.IsFalse(resolved.HasRings);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Load_FromSettingsFile_ReadsPhysicsFieldsNeededForClimateStages()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, PlanetSettings.SettingsFileName), """
                {
                  "World": {
                    "Universe": {
                      "PlanetName": "TestWorld",
                      "PlanetMassKg": 5.9726e24,
                      "PlanetEquatorialRadiusKm": 6378.1,
                      "PlanetObliquityDeg": 45.0,
                      "PlanetAlbedo": 0.5,
                      "PlanetGreenhouseWarmingK": 20.0,
                      "PlanetSiderealRotationHrs": 10.0,
                      "StarLuminosityWatts": 1.0e26,
                      "OrbitSemiMajorAxisAu": 1.5,
                      "OrbitEccentricity": 0.2,
                      "HasRings": true,
                      "RingMeanOpticalDepth": 2.5
                    }
                  }
                }
                """);

            var resolved = PlanetSettings.Load(Path.Combine(dir.FullName, "world.db"));

            Assert.AreEqual(45.0, resolved.PlanetObliquityDeg, 1e-9);
            Assert.AreEqual(0.5, resolved.PlanetAlbedo, 1e-9);
            Assert.AreEqual(20.0, resolved.PlanetGreenhouseWarmingK, 1e-9);
            Assert.AreEqual(10.0, resolved.PlanetSiderealRotationHrs, 1e-9);
            Assert.AreEqual(1.0e26, resolved.StarLuminosityWatts, 1e18);
            Assert.AreEqual(1.5, resolved.OrbitSemiMajorAxisAu, 1e-9);
            Assert.AreEqual(0.2, resolved.OrbitEccentricity, 1e-9);
            Assert.IsTrue(resolved.HasRings);
            Assert.AreEqual(2.5, resolved.RingMeanOpticalDepth, 1e-9);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
