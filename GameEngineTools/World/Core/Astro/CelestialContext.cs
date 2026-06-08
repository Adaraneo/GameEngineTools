// CelestialContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

/// <summary>
/// Snapshot of astronomical conditions for a single simulation tick.
/// Computed by <see cref="CelestialContextComputer"/> once per tick and passed to every
/// character via <see cref="GameEngineTools.Characters.Core.EnginesSnapshot.Celestial"/>.
/// </summary>
/// <param name="IrradianceFactor">
/// Relative irradiance from <see cref="SunModel.IrradianceFactor"/>. 0 at night or polar night,
/// ≈ 1 for perpendicular radiation at mean distance, &gt; 1 at perihelion.
/// </param>
/// <param name="DaylightHours">Day length in game hours (0..HoursPerDay).</param>
/// <param name="SunriseHour">Hour of sunrise. <see cref="double.NaN"/> during polar night.</param>
/// <param name="SunsetHour">Hour of sunset. <see cref="double.NaN"/> during polar night.</param>
/// <param name="SolarNoonHour">Hour of solar noon.</param>
/// <param name="SeasonFraction">
/// Phase of the year: 0.0 = vernal equinox, 0.25 = summer solstice,
/// 0.5 = autumnal equinox, 0.75 = winter solstice.
/// </param>
/// <param name="VernalPhase">Configured vernal-equinox phase (passed through from <see cref="AstroConfig"/>).</param>
/// <param name="IsDay"><c>true</c> pokud je <paramref name="IrradianceFactor"/> &gt; 0.</param>
/// <param name="BaseAmbientTempCelsius">
/// Seasonal ambient temperature (°C): <c>baseTempC + amplitudeC × sin(SeasonFraction × 2π − π/2)</c>.
/// Maximum in summer (0.25), minimum in winter (0.75).
/// </param>
/// <param name="SurfaceGravityVsEarth">
/// Surface gravity of the planet as a multiple of Earth's (phase 2 — from <c>PlanetConfig</c>).
/// Default 1.0 (phase 1).
/// </param>
public sealed record CelestialContext(
    double IrradianceFactor,
    double DaylightHours,
    double SunriseHour,
    double SunsetHour,
    double SolarNoonHour,
    double SeasonFraction,
    double VernalPhase,
    bool IsDay,
    double BaseAmbientTempCelsius,
    double SurfaceGravityVsEarth = 1.0,
    /// <summary>
    /// Tidal-cycle phase of the primary moon [0..1].
    /// 0 = new moon (moon between planet and star), 0.5 = full moon.
    /// <c>null</c> if the planet has no moon or the astronomical logic is not configured.
    /// </summary>
    double? TidalPhase = null);
