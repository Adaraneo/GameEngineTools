// CelestialContextComputer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;

/// <summary>
/// Vypočítá <see cref="CelestialContext"/> pro daný okamžik ze <see cref="SunModel"/>
/// a <see cref="AstroConfig"/>. Registruj jako singleton nebo vytvoř přímo.
/// </summary>
internal sealed class CelestialContextComputer
{
    private readonly SunModel _sunModel;

    /// <param name="sunModel">Instance slunečního modelu (singleton).</param>
    internal CelestialContextComputer(SunModel sunModel)
        => _sunModel = sunModel;

    /// <summary>
    /// Spočítá astronomický kontext pro daný herní okamžik podle konfigurace.
    /// </summary>
    /// <param name="now">Aktuální herní čas.</param>
    /// <param name="cfg">Astronomická konfigurace světa.</param>
    internal CelestialContext Compute(WDateTime now, AstroConfig cfg)
    {
        var sp = (cfg.Sun ?? new SunParamsConfig()).ToSunParams();
        var spec = WWorld.Spec;

        var (solarNoon, sunrise, sunset, daylight) = _sunModel.SolarDay(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        var irradiance = _sunModel.IrradianceFactor(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        // SeasonFraction z day-of-year / délka roku
        var dayIdx   = now.WorldTicks / spec.TicksPerDay;
        var (year, _, _) = spec.Calendar.DateFromDays(dayIdx);
        var yearStart    = spec.Calendar.DaysFromDate(year, 1, 1);
        var yearLen      = spec.Calendar.DaysInYear(year);
        var seasonFrac   = ((dayIdx - yearStart) / (double)yearLen + cfg.VernalPhase) % 1.0;
        if (seasonFrac < 0) seasonFrac += 1.0;   // guard pro záporný výsledek

        // Teplota: max v létě (0.25), min v zimě (0.75)
        var tempC = cfg.BaseTemperatureCelsius
                  + cfg.SeasonalAmplitudeCelsius * Math.Sin(seasonFrac * 2.0 * Math.PI - Math.PI / 2.0);

        return new CelestialContext(
            IrradianceFactor:       irradiance,
            DaylightHours:          daylight,
            SunriseHour:            sunrise,
            SunsetHour:             sunset,
            SolarNoonHour:          solarNoon,
            SeasonFraction:         seasonFrac,
            VernalPhase:            cfg.VernalPhase,
            IsDay:                  irradiance > 0.0,
            BaseAmbientTempCelsius: tempC);
    }
}
