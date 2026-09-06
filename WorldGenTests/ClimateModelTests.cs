using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class ClimateModelTests
{
    private static WorldContentGenerator.Options BaseOptions() => new(
        Count: 1,
        PlanetRadiusMeters: 6_378_100.0,
        EquatorTemperatureCelsius: 27.0,
        PoleTemperatureCelsius: -25.0,
        ClimateSeed: 1);

    [TestMethod]
    public void At_NoRings_MatchesLatitudeGradientOnly()
    {
        var options = BaseOptions() with { HasRings = false };

        var equator = ClimateModel.At(0, 0, 0, options);

        Assert.AreEqual(27.0, equator.TemperatureCelsius, 0.01);
    }

    [TestMethod]
    public void At_WithRings_CoolsEquatorMoreThanBandEdge()
    {
        var options = BaseOptions() with
        {
            HasRings = true,
            RingMeanOpticalDepth = 2.0,
            RingShadowHalfWidthDeg = 20.0,
            RingShadowMaxCoolingC = 5.0,
        };

        // Offset (x, 0) so latitude at the equator is exactly 0deg via PlanetGeometry.OffsetToLatLon.
        var equatorSample = ClimateModel.At(0, 0, 0, options);
        var withoutRings = ClimateModel.At(0, 0, 0, options with { HasRings = false });

        Assert.AreEqual(17.0, equatorSample.TemperatureCelsius, 0.01,
            "Equator cooling of MaxCooling * OpticalDepth = 5*2 = 10C below the un-ringed 27C baseline.");
        Assert.IsTrue(equatorSample.TemperatureCelsius < withoutRings.TemperatureCelsius);
    }

    [TestMethod]
    public void At_WithRings_OutsideShadowBand_IsUnaffected()
    {
        var options = BaseOptions() with
        {
            HasRings = true,
            RingMeanOpticalDepth = 2.0,
            RingShadowHalfWidthDeg = 5.0,
            RingShadowMaxCoolingC = 5.0,
        };

        // Offset far enough north that latitude exceeds the 5deg shadow half-width.
        var farOffsetMeters = options.PlanetRadiusMeters * (30.0 * Math.PI / 180.0);
        var farSample = ClimateModel.At(0, farOffsetMeters, 0, options);
        var farSampleNoRings = ClimateModel.At(0, farOffsetMeters, 0, options with { HasRings = false });

        Assert.AreEqual(farSampleNoRings.TemperatureCelsius, farSample.TemperatureCelsius, 0.01);
    }
}
