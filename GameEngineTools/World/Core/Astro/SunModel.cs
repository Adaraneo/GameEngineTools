// SunModel.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro
{
    using System;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Model pohybu Slunce — výpočet pozice, východu/západu, soumraků a ozáření
    /// pro libovolnou planetu definovanou <see cref="SunParams"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Všechny výpočty jsou škálované na délku roku a dne v herním kalendáři
    /// (<see cref="WorldTimeSpec"/>), takže fungují pro libovolný svět — nejen Zemi.
    /// </para>
    /// <para>
    /// Registruj jako singleton přes DI:
    /// <code>
    /// services.AddSingleton&lt;SunModel&gt;();
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class SunModel
    {
        #region Konstanty

        private const double TwoPi = Math.PI * 2.0;

        #endregion

        #region Soukromá pole

        private readonly WorldTimeContext _ctx;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje model slunce s kontextem světového času.
        /// </summary>
        /// <param name="ctx">
        /// Kontext potřebný pro délku dne (<see cref="WorldTimeSpec.HoursPerDay"/>),
        /// délku roku (kalendář) a dekompozici <see cref="WDateTime"/> na složky.
        /// Nahrazuje odstraněný globální <c>WDateTime.Spec</c>.
        /// </param>
        public SunModel(WorldTimeContext ctx) => _ctx = ctx;

        #endregion

        #region Veřejné metody

        /// <summary>
        /// Vrátí relativní ozáření (0..∞) ~ cos(zenit) / r² pro aktuální polohu a okamžik.
        /// </summary>
        /// <param name="instant">Okamžik výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy (excentricita, sklon osy, fáze…).</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti jako zlomek roku (výchozí 0.0).</param>
        /// <returns>Bezrozměrný faktor ozáření (0 = noc, &gt;1 = přímé záření).</returns>
        public double IrradianceFactor(
            WDateTime instant,
            double    latitudeDeg,
            double    longitudeDeg,
            in SunParams p,
            double vernalPhase = 0.0)
        {
            var (_, alt, _)    = SolarPosition(instant, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var (_, _, _, rAu) = SolarDeclinationEOT(instant, in p, vernalPhase);

            double mu    = Math.Max(0.0, Math.Sin(DegToRad(alt))); // ~ cos(zenit)
            double invR2 = 1.0 / (rAu * rAu);
            return mu * invR2;
        }

        /// <summary>
        /// Vrátí sluneční poledne, východ, západ a délku dne (v hodinách světového dne)
        /// pro dané datum, zeměpisnou polohu a parametry hvězdy.
        /// </summary>
        /// <param name="date">Datum (čas se ignoruje).</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí 0.0).</param>
        /// <returns>
        /// Tuple (solarNoonHour, sunriseHour, sunsetHour, daylightHours) —
        /// hodiny jsou v rozsahu 0..HoursPerDay. <c>double.NaN</c> pro polární noc.
        /// </returns>
        public (double solarNoonHour, double sunriseHour, double sunsetHour, double daylightHours)
            SolarDay(
                WDateTime date,
                double    latitudeDeg,
                double    longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var spec = _ctx.Spec;
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(date, in p, vernalPhase);

            var H0 = HourAngleForAltitude(latitudeDeg, delta, p.H0DegSunrise,
                out bool polarDay, out bool polarNight);

            double noon = spec.HoursPerDay * 0.5
                        - LongitudeHours(longitudeDeg)
                        - eotFrac * spec.HoursPerDay;

            if (polarNight)
                return (WrapHours(noon), double.NaN, double.NaN, 0.0);
            if (polarDay)
                return (WrapHours(noon), 0.0, spec.HoursPerDay, spec.HoursPerDay);

            double H0h     = AngleToHours(H0);
            double sunrise = WrapHours(noon - H0h);
            double sunset  = WrapHours(noon + H0h);
            double daylight = 2 * H0h;

            return (noon, sunrise, sunset, daylight);
        }

        /// <summary>
        /// Vrátí azimut, výšku a deklinaci Slunce pro konkrétní okamžik a polohu (ve stupních).
        /// </summary>
        /// <param name="instant">Okamžik výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí 0.0).</param>
        /// <returns>Tuple (azimuthDeg, altitudeDeg, declinationDeg).</returns>
        public (double azimuthDeg, double altitudeDeg, double declinationDeg)
            SolarPosition(
                WDateTime instant,
                double    latitudeDeg,
                double    longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var spec = _ctx.Spec;

            var (delta, eotFrac, _, _) = SolarDeclinationEOT(instant, in p, vernalPhase);

            double fracDay  = FractionOfDay(instant.WorldTicks);
            double lstHours = fracDay * spec.HoursPerDay
                            + LongitudeHours(longitudeDeg)
                            + eotFrac * spec.HoursPerDay;

            double H      = HoursToAngle(lstHours - spec.HoursPerDay * 0.5);
            double phi    = DegToRad(latitudeDeg);
            double sinAlt = Math.Sin(DegToRad(delta)) * Math.Sin(phi)
                          + Math.Cos(DegToRad(delta)) * Math.Cos(phi) * Math.Cos(H);
            double alt    = RadToDeg(Math.Asin(sinAlt));

            double cosAz = (Math.Sin(DegToRad(delta)) - Math.Sin(phi) * sinAlt)
                         / (Math.Cos(phi) * Math.Cos(DegToRad(alt)));
            cosAz    = Math.Clamp(cosAz, -1, 1);
            double az = RadToDeg(Math.Acos(cosAz));
            if (Math.Sin(H) > 0) az = 360 - az;

            return (az, alt, delta);
        }

        /// <summary>
        /// Vrátí časy úsvitu/soumraku (civil, nautical, astronomical) pro dané datum a polohu.
        /// </summary>
        /// <param name="date">Datum výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí 0.0).</param>
        /// <returns>
        /// Tuple s časy (hodiny světového dne): civilní úsvit/soumrak, nautický, astronomický,
        /// plus východ/západ Slunce a sluneční poledne.
        /// </returns>
        public (double civilDawn,    double sunrise,     double solarNoon, double sunset,     double civilDusk,
                double nauticalDawn, double nauticalDusk,
                double astroDawn,    double astroDusk)
            Twilights(
                WDateTime date,
                double    latitudeDeg,
                double    longitudeDeg,
                in SunParams p,
                double vernalPhase = 0.0)
        {
            var (noon, sunrise, sunset, _)    = SolarDay(date, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var (delta, eotFrac, _, _)         = SolarDeclinationEOT(date, in p, vernalPhase);
            var spec                           = _ctx.Spec;

            // Pomocná funkce — výpočet úsvitu/soumraku pro libovolný prah výšky
            (double dawn, double dusk) Edge(double h0)
            {
                var H0 = HourAngleForAltitude(latitudeDeg, delta, h0, out bool pd, out bool pn);
                if (pn) return (double.NaN, double.NaN);
                if (pd) return (0.0, spec.HoursPerDay);
                double H0h   = AngleToHours(H0);
                return (WrapHours(noon - H0h), WrapHours(noon + H0h));
            }

            var (civilDawn,  civilDusk)  = Edge(-p.TwilightCivilDeg);
            var (nautDawn,   nautDusk)   = Edge(-p.TwilightNauticalDeg);
            var (astroDawn,  astroDusk)  = Edge(-p.TwilightAstronomicalDeg);

            return (civilDawn, sunrise, noon, sunset, civilDusk,
                    nautDawn,  nautDusk,
                    astroDawn, astroDusk);
        }

        #endregion

        #region Privátní výpočetní metody

        /// <summary>
        /// Vrátí deklinaci, rovnici času (jako frakci dne), ekliptickou délku a relativní vzdálenost
        /// pro daný okamžik a parametry hvězdy.
        /// </summary>
        private (double deltaDeg, double eotFracOfDay, double lambdaDeg, double rAu)
            SolarDeclinationEOT(WDateTime t, in SunParams p, double vernalPhase)
        {
            var spec     = _ctx.Spec;
            var dayIndex = t.WorldTicks / spec.TicksPerDay;
            var (y, _, _) = spec.Calendar.DateFromDays(dayIndex);
            long doy     = dayIndex - spec.Calendar.DaysFromDate(y, 1, 1);
            double Y     = spec.Calendar.DaysInYear(y);

            double phase  = (doy / Y + vernalPhase)      * TwoPi;
            double M      = (doy / Y + p.PeriapsisPhase) * TwoPi;
            double C      = 2 * p.Eccentricity * Math.Sin(M)
                          + 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);
            double lambda = WrapAngle(phase + C);

            double eps      = DegToRad(p.AxialTiltDeg);
            double sinDelta = Math.Sin(eps) * Math.Sin(lambda);
            double delta    = RadToDeg(Math.Asin(sinDelta));

            double yv = Math.Tan(eps / 2.0); yv *= yv;
            double L0 = phase;
            double EoT_rad =
                 yv * Math.Sin(2 * L0)
               - 2 * p.Eccentricity * Math.Sin(M)
               + 4 * p.Eccentricity * yv * Math.Sin(M) * Math.Cos(2 * L0)
               - 0.5 * yv * yv * Math.Sin(4 * L0)
               - 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);

            double eotFrac = EoT_rad / TwoPi;
            double r       = 1 - p.Eccentricity * Math.Cos(M);

            return (delta, eotFrac, RadToDeg(lambda), r);
        }

        /// <summary>Vrátí frakci aktuálního dne (0..1) z worldTicků.</summary>
        private double FractionOfDay(long worldTicks)
        {
            var spec     = _ctx.Spec;
            long dayTicks = worldTicks % spec.TicksPerDay;
            if (dayTicks < 0) dayTicks += spec.TicksPerDay;
            return (double)dayTicks / spec.TicksPerDay;
        }

        /// <summary>Převede hodinový úhel (hodiny) na radiány.</summary>
        private double HoursToAngle(double hours)
            => hours / _ctx.Spec.HoursPerDay * TwoPi;

        /// <summary>Převede radiány hodinového úhlu na hodiny světového dne.</summary>
        private double AngleToHours(double angleRad)
            => angleRad / TwoPi * _ctx.Spec.HoursPerDay;

        /// <summary>Převede zeměpisnou délku na posun v hodinách světového dne.</summary>
        private double LongitudeHours(double longitudeDeg)
            => longitudeDeg / 360.0 * _ctx.Spec.HoursPerDay;

        /// <summary>Zabalí hodiny do rozsahu 0..HoursPerDay (wraparound).</summary>
        private double WrapHours(double h)
        {
            double d = _ctx.Spec.HoursPerDay;
            h %= d; if (h < 0) h += d;
            return h;
        }

        /// <summary>Vypočítá hodinový úhel H0 pro daný prah výšky Slunce nad obzorem.</summary>
        /// <param name="polarDay"><c>true</c> pokud Slunce nezapadá.</param>
        /// <param name="polarNight"><c>true</c> pokud Slunce nevychází.</param>
        private static double HourAngleForAltitude(
            double latDeg, double declDeg, double h0Deg,
            out bool polarDay, out bool polarNight)
        {
            double phi    = DegToRad(latDeg);
            double delta  = DegToRad(declDeg);
            double h0     = DegToRad(h0Deg);
            double cosH0  = (Math.Sin(h0) - Math.Sin(phi) * Math.Sin(delta))
                          / (Math.Cos(phi) * Math.Cos(delta));

            if (cosH0 < -1.0) { polarDay   = true;  polarNight = false; return 0; }
            if (cosH0 >  1.0) { polarDay   = false; polarNight = true;  return 0; }

            polarDay = polarNight = false;
            return Math.Acos(Math.Clamp(cosH0, -1, 1));
        }

        private static double WrapAngle(double a) { a %= TwoPi; if (a < 0) a += TwoPi; return a; }
        private static double DegToRad(double x) => x * Math.PI / 180.0;
        private static double RadToDeg(double x) => x * 180.0 / Math.PI;

        #endregion
    }
}
