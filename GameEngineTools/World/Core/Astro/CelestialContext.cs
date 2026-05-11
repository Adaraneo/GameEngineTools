// CelestialContext.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

/// <summary>
/// Snapshot astronomických podmínek pro jeden simulační tick.
/// Vypočítán <see cref="CelestialContextComputer"/> jednou za tick a předán každé
/// postavě přes <see cref="GameEngineTools.Characters.Core.EnginesSnapshot.Celestial"/>.
/// </summary>
/// <param name="IrradianceFactor">
/// Relativní ozáření z <see cref="SunModel.IrradianceFactor"/>. 0 v noci nebo polární noci,
/// ≈ 1 při kolmém záření ve střední vzdálenosti, &gt; 1 v perihéliu.
/// </param>
/// <param name="DaylightHours">Délka dne v herních hodinách (0..HoursPerDay).</param>
/// <param name="SunriseHour">Hodina východu Slunce. <see cref="double.NaN"/> při polární noci.</param>
/// <param name="SunsetHour">Hodina západu Slunce. <see cref="double.NaN"/> při polární noci.</param>
/// <param name="SolarNoonHour">Hodina slunečního poledne.</param>
/// <param name="SeasonFraction">
/// Fáze roku: 0.0 = jarní rovnodennost, 0.25 = letní slunovrat,
/// 0.5 = podzimní rovnodennost, 0.75 = zimní slunovrat.
/// </param>
/// <param name="VernalPhase">Konfigurovovaná fáze jarní rovnodennosti (průchod z <see cref="AstroConfig"/>).</param>
/// <param name="IsDay"><c>true</c> pokud je <paramref name="IrradianceFactor"/> &gt; 0.</param>
/// <param name="BaseAmbientTempCelsius">
/// Sezónní teplota prostředí (°C): <c>baseTempC + amplitudeC × sin(SeasonFraction × 2π − π/2)</c>.
/// Maximum v létě (0.25), minimum v zimě (0.75).
/// </param>
/// <param name="SurfaceGravityVsEarth">
/// Povrchová gravitace planety jako násobek zemské (fáze 2 — z <c>PlanetConfig</c>).
/// Výchozí 1.0 (fáze 1).
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
    /// Fáze slapového cyklu primárního měsíce [0..1].
    /// 0 = nov (měsíc mezi planetou a hvězdou), 0.5 = úplněk.
    /// <c>null</c> pokud planeta nemá měsíc nebo astronomická logika není nakonfigurována.
    /// </summary>
    double? TidalPhase = null);
