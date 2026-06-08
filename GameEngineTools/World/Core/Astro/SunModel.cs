// SunModel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro
{
    using System;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Model of the Sun's motion — computes position, sunrise/sunset, twilights and irradiance
    /// pro libovolnou planetu definovanou <see cref="SunParams"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All calculations are scaled to the length of the year and day in the game calendar
    /// (<see cref="WWorld.Spec"/>), so they work for any world — not just Earth.
    /// </para>
    /// <para>
    /// <b>Ambient design.</b> The class does not require a <c>WorldTimeContext</c> in its constructor —
    /// internally it accesses <see cref="WWorld.Spec"/> directly, which must be configured
    /// before the first call to any method.
    /// </para>
    /// <para>
    /// Register as a singleton via DI or create directly:
    /// <code>
    /// services.AddSingleton&lt;SunModel&gt;();
    /// // nebo
    /// var sun = new SunModel();
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class SunModel
    {
        #region Konstanty

        private const double TwoPi = Math.PI * 2.0;

        #endregion Konstanty

        #region Konstrukce

        /// <summary>
        /// Inicializuje model slunce.
        /// Requires <see cref="WWorld"/> to be configured before the first method call.
        /// </summary>
        public SunModel()
        { }

        #endregion Konstrukce

        #region Veřejné metody — ozáření

        /// <summary>
        /// Returns the relative irradiance (0..∞) ~ cos(zenith) / r² for the current location and instant.
        /// </summary>
        /// <param name="instant">The instant to compute for.</param>
        /// <param name="latitudeDeg">Latitude in degrees (−90..90).</param>
        /// <param name="longitudeDeg">Longitude in degrees (−180..180).</param>
        /// <param name="p">Star parameters (eccentricity, axial tilt, phase…).</param>
        /// <param name="vernalPhase">
        /// Vernal-equinox phase as a fraction of the year (default <c>0.0</c>).
        /// The offset lets you choose which part of the year spring falls in.
        /// </param>
        /// <returns>
        /// Dimensionless irradiance factor. <c>0</c> = night or polar night,
        /// <c>1</c> = perpendicular radiation at normal distance, <c>&gt;1</c> = direct radiation at perihelion.
        /// </returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public double IrradianceFactor(
            WDateTime instant,
            double latitudeDeg,
            double longitudeDeg,
            in SunParams p,
            double vernalPhase = 0.0)
        {
            var (_, alt, _) = SolarPosition(instant, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var (_, _, _, rAu) = SolarDeclinationEOT(instant, in p, vernalPhase);

            double mu = Math.Max(0.0, Math.Sin(DegToRad(alt))); // ~ cos(zenit)
            double invR2 = 1.0 / (rAu * rAu);
            return mu * invR2;
        }

        #endregion Veřejné metody — ozáření

        #region Veřejné metody — sluneční den

        /// <summary>
        /// Returns solar noon, sunrise, sunset and day length (in world-day hours)
        /// for the given date, geographic location and star parameters.
        /// </summary>
        /// <param name="date">The date to compute for (the time within the day is ignored).</param>
        /// <param name="latitudeDeg">Latitude in degrees.</param>
        /// <param name="longitudeDeg">Longitude in degrees.</param>
        /// <param name="p">Star parameters.</param>
        /// <param name="vernalPhase">Vernal-equinox phase (default <c>0.0</c>).</param>
        /// <returns>
        /// Tuple (solarNoonHour, sunriseHour, sunsetHour, daylightHours) —
        /// hodiny jsou v rozsahu 0..HoursPerDay.
        /// <c>sunriseHour</c> and <c>sunsetHour</c> are <c>double.NaN</c> during polar night.
        /// </returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public (double solarNoonHour, double sunriseHour, double sunsetHour, double daylightHours)
            SolarDay(
                WDateTime date,
                double latitudeDeg,
                double longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var spec = WWorld.Spec;
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(date, in p, vernalPhase);

            var H0 = HourAngleForAltitude(latitudeDeg, delta, p.H0DegSunrise,
                out bool polarDay, out bool polarNight);

            double noon = spec.HoursPerDay * 0.5
                        - LongitudeHours(longitudeDeg, spec.HoursPerDay)
                        - eotFrac * spec.HoursPerDay;

            if (polarNight)
                return (WrapHours(noon, spec.HoursPerDay), double.NaN, double.NaN, 0.0);

            if (polarDay)
                return (WrapHours(noon, spec.HoursPerDay), 0.0, spec.HoursPerDay, spec.HoursPerDay);

            double H0h = AngleToHours(H0, spec.HoursPerDay);
            double sunrise = WrapHours(noon - H0h, spec.HoursPerDay);
            double sunset = WrapHours(noon + H0h, spec.HoursPerDay);
            double daylight = 2 * H0h;

            return (noon, sunrise, sunset, daylight);
        }

        #endregion Veřejné metody — sluneční den

        #region Veřejné metody — sluneční pozice

        /// <summary>
        /// Returns the Sun's azimuth, altitude and declination for a specific instant and location.
        /// All values are in degrees (°).
        /// </summary>
        /// <param name="instant">The instant to compute for.</param>
        /// <param name="latitudeDeg">Latitude in degrees.</param>
        /// <param name="longitudeDeg">Longitude in degrees.</param>
        /// <param name="p">Star parameters.</param>
        /// <param name="vernalPhase">Vernal-equinox phase (default <c>0.0</c>).</param>
        /// <returns>
        /// Tuple (azimuthDeg, altitudeDeg, declinationDeg).
        /// Azimuth: 0° = north, 90° = east, 180° = south, 270° = west.
        /// Altitude: negative = below the horizon.
        /// </returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public (double azimuthDeg, double altitudeDeg, double declinationDeg)
            SolarPosition(
                WDateTime instant,
                double latitudeDeg,
                double longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var spec = WWorld.Spec;
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(instant, in p, vernalPhase);

            double fracDay = FractionOfDay(instant.WorldTicks, spec);
            double lstHours = fracDay * spec.HoursPerDay
                            + LongitudeHours(longitudeDeg, spec.HoursPerDay)
                            + eotFrac * spec.HoursPerDay;

            double H = HoursToAngle(lstHours - spec.HoursPerDay * 0.5, spec.HoursPerDay);
            double phi = DegToRad(latitudeDeg);
            double sinAlt = Math.Sin(DegToRad(delta)) * Math.Sin(phi)
                          + Math.Cos(DegToRad(delta)) * Math.Cos(phi) * Math.Cos(H);
            double alt = RadToDeg(Math.Asin(sinAlt));

            double cosAz = (Math.Sin(DegToRad(delta)) - Math.Sin(phi) * sinAlt)
                          / (Math.Cos(phi) * Math.Cos(DegToRad(alt)));
            cosAz = Math.Clamp(cosAz, -1, 1);
            double az = RadToDeg(Math.Acos(cosAz));
            if (Math.Sin(H) > 0) az = 360 - az;

            return (az, alt, delta);
        }

        #endregion Veřejné metody — sluneční pozice

        #region Veřejné metody — soumraky

        /// <summary>
        /// Returns dawn and dusk times (civil, nautical, astronomical) for the given date and location.
        /// </summary>
        /// <param name="date">The date to compute for.</param>
        /// <param name="latitudeDeg">Latitude in degrees.</param>
        /// <param name="longitudeDeg">Longitude in degrees.</param>
        /// <param name="p">Star parameters.</param>
        /// <param name="vernalPhase">Vernal-equinox phase (default <c>0.0</c>).</param>
        /// <returns>
        /// A tuple of times (world-day hours, 0..HoursPerDay):
        /// <c>civilDawn</c>, <c>sunrise</c>, <c>solarNoon</c>, <c>sunset</c>, <c>civilDusk</c>,
        /// <c>nauticalDawn</c>, <c>nauticalDusk</c>, <c>astroDawn</c>, <c>astroDusk</c>.
        /// A value of <c>double.NaN</c> = polar night or polar day (the given threshold is never reached).
        /// </returns>
        /// <exception cref="InvalidOperationException">If WWorld is not configured.</exception>
        public (double civilDawn, double sunrise, double solarNoon, double sunset, double civilDusk,
                double nauticalDawn, double nauticalDusk,
                double astroDawn, double astroDusk)
            Twilights(
                WDateTime date,
                double latitudeDeg,
                double longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var spec = WWorld.Spec;
            var (noon, sunrise, sunset, _) = SolarDay(date, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(date, in p, vernalPhase);

            // Local helper function — computes dawn/dusk for an arbitrary altitude threshold
            (double dawn, double dusk) Edge(double h0)
            {
                var H0 = HourAngleForAltitude(latitudeDeg, delta, h0, out bool pd, out bool pn);
                if (pn) return (double.NaN, double.NaN);
                if (pd) return (0.0, spec.HoursPerDay);
                double H0h = AngleToHours(H0, spec.HoursPerDay);
                return (WrapHours(noon - H0h, spec.HoursPerDay),
                        WrapHours(noon + H0h, spec.HoursPerDay));
            }

            var (civilDawn, civilDusk) = Edge(-p.TwilightCivilDeg);
            var (nautDawn, nautDusk) = Edge(-p.TwilightNauticalDeg);
            var (astroDawn, astroDusk) = Edge(-p.TwilightAstronomicalDeg);

            return (civilDawn, sunrise, noon, sunset, civilDusk,
                    nautDawn, nautDusk,
                    astroDawn, astroDusk);
        }

        #endregion Veřejné metody — soumraky

        #region Privátní výpočetní metody

        /// <summary>
        /// Returns declination (°), the equation of time (fraction of a day), ecliptic longitude (°)
        /// and the relative distance from the star (AU) for the given instant.
        /// </summary>
        /// <param name="t">The instant to compute for.</param>
        /// <param name="p">Star parameters.</param>
        /// <param name="vernalPhase">Vernal-equinox phase as a fraction of the year.</param>
        private static (double deltaDeg, double eotFracOfDay, double lambdaDeg, double rAu)
            SolarDeclinationEOT(WDateTime t, in SunParams p, double vernalPhase)
        {
            var spec = WWorld.Spec;
            long dayIdx = t.WorldTicks / spec.TicksPerDay;
            var (y, _, _) = spec.Calendar.DateFromDays(dayIdx);
            long doy = dayIdx - spec.Calendar.DaysFromDate(y, 1, 1);
            double Y = spec.Calendar.DaysInYear(y);

            double phase = (doy / Y + vernalPhase) * TwoPi;
            double M = (doy / Y + p.PeriapsisPhase) * TwoPi;

            // Equation of the centre — elliptical correction to the true position
            double C = 2 * p.Eccentricity * Math.Sin(M)
                          + 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);
            double lambda = WrapAngle(phase + C);

            // Declination from axial tilt and ecliptic longitude
            double eps = DegToRad(p.AxialTiltDeg);
            double sinDelta = Math.Sin(eps) * Math.Sin(lambda);
            double delta = RadToDeg(Math.Asin(sinDelta));

            // Equation of time (Spencer/Fourier approximation)
            double yv = Math.Tan(eps / 2.0); yv *= yv;
            double L0 = phase;
            double eotRad =
                 yv * Math.Sin(2 * L0)
               - 2 * p.Eccentricity * Math.Sin(M)
               + 4 * p.Eccentricity * yv * Math.Sin(M) * Math.Cos(2 * L0)
               - 0.5 * yv * yv * Math.Sin(4 * L0)
               - 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);

            double eotFrac = eotRad / TwoPi;
            double r = 1 - p.Eccentricity * Math.Cos(M); // relativní vzdálenost [AU]

            return (delta, eotFrac, RadToDeg(lambda), r);
        }

        /// <summary>
        /// Returns the fraction of the current day (0..1) from world ticks.
        /// </summary>
        private static double FractionOfDay(long worldTicks, WorldTimeSpec spec)
        {
            long dayTicks = worldTicks % spec.TicksPerDay;
            if (dayTicks < 0) dayTicks += spec.TicksPerDay;
            return (double)dayTicks / spec.TicksPerDay;
        }

        /// <summary>
        /// Converts an hour angle (world-day hours) into radians.
        /// </summary>
        private static double HoursToAngle(double hours, int hoursPerDay)
            => hours / hoursPerDay * TwoPi;

        /// <summary>
        /// Converts an hour angle in radians into world-day hours.
        /// </summary>
        private static double AngleToHours(double angleRad, int hoursPerDay)
            => angleRad / TwoPi * hoursPerDay;

        /// <summary>
        /// Converts longitude into an offset in world-day hours.
        /// </summary>
        private static double LongitudeHours(double longitudeDeg, int hoursPerDay)
            => longitudeDeg / 360.0 * hoursPerDay;

        /// <summary>
        /// Wraps hours into the range [0, HoursPerDay) — wraparound across midnight.
        /// </summary>
        private static double WrapHours(double h, int hoursPerDay)
        {
            h %= hoursPerDay;
            if (h < 0) h += hoursPerDay;
            return h;
        }

        /// <summary>
        /// Computes the hour angle H0 for a given threshold of the Sun's altitude above the horizon.
        /// </summary>
        /// <param name="latDeg">Latitude in degrees.</param>
        /// <param name="declDeg">The Sun's declination in degrees.</param>
        /// <param name="h0Deg">The Sun's altitude threshold in degrees (negative = below the horizon).</param>
        /// <param name="polarDay"><c>true</c> if the Sun does not set on the given day.</param>
        /// <param name="polarNight"><c>true</c> if the Sun does not rise on the given day.</param>
        /// <returns>The hour angle in radians, or <c>0</c> during polar day/night.</returns>
        private static double HourAngleForAltitude(
            double latDeg, double declDeg, double h0Deg,
            out bool polarDay, out bool polarNight)
        {
            double phi = DegToRad(latDeg);
            double delta = DegToRad(declDeg);
            double h0 = DegToRad(h0Deg);
            double cosH0 = (Math.Sin(h0) - Math.Sin(phi) * Math.Sin(delta))
                         / (Math.Cos(phi) * Math.Cos(delta));

            if (cosH0 < -1.0) { polarDay = true; polarNight = false; return 0; }
            if (cosH0 > 1.0) { polarDay = false; polarNight = true; return 0; }

            polarDay = polarNight = false;
            return Math.Acos(Math.Clamp(cosH0, -1, 1));
        }

        /// <summary>Wraps an angle in radians into the range [0, 2π).</summary>
        private static double WrapAngle(double a)
        {
            a %= TwoPi;
            if (a < 0) a += TwoPi;
            return a;
        }

        /// <summary>Converts degrees to radians.</summary>
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Converts radians to degrees.</summary>
        private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

        #endregion Privátní výpočetní metody
    }
}
