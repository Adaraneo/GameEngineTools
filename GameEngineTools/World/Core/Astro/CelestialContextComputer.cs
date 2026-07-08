// CelestialContextComputer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

using GameEngineTools.Universe;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;

/// <summary>
/// Computes the <see cref="CelestialContext"/> for a given instant from <see cref="SunModel"/>
/// and <see cref="AstroConfig"/>. Register as a singleton or create directly.
/// </summary>
internal sealed class CelestialContextComputer
{
    private readonly SunModel _sunModel;

    /// <param name="sunModel">Solar-model instance (singleton).</param>
    internal CelestialContextComputer(SunModel sunModel)
        => _sunModel = sunModel;

    /// <summary>
    /// Computes the astronomical context for a given game instant per the configuration.
    /// </summary>
    /// <param name="now">Current game time.</param>
    /// <param name="cfg">The world's astronomical configuration.</param>
    internal CelestialContext Compute(WDateTime now, AstroConfig cfg)
    {
        var sp = (cfg.Sun ?? new SunParamsConfig()).ToSunParams();
        var spec = WWorld.Spec;

        var (solarNoon, sunrise, sunset, daylight) = _sunModel.SolarDay(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        var irradiance = _sunModel.IrradianceFactor(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        // SeasonFraction from day-of-year / length of the year
        var dayIdx = now.WorldTicks / spec.TicksPerDay;
        var (year, _, _) = spec.Calendar.DateFromDays(dayIdx);
        var yearStart = spec.Calendar.DaysFromDate(year, 1, 1);
        var yearLen = spec.Calendar.DaysInYear(year);
        var seasonFrac = ((dayIdx - yearStart) / (double)yearLen + cfg.VernalPhase) % 1.0;
        if (seasonFrac < 0) seasonFrac += 1.0;   // guard pro záporný výsledek

        // Temperature: max in summer (0.25), min in winter (0.75)
        var tempC = cfg.BaseTemperatureCelsius
                  + cfg.SeasonalAmplitudeCelsius * Math.Sin(seasonFrac * 2.0 * Math.PI - Math.PI / 2.0);

        return new CelestialContext(
            IrradianceFactor: irradiance,
            DaylightHours: daylight,
            SunriseHour: sunrise,
            SunsetHour: sunset,
            SolarNoonHour: solarNoon,
            SeasonFraction: seasonFrac,
            VernalPhase: cfg.VernalPhase,
            IsDay: irradiance > 0.0,
            BaseAmbientTempCelsius: tempC);
    }

    /// <summary>
    /// Phase 2 overload — SeasonFraction from Kepler's equation, temperature from the physical model,
    /// gravitace z <see cref="PlanetConfig.SurfaceGravityVsEarth"/>.
    /// </summary>
    internal CelestialContext Compute(
        WDateTime now,
        AstroConfig cfg,
        StarPhysics star,
        OrbitalElements orbit,
        PlanetConfig planet)
    {
        // SunParams: ObliquityDeg z planety, refrakce/twilight z cfg.Sun
        var sunCfg = cfg.Sun ?? new SunParamsConfig();
        var sp = new SunParams(
            planet.ObliquityDeg,
            orbit.Eccentricity,
            sunCfg.PeriapsisPhase,
            sunCfg.RefractionDeg,
            sunCfg.ApparentRadiusDeg,
            sunCfg.TwilightCivilDeg,
            sunCfg.TwilightNauticalDeg,
            sunCfg.TwilightAstronomicalDeg);

        var spec = WWorld.Spec;

        var (solarNoon, sunrise, sunset, daylight) = _sunModel.SolarDay(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);
        var irradiance = _sunModel.IrradianceFactor(
            now, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        // SeasonFraction anchored to the axial tilt (day-of-year) — the SAME clock that drives
        // irradiance and day length. 0.0 = vernal equinox, 0.25 = summer solstice (N hemisphere).
        // (The earlier Kepler true-anomaly season was anchored to periapsis, a different orbital
        // reference than the tilt, so temperature and light could drift out of phase.)
        var dayIdx = now.WorldTicks / spec.TicksPerDay;
        var (year, _, _) = spec.Calendar.DateFromDays(dayIdx);
        var yearStart = spec.Calendar.DaysFromDate(year, 1, 1);
        var yearLen = spec.Calendar.DaysInYear(year);
        var seasonFrac = ((dayIdx - yearStart) / (double)yearLen + cfg.VernalPhase) % 1.0;
        if (seasonFrac < 0) seasonFrac += 1.0;

        // Distance from the star this instant (Kepler) → the genuine eccentric distance effect
        // on the mean temperature (closer to periapsis = warmer). This is the minor real
        // contribution of eccentricity, kept physical.
        var secondsPerWorldDay = (double)spec.HoursPerDay * spec.MinutesPerHour * spec.SecondsPerMinute;
        var tSinceEpochEarthDays = dayIdx * secondsPerWorldDay / 86_400.0;
        var (kx, ky) = KeplerSolver.OrbitalPositionAu(orbit, tSinceEpochEarthDays, star.GravitationalParameter);
        var orbitAu = Math.Sqrt(kx * kx + ky * ky);
        var meanTempC = star.EquilibriumTempK(orbitAu, planet.Albedo) + planet.GreenhouseWarmingK - 273.15;

        // Seasonal temperature offset from the axial tilt — driven by the SAME solar declination
        // as the light, so it is phase-locked to it and hemisphere-correct. A thermal lag models
        // the slow warming of ocean/soil: temperature trails insolation by ~1 month on Earth.
        var tempC = meanTempC + SeasonalTemperatureOffset(now, in sp, cfg, planet, spec, yearLen);

        // ── Primary moon — tidal phase ───────────────────────────────────────────
        double? tidalPhase = null;
        if (planet.PrimaryMoon is { } moon)
        {
            var moonPeriodSec = moon.Orbit.OrbitalPeriodSeconds(planet.GravitationalParameter);
            var elapsedSec = dayIdx * secondsPerWorldDay;
            tidalPhase = elapsedSec % moonPeriodSec / moonPeriodSec;   // 0..1
        }

        // ── Ring system — shadow belt + aurora ───────────────────────────────────
        if (planet.Rings is { } rings)
        {
            var materialBands = rings.Bands.Where(b => !b.IsGap).ToList();
            if (materialBands.Count > 0)
            {
                // The shadow belt blocks part of the direct radiation
                var maxOpticalDepth = materialBands.Max(b => b.MeanOpticalDepth);
                var shadowLat = RingSystem.ShadowBeltLatitudeDeg(planet.ObliquityDeg, seasonFrac);
                var shadowFrac = RingSystem.ShadowFraction(cfg.LatitudeDeg, shadowLat, maxOpticalDepth);
                irradiance *= (1.0 - shadowFrac);

                // Aurora at night — the rings reflect starlight toward the poles
                if (irradiance <= 0.0 && Math.Abs(cfg.LatitudeDeg) > 60)
                {
                    var starFlux = star.IrradianceAtAu(orbitAu);
                    var refFlux = star.IrradianceAtAu(1.0);           // normalizační reference
                    var avgAlbedo = materialBands.Average(b => b.AlbedoGeometric);
                    var ringGlowW = rings.ApproximatePolarRingGlow(starFlux, avgAlbedo);
                    irradiance = refFlux > 0.0 ? ringGlowW / refFlux : 0.0;
                }
            }
        }

        return new CelestialContext(
            IrradianceFactor: irradiance,
            DaylightHours: daylight,
            SunriseHour: sunrise,
            SunsetHour: sunset,
            SolarNoonHour: solarNoon,
            SeasonFraction: seasonFrac,
            VernalPhase: cfg.VernalPhase,
            IsDay: irradiance > 0.0,
            BaseAmbientTempCelsius: tempC,
            SurfaceGravityVsEarth: planet.SurfaceGravityVsEarth,
            TidalPhase: tidalPhase);
    }

    /// <summary>
    /// Seasonal temperature offset (°C) driven by the solar declination — the same quantity that
    /// governs day length and irradiance — so it is phase-locked to the light and hemisphere-correct.
    /// The declination is sampled at an earlier instant (<see cref="AstroConfig.SeasonalThermalLagFraction"/>)
    /// to model the thermal inertia of ocean and soil: the hottest day trails the summer solstice.
    /// Returns <c>0</c> for a planet with no axial tilt (no seasons).
    /// </summary>
    private double SeasonalTemperatureOffset(
        WDateTime now,
        in SunParams sp,
        AstroConfig cfg,
        PlanetConfig planet,
        WorldTimeSpec spec,
        long yearLen)
    {
        if (planet.ObliquityDeg <= 0.01)
            return 0.0;   // no tilt → no seasons

        // Sample declination in the past by the thermal lag. Declination is annual-periodic, so we
        // wrap by a whole year if the shift would run before the epoch (guards early-simulation ticks).
        var yearTicks = yearLen * spec.TicksPerDay;
        var lagTicks  = (long)(cfg.SeasonalThermalLagFraction * yearTicks);
        var laggedTicks = now.WorldTicks - lagTicks;
        if (laggedTicks < 0) laggedTicks += yearTicks;

        var laggedTime = new WDateTime(laggedTicks);
        var (_, _, declDeg) = _sunModel.SolarPosition(
            laggedTime, cfg.LatitudeDeg, cfg.LongitudeDeg, in sp, cfg.VernalPhase);

        // δ/ε ∈ [−1, +1], peaking at the solstice; the latitude sign makes summer land in the
        // correct hemisphere (southern latitudes are warm when the northern sub-solar point is negative).
        var seasonalUnit = declDeg / planet.ObliquityDeg * Math.Sign(cfg.LatitudeDeg);
        return cfg.SeasonalAmplitudeCelsius * seasonalUnit;
    }
}
