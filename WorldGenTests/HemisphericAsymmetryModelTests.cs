using WorldGen;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class HemisphericAsymmetryModelTests
{
    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "world.db"));

    [TestMethod]
    public void PoleOffsetC_DefaultPeriapsisPhase_IsZeroForBothHemispheres()
    {
        var planet = EarthDefaults();

        Assert.AreEqual(0.0, HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: true), 1e-6);
        Assert.AreEqual(0.0, HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: false), 1e-6);
    }

    [TestMethod]
    public void PoleOffsetC_ZeroEccentricity_IsZeroRegardlessOfPeriapsisPhase()
    {
        var circular = EarthDefaults() with { OrbitEccentricity = 0.0, PeriapsisPhase = 0.783 };

        Assert.AreEqual(0.0, HemisphericAsymmetryModel.PoleOffsetC(circular, isNorthernHemisphere: true), 1e-6);
        Assert.AreEqual(0.0, HemisphericAsymmetryModel.PoleOffsetC(circular, isNorthernHemisphere: false), 1e-6);
    }

    [TestMethod]
    public void PoleOffsetC_EarthLikePerihelionNearNorthernWinter_WarmsNorthAndCoolsSouth()
    {
        // Perihelion ~12 days after the December solstice on real Earth -> lambda ~281.8deg -> phase ~0.783.
        var planet = EarthDefaults() with { PeriapsisPhase = 0.783 };

        var north = HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: true);
        var south = HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: false);

        Assert.IsTrue(north > 0.0, "Northern winter near perihelion should be milder (positive pole offset).");
        Assert.IsTrue(south < 0.0, "Southern winter near aphelion should be harsher (negative pole offset).");
    }

    [TestMethod]
    public void PoleOffsetC_IsSmallRelativeToObliquityDrivenAmplitude()
    {
        // Literature: eccentricity's ~6.8% insolation swing "pales in comparison" to obliquity's
        // effect -- this offset should stay well under the full seasonal amplitude it's derived from.
        var planet = EarthDefaults() with { PeriapsisPhase = 0.783 };
        var offset = Math.Abs(HemisphericAsymmetryModel.PoleOffsetC(planet, isNorthernHemisphere: true));
        var fullAmplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 90.0);

        Assert.IsTrue(offset < fullAmplitude * 0.1,
            "The eccentricity-driven offset should be a small fraction of the full obliquity-driven amplitude.");
    }

    [TestMethod]
    public void ClimateModel_At_WithHemisphericOffsets_PicksCorrectPoleBySignOfLatitude()
    {
        var options = new WorldContentGenerator.Options(
            Count: 1,
            PlanetRadiusMeters: 6_378_100.0,
            EquatorTemperatureCelsius: 27.0,
            PoleTemperatureCelsius: -25.0,
            NorthPoleTemperatureCelsius: -20.0,
            SouthPoleTemperatureCelsius: -30.0,
            ClimateSeed: 1);

        var northPoleOffset = options.PlanetRadiusMeters * (89.9 * Math.PI / 180.0);
        var northSample = ClimateModel.At(0, northPoleOffset, 0, options);
        var southSample = ClimateModel.At(0, -northPoleOffset, 0, options);

        Assert.AreEqual(-20.0, northSample.TemperatureCelsius, 0.5);
        Assert.AreEqual(-30.0, southSample.TemperatureCelsius, 0.5);
    }

    [TestMethod]
    public void ClimateModel_At_NoHemisphericOverrides_FallsBackToPoleTemperatureCelsius()
    {
        var options = new WorldContentGenerator.Options(
            Count: 1,
            PlanetRadiusMeters: 6_378_100.0,
            EquatorTemperatureCelsius: 27.0,
            PoleTemperatureCelsius: -25.0,
            ClimateSeed: 1);

        var poleOffset = options.PlanetRadiusMeters * (89.9 * Math.PI / 180.0);
        var northSample = ClimateModel.At(0, poleOffset, 0, options);
        var southSample = ClimateModel.At(0, -poleOffset, 0, options);

        Assert.AreEqual(-25.0, northSample.TemperatureCelsius, 0.5);
        Assert.AreEqual(-25.0, southSample.TemperatureCelsius, 0.5);
    }
}
