// AstroConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

/// <summary>
/// Settings for the host star and observer location used by
/// <see cref="CelestialContextComputer"/> calculations. Bound from configuration
/// under the <c>World:Astro</c> key.
/// </summary>
public sealed record AstroConfig(
    /// <summary>Parameters of the host star and orbit.</summary>
    SunParamsConfig Sun = null!,
    /// <summary>Latitude of the world's default location (°). Affects day length and irradiance intensity.</summary>
    double LatitudeDeg = 50.0,
    /// <summary>Longitude of the world's default location (°).</summary>
    double LongitudeDeg = 15.0,
    /// <summary>Mean annual ambient temperature (°C).</summary>
    double BaseTemperatureCelsius = 11.0,
    /// <summary>Half-amplitude of the seasonal temperature swing (°C). Summer = +amplitude, winter = −amplitude.</summary>
    double SeasonalAmplitudeCelsius = 9.0,
    /// <summary>
    /// Vernal-equinox phase as a fraction of the year [0, 1).
    /// <c>0.0</c> = vernal equinox on the first day of the year.
    /// </summary>
    double VernalPhase = 0.0,
    /// <summary>
    /// Thermal lag of surface temperature behind insolation, as a fraction of the year.
    /// Real oceans and soil warm slowly, so the hottest day trails the summer solstice by
    /// roughly a month on Earth (≈ <c>0.08</c> of a year). <c>0.0</c> = no lag (temperature
    /// peaks exactly at the solstice).
    /// </summary>
    double SeasonalThermalLagFraction = 0.08)
{
    /// <summary>Default instance with Earth-like values.</summary>
    public AstroConfig() : this(new SunParamsConfig()) { }
}

/// <summary>
/// Serializable stand-in for <see cref="SunParams"/> for IOptions binding.
/// Conversion to <see cref="SunParams"/> is performed by <see cref="CelestialContextComputer"/>.
/// </summary>
public sealed record SunParamsConfig(
    double AxialTiltDeg = 23.44,
    double Eccentricity = 0.0167,
    double PeriapsisPhase = 0.0,
    double RefractionDeg = 0.566,
    double ApparentRadiusDeg = 0.266,
    double TwilightCivilDeg = 6.0,
    double TwilightNauticalDeg = 12.0,
    double TwilightAstronomicalDeg = 18.0)
{
    /// <summary>Default instance with approximately Earth-like values.</summary>
    public SunParamsConfig() : this(23.44) { }

    /// <summary>Converts the config record into a <see cref="SunParams"/> struct.</summary>
    public SunParams ToSunParams() => new(
        AxialTiltDeg,
        Eccentricity,
        PeriapsisPhase,
        RefractionDeg,
        ApparentRadiusDeg,
        TwilightCivilDeg,
        TwilightNauticalDeg,
        TwilightAstronomicalDeg);
}
