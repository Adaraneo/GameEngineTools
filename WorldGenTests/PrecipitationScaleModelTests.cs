using WorldGen;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class PrecipitationScaleModelTests
{
    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "world.db"));

    [TestMethod]
    public void ReferenceAnnualPrecipMm_EarthDefaults_MatchesEarthReferenceValue()
    {
        var value = PrecipitationScaleModel.ReferenceAnnualPrecipMm(EarthDefaults());

        Assert.AreEqual(1000.0, value, 1.0);
    }

    [TestMethod]
    public void ReferenceAnnualPrecipMm_HotterPlanet_IsHigher()
    {
        var earth = EarthDefaults();
        var hotter = earth with { StarLuminosityWatts = earth.StarLuminosityWatts * 1.5 };

        var earthValue = PrecipitationScaleModel.ReferenceAnnualPrecipMm(earth);
        var hotterValue = PrecipitationScaleModel.ReferenceAnnualPrecipMm(hotter);

        Assert.IsTrue(hotterValue > earthValue);
    }

    [TestMethod]
    public void ReferenceAnnualPrecipMm_ColderPlanet_IsLower()
    {
        var earth = EarthDefaults();
        var colder = earth with { OrbitSemiMajorAxisAu = earth.OrbitSemiMajorAxisAu * 1.5 };

        var earthValue = PrecipitationScaleModel.ReferenceAnnualPrecipMm(earth);
        var colderValue = PrecipitationScaleModel.ReferenceAnnualPrecipMm(colder);

        Assert.IsTrue(colderValue < earthValue);
    }
}
