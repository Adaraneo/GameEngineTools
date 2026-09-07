namespace WorldGen.Generation;

/// <summary>Wires Stage 7/8/9 (Koppen classification, seasonal amplitude, precipitation scale) into WorldContentGenerator's warm-climate biome split — see docs/plans/planet-physics-driven-climate.md Stage 7.</summary>
// [DESIGN SIMPLIFICATION] Monthly precipitation is flat (annual total / 12 every month) -- ClimateModel's humidity field has no modeled seasonality, so Koppen's own seasonal-precip distinctions (Am vs Aw, Cs vs Cw) never trigger from precipitation shape alone here; the s/w mapping below still applies when they DO trigger (e.g. from a future seasonal-precip model), so nothing needs to change on this end if that's added later.
public static class KoppenWiring
{
    private const int NorthernHemispherePeakMonth = 6; // July -- no thermal-lag term, Stage 9's own "cheapest defensible default"
    private const int SouthernHemispherePeakMonth = 0; // January

    public enum WarmClimateCategory { Desert, Jungle, Savanna, PlainsOrForest }

    /// <summary>Classifies the warm-climate split (Desert/Jungle/Savanna/Plains-or-Forest) via a real Koppen-Geiger code instead of the ad hoc humidity+temperature thresholds. Requires <see cref="WorldContentGenerator.Options.Planet"/> to be set.</summary>
    public static WarmClimateCategory ClassifyWarmClimate(double offsetXMeters, double offsetYMeters, double heightMeters, WorldContentGenerator.Options options)
    {
        var planet = options.Planet ?? throw new InvalidOperationException("KoppenWiring requires Options.Planet to be set.");
        var (latDeg, _) = PlanetGeometry.OffsetToLatLon(offsetXMeters, offsetYMeters, options.PlanetRadiusMeters);
        var climate = ClimateModel.At(offsetXMeters, offsetYMeters, heightMeters, options);

        var amplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, latDeg);
        var isNorthernHemisphere = latDeg >= 0.0;
        var peakMonth = isNorthernHemisphere ? NorthernHemispherePeakMonth : SouthernHemispherePeakMonth;

        var monthlyTempC = new double[12];
        for (var m = 0; m < 12; m++)
            monthlyTempC[m] = climate.TemperatureCelsius + amplitude * Math.Cos(2.0 * Math.PI * (m - peakMonth) / 12.0);

        var annualPrecipMm = PrecipitationScaleModel.ReferenceAnnualPrecipMm(planet) * Math.Clamp(climate.Humidity, 0.0, 1.0);
        var monthlyPrecipMm = new double[12];
        for (var m = 0; m < 12; m++) monthlyPrecipMm[m] = annualPrecipMm / 12.0;

        var koppenCode = KoppenClassifier.Classify(monthlyTempC, monthlyPrecipMm, isNorthernHemisphere);
        return MapToWarmClimateCategory(koppenCode);
    }

    private static WarmClimateCategory MapToWarmClimateCategory(string koppenCode)
    {
        if (koppenCode.StartsWith('B')) return WarmClimateCategory.Desert;
        if (koppenCode is "Af" or "Am") return WarmClimateCategory.Jungle;
        if (koppenCode is "Aw") return WarmClimateCategory.Savanna;
        // Reached only if seasonal cold dips below the (annual-mean-only) Tundra threshold upstream missed --
        // ClassifyBiome already ruled out Tundra for this candidate, so treat as harsh/marginal like Desert.
        if (koppenCode is "ET" or "EF") return WarmClimateCategory.Desert;
        if (koppenCode.Length >= 2 && (koppenCode[1] == 's' || koppenCode[1] == 'w')) return WarmClimateCategory.Savanna;
        return WarmClimateCategory.PlainsOrForest;
    }
}
