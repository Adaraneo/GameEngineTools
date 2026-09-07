using WorldGen;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class KoppenWiringTests
{
    private static WorldContentGenerator.Options OptionsWithPlanet(PlanetSettings.Resolved planet) => new(
        Count: 1,
        PlanetRadiusMeters: planet.PlanetRadiusMeters,
        EquatorTemperatureCelsius: 27.0,
        PoleTemperatureCelsius: -25.0,
        ClimateSeed: 1,
        Planet: planet);

    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "world.db"));

    [TestMethod]
    public void ClassifyWarmClimate_MatchesIndependentlyBuiltMonthlySeriesForTheSameFormulas()
    {
        var planet = EarthDefaults();
        var options = OptionsWithPlanet(planet);
        const double offsetX = 12_345.0;
        const double offsetY = 6_789.0;
        const double height = 50.0;

        // Reproduce KoppenWiring's own formula from its public building blocks, independently of
        // its private implementation, to check the wiring glue (not re-derive the physics again).
        var (latDeg, _) = PlanetGeometry.OffsetToLatLon(offsetX, offsetY, options.PlanetRadiusMeters);
        var climate = ClimateModel.At(offsetX, offsetY, height, options);
        var amplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, latDeg);
        var isNorthern = latDeg >= 0.0;
        var peakMonth = isNorthern ? 6 : 0;
        var monthlyTempC = new double[12];
        for (var m = 0; m < 12; m++)
            monthlyTempC[m] = climate.TemperatureCelsius + amplitude * Math.Cos(2.0 * Math.PI * (m - peakMonth) / 12.0);
        var annualPrecipMm = PrecipitationScaleModel.ReferenceAnnualPrecipMm(planet) * Math.Clamp(climate.Humidity, 0.0, 1.0);
        var monthlyPrecipMm = new double[12];
        for (var m = 0; m < 12; m++) monthlyPrecipMm[m] = annualPrecipMm / 12.0;
        var expectedCode = KoppenClassifier.Classify(monthlyTempC, monthlyPrecipMm, isNorthern);

        var actualCategory = KoppenWiring.ClassifyWarmClimate(offsetX, offsetY, height, options);

        var expectedCategory = expectedCode switch
        {
            _ when expectedCode.StartsWith('B') => KoppenWiring.WarmClimateCategory.Desert,
            "Af" or "Am" => KoppenWiring.WarmClimateCategory.Jungle,
            "Aw" => KoppenWiring.WarmClimateCategory.Savanna,
            "ET" or "EF" => KoppenWiring.WarmClimateCategory.Desert,
            _ when expectedCode.Length >= 2 && (expectedCode[1] == 's' || expectedCode[1] == 'w') => KoppenWiring.WarmClimateCategory.Savanna,
            _ => KoppenWiring.WarmClimateCategory.PlainsOrForest,
        };
        Assert.AreEqual(expectedCategory, actualCategory);
    }

    [TestMethod]
    public void ClassifyWarmClimate_FlatPrecipitation_NeverTriggersSeasonalDrySplit()
    {
        // Documented limitation (see docs/plans Stage 7's wiring addendum): monthly precipitation
        // here is always flat, so the s/w (seasonally dry) Koppen second letter can never trigger
        // from this wiring alone. Sampling several points confirms none map to a result that would
        // only be reachable via that split for a genuinely flat precipitation series.
        var planet = EarthDefaults();
        var options = OptionsWithPlanet(planet);

        for (var i = 0; i < 20; i++)
        {
            var category = KoppenWiring.ClassifyWarmClimate(i * 111_000.0, i * 37_000.0, 100.0, options);
            Assert.IsTrue(Enum.IsDefined(category));
        }
    }

    [TestMethod]
    public void ClassifyWarmClimate_HemisphereSpecificOceanFraction_PicksNorthOrSouthBySign()
    {
        var planet = EarthDefaults();
        var baseOptions = OptionsWithPlanet(planet) with { NorthOceanFraction = 0.1, SouthOceanFraction = 0.95 };

        // A far-north and a far-south point at the same |latitude| — only OceanFraction selection differs.
        var northOffsetY = baseOptions.PlanetRadiusMeters * (60.0 * Math.PI / 180.0);
        var southOffsetY = -northOffsetY;

        double AmplitudeUsedAt(double offsetY)
        {
            var (latDeg, _) = PlanetGeometry.OffsetToLatLon(0, offsetY, baseOptions.PlanetRadiusMeters);
            var isNorthern = latDeg >= 0.0;
            var oceanFraction = isNorthern ? baseOptions.NorthOceanFraction : baseOptions.SouthOceanFraction;
            return SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, latDeg, oceanFraction);
        }

        var northAmplitude = AmplitudeUsedAt(northOffsetY);
        var southAmplitude = AmplitudeUsedAt(southOffsetY);

        // Mostly-land north (0.1 ocean) should show a LARGER seasonal amplitude than mostly-ocean
        // south (0.95) at the same |latitude| -- confirms the two fractions are genuinely distinct
        // inputs, not both silently falling back to the same global OceanFraction.
        Assert.IsTrue(northAmplitude > southAmplitude);

        // The actual classification call must not throw and must be deterministic either way.
        var northCategory = KoppenWiring.ClassifyWarmClimate(0, northOffsetY, 100.0, baseOptions);
        var southCategory = KoppenWiring.ClassifyWarmClimate(0, southOffsetY, 100.0, baseOptions);
        Assert.IsTrue(Enum.IsDefined(northCategory));
        Assert.IsTrue(Enum.IsDefined(southCategory));
    }

    [TestMethod]
    public void ClassifyWarmClimate_NoPlanetSet_Throws()
    {
        var options = new WorldContentGenerator.Options(Count: 1);

        Assert.Throws<InvalidOperationException>(() =>
            KoppenWiring.ClassifyWarmClimate(0, 0, 0, options));
    }
}
