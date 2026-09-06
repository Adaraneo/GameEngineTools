using WorldGen;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class PlanetaryTemperatureModelTests
{
    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "world.db"));

    [TestMethod]
    public void DeriveEquatorPoleTemperatures_EarthDefaultsAtEpochZero_MatchesLegacyHardcodedPair()
    {
        var (equatorC, poleC) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(EarthDefaults(), epochKyr: 0.0);

        Assert.AreEqual(27.0, equatorC, 0.05);
        Assert.AreEqual(-25.0, poleC, 0.05);
    }

    [TestMethod]
    public void DeriveEquatorPoleTemperatures_HotterBrighterStar_ShiftsBothValuesUp()
    {
        var earth = EarthDefaults();
        var hotter = earth with { StarLuminosityWatts = earth.StarLuminosityWatts * 2.0 };

        var (equatorEarth, poleEarth) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(earth);
        var (equatorHot, poleHot) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(hotter);

        Assert.IsTrue(equatorHot > equatorEarth);
        Assert.IsTrue(poleHot > poleEarth);
    }

    [TestMethod]
    public void DeriveEquatorPoleTemperatures_HigherGreenhouseWarming_ShiftsBothValuesUp()
    {
        var earth = EarthDefaults();
        var greenhouse = earth with { PlanetGreenhouseWarmingK = earth.PlanetGreenhouseWarmingK + 20.0 };

        var (equatorEarth, poleEarth) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(earth);
        var (equatorHot, poleHot) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(greenhouse);

        Assert.IsTrue(equatorHot > equatorEarth);
        Assert.IsTrue(poleHot > poleEarth);
    }

    [TestMethod]
    public void DeriveEquatorPoleTemperatures_HigherObliquity_NarrowsTheGradient()
    {
        var earth = EarthDefaults();
        var tilted = earth with { PlanetObliquityDeg = earth.PlanetObliquityDeg * 2.0 };

        var (equatorEarth, poleEarth) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(earth);
        var (equatorTilted, poleTilted) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(tilted);

        Assert.IsTrue(equatorTilted - poleTilted < equatorEarth - poleEarth,
            "Higher obliquity should narrow the equator/pole gradient.");
    }

    [TestMethod]
    public void DeriveEquatorPoleTemperatures_NonZeroEpoch_ShiftsPoleAwayFromBaseline()
    {
        var earth = EarthDefaults();

        var (_, poleAtEpochZero) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(earth, epochKyr: 0.0);
        var (_, poleAtQuarterCycle) = PlanetaryTemperatureModel.DeriveEquatorPoleTemperatures(earth, epochKyr: 41.0 / 4.0);

        Assert.AreNotEqual(poleAtEpochZero, poleAtQuarterCycle, 1e-9);
    }
}
