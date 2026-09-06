// ClimateModel.cs
// Copyright (c) 50PSoftware

namespace WorldGen.Generation;

/// <summary>
/// Lightweight climate approximation for a WorldGen candidate point — temperature from an
/// equator-to-pole latitude gradient plus a fixed adiabatic lapse rate for altitude, and humidity
/// from an independent large-scale noise field with a small altitude-drying term (higher ground
/// holds less moisture). Not a real climate/atmosphere simulation — no seasons, no wind, no
/// prevailing-wind rain shadows — same "lightweight approximation" spirit as TerraGen's own
/// <see cref="TectonicPlates"/>, just enough spatial variation to tell Desert/Savanna/Jungle/Tundra
/// apart from Plains/Forest beyond raw elevation and slope. Biome thresholds follow the broad
/// shape of the Köppen–Geiger classification (see Kottek et al. 2006, Meteorol. Z. 15:259–263)
/// without reproducing its full rule set.
/// </summary>
/// <remarks>
/// The humidity noise is an independent port of the value-noise fBm technique in TerraGen's
/// <c>PlanetNoise</c> (no project reference from WorldGen to TerraGen; see WorldGen.csproj) —
/// restricted to 2D, since humidity doesn't need the sphere-wrapping 3D sampling the landmass
/// layer uses to stay seamless at the poles/antimeridian.
/// </remarks>
public static class ClimateModel
{
    /// <param name="TemperatureCelsius">Ambient temperature at this point.</param>
    /// <param name="Humidity">Ambient relative wetness, clamped to [0, 1].</param>
    public readonly record struct Sample(double TemperatureCelsius, double Humidity);

    private const int Octaves = 4;
    private const double Persistence = 0.5;
    private const double Lacunarity = 2.0;

    /// <summary>Fixed phase offset (in wavelength units) applied before sampling humidity noise —
    /// avoids every planet's humidity field sharing the same value at the reference origin (0,0),
    /// which would otherwise sit exactly on an integer lattice point of the noise grid.</summary>
    private const double HumidityPhaseOffset = 7.318;

    /// <summary>Samples temperature and humidity at a flat-offset point. <paramref name="options"/>
    /// supplies both the climate constants and the <see cref="WorldContentGenerator.Options.PlanetRadiusMeters"/>
    /// needed to recover latitude via <see cref="PlanetGeometry.OffsetToLatLon"/>.</summary>
    public static Sample At(double offsetXMeters, double offsetYMeters, double heightMeters, WorldContentGenerator.Options options)
    {
        var (latDeg, _) = PlanetGeometry.OffsetToLatLon(offsetXMeters, offsetYMeters, options.PlanetRadiusMeters);
        var latRad = latDeg * Math.PI / 180.0;
        var latitudeFactor = Math.Sin(latRad) * Math.Sin(latRad); // 0 at the equator, 1 at the poles

        var altitudeAboveSeaKm = Math.Max(heightMeters, 0.0) / 1000.0;
        var temperature = double.Lerp(options.EquatorTemperatureCelsius, options.PoleTemperatureCelsius, latitudeFactor)
                           - options.LapseRateCPerKm * altitudeAboveSeaKm
                           - RingShadowCoolingC(latDeg, options);

        var humidityNoise = Fbm2D(
            offsetXMeters / options.HumidityWavelengthMeters + HumidityPhaseOffset,
            offsetYMeters / options.HumidityWavelengthMeters + HumidityPhaseOffset,
            options.ClimateSeed);
        var humidity = (humidityNoise + 1.0) / 2.0 - options.AltitudeDrynessPerKm * altitudeAboveSeaKm;

        return new Sample(temperature, Math.Clamp(humidity, 0.0, 1.0));
    }

    /// <summary>Annual-mean ring-shadow cooling (°C) at this latitude — see docs/plans/planet-physics-driven-climate.md Stage 5.</summary>
    private static double RingShadowCoolingC(double latDeg, WorldContentGenerator.Options options)
    {
        if (!options.HasRings || options.RingShadowHalfWidthDeg <= 0) return 0.0;

        var absLat = Math.Abs(latDeg);
        if (absLat >= options.RingShadowHalfWidthDeg) return 0.0;

        var tapered = 1.0 - absLat / options.RingShadowHalfWidthDeg;
        return options.RingShadowMaxCoolingC * options.RingMeanOpticalDepth * tapered;
    }

    private static double Fbm2D(double x, double y, int seed)
    {
        var amplitude = 1.0;
        var frequency = 1.0;
        var sum = 0.0;
        var norm = 0.0;
        for (var o = 0; o < Octaves; o++)
        {
            sum += ValueNoise2D(x * frequency, y * frequency, seed + o * 101) * amplitude;
            norm += amplitude;
            amplitude *= Persistence;
            frequency *= Lacunarity;
        }
        return sum / norm;
    }

    private static double ValueNoise2D(double x, double y, int seed)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = x0 + 1;
        var y1 = y0 + 1;

        var tx = Smoothstep(x - x0);
        var ty = Smoothstep(y - y0);

        var v00 = LatticeValue(x0, y0, seed);
        var v10 = LatticeValue(x1, y0, seed);
        var v01 = LatticeValue(x0, y1, seed);
        var v11 = LatticeValue(x1, y1, seed);

        var top = v00 + (v10 - v00) * tx;
        var bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }

    private static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);

    private static double LatticeValue(int x, int y, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            var frac = (h & 0xFFFFFF) / (double)0xFFFFFF;
            return frac * 2.0 - 1.0;
        }
    }
}
