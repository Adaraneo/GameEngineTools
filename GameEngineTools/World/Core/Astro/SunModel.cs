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
    /// (<see cref="WWorld.Spec"/>), takže fungují pro libovolný svět — nejen Zemi.
    /// </para>
    /// <para>
    /// <b>Ambient design.</b> Třída nevyžaduje <c>WorldTimeContext</c> v konstruktoru —
    /// interně přistupuje přímo na <see cref="WWorld.Spec"/>, které musí být nakonfigurováno
    /// před prvním voláním libovolné metody.
    /// </para>
    /// <para>
    /// Registruj jako singleton přes DI nebo vytvoř přímo:
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
        /// Vyžaduje nakonfigurovaný <see cref="WWorld"/> před prvním voláním metod.
        /// </summary>
        public SunModel()
        { }

        #endregion Konstrukce

        #region Veřejné metody — ozáření

        /// <summary>
        /// Vrátí relativní ozáření (0..∞) ~ cos(zenit) / r² pro aktuální polohu a okamžik.
        /// </summary>
        /// <param name="instant">Okamžik výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních (−90..90).</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních (−180..180).</param>
        /// <param name="p">Parametry hvězdy (excentricita, sklon osy, fáze…).</param>
        /// <param name="vernalPhase">
        /// Fáze jarní rovnodennosti jako zlomek roku (výchozí <c>0.0</c>).
        /// Posunutí umožňuje určit, ve kterou část roku připadá jaro.
        /// </param>
        /// <returns>
        /// Bezrozměrný faktor ozáření. <c>0</c> = noc nebo polární noc,
        /// <c>1</c> = kolmé záření v normální vzdálenosti, <c>&gt;1</c> = přímé záření v perihéliu.
        /// </returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Vrátí sluneční poledne, východ, západ a délku dne (v hodinách světového dne)
        /// pro dané datum, zeměpisnou polohu a parametry hvězdy.
        /// </summary>
        /// <param name="date">Datum výpočtu (čas v rámci dne je ignorován).</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí <c>0.0</c>).</param>
        /// <returns>
        /// Tuple (solarNoonHour, sunriseHour, sunsetHour, daylightHours) —
        /// hodiny jsou v rozsahu 0..HoursPerDay.
        /// <c>sunriseHour</c> a <c>sunsetHour</c> jsou <c>double.NaN</c> při polární noci.
        /// </returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Vrátí azimut, výšku a deklinaci Slunce pro konkrétní okamžik a polohu.
        /// Všechny hodnoty jsou ve stupních (°).
        /// </summary>
        /// <param name="instant">Okamžik výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí <c>0.0</c>).</param>
        /// <returns>
        /// Tuple (azimuthDeg, altitudeDeg, declinationDeg).
        /// Azimut: 0° = sever, 90° = východ, 180° = jih, 270° = západ.
        /// Výška: záporná = pod obzorem.
        /// </returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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
        /// Vrátí časy úsvitu a soumraku (civil, nautical, astronomical) pro dané datum a polohu.
        /// </summary>
        /// <param name="date">Datum výpočtu.</param>
        /// <param name="latitudeDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="longitudeDeg">Zeměpisná délka v stupních.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti (výchozí <c>0.0</c>).</param>
        /// <returns>
        /// Tuple s časy (hodiny světového dne, 0..HoursPerDay):
        /// <c>civilDawn</c>, <c>sunrise</c>, <c>solarNoon</c>, <c>sunset</c>, <c>civilDusk</c>,
        /// <c>nauticalDawn</c>, <c>nauticalDusk</c>, <c>astroDawn</c>, <c>astroDusk</c>.
        /// Hodnota <c>double.NaN</c> = polární noc nebo polární den (daný práh nenastane).
        /// </returns>
        /// <exception cref="InvalidOperationException">Pokud WWorld není nakonfigurován.</exception>
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

            // Lokální pomocná funkce — vypočítá úsvit/soumrak pro libovolný prah výšky
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
        /// Vrátí deklinaci (°), rovnici času (frakce dne), ekliptickou délku (°)
        /// a relativní vzdálenost od hvězdy (AU) pro daný okamžik.
        /// </summary>
        /// <param name="t">Okamžik výpočtu.</param>
        /// <param name="p">Parametry hvězdy.</param>
        /// <param name="vernalPhase">Fáze jarní rovnodennosti jako zlomek roku.</param>
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

            // Rovnice středu — eliptická korekce na skutečnou polohu
            double C = 2 * p.Eccentricity * Math.Sin(M)
                          + 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);
            double lambda = WrapAngle(phase + C);

            // Deklinace ze sklonu osy a ekliptické délky
            double eps = DegToRad(p.AxialTiltDeg);
            double sinDelta = Math.Sin(eps) * Math.Sin(lambda);
            double delta = RadToDeg(Math.Asin(sinDelta));

            // Rovnice času (Spencer/Fourier aproximace)
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
        /// Vrátí frakci aktuálního dne (0..1) z worldTicků.
        /// </summary>
        private static double FractionOfDay(long worldTicks, WorldTimeSpec spec)
        {
            long dayTicks = worldTicks % spec.TicksPerDay;
            if (dayTicks < 0) dayTicks += spec.TicksPerDay;
            return (double)dayTicks / spec.TicksPerDay;
        }

        /// <summary>
        /// Převede hodinový úhel (hodiny světového dne) na radiány.
        /// </summary>
        private static double HoursToAngle(double hours, int hoursPerDay)
            => hours / hoursPerDay * TwoPi;

        /// <summary>
        /// Převede hodinový úhel v radiánech na hodiny světového dne.
        /// </summary>
        private static double AngleToHours(double angleRad, int hoursPerDay)
            => angleRad / TwoPi * hoursPerDay;

        /// <summary>
        /// Převede zeměpisnou délku na posun v hodinách světového dne.
        /// </summary>
        private static double LongitudeHours(double longitudeDeg, int hoursPerDay)
            => longitudeDeg / 360.0 * hoursPerDay;

        /// <summary>
        /// Zabalí hodiny do rozsahu [0, HoursPerDay) — wraparound přes půlnoc.
        /// </summary>
        private static double WrapHours(double h, int hoursPerDay)
        {
            h %= hoursPerDay;
            if (h < 0) h += hoursPerDay;
            return h;
        }

        /// <summary>
        /// Vypočítá hodinový úhel H0 pro daný práh výšky Slunce nad obzorem.
        /// </summary>
        /// <param name="latDeg">Zeměpisná šířka v stupních.</param>
        /// <param name="declDeg">Deklinace Slunce v stupních.</param>
        /// <param name="h0Deg">Práh výšky Slunce v stupních (záporný = pod obzorem).</param>
        /// <param name="polarDay"><c>true</c> pokud Slunce v daný den nezapadá.</param>
        /// <param name="polarNight"><c>true</c> pokud Slunce v daný den nevychází.</param>
        /// <returns>Hodinový úhel v radiánech, nebo <c>0</c> při polárním dni/noci.</returns>
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

        /// <summary>Zabalí úhel v radiánech do rozsahu [0, 2π).</summary>
        private static double WrapAngle(double a)
        {
            a %= TwoPi;
            if (a < 0) a += TwoPi;
            return a;
        }

        /// <summary>Převede stupně na radiány.</summary>
        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Převede radiány na stupně.</summary>
        private static double RadToDeg(double rad) => rad * 180.0 / Math.PI;

        #endregion Privátní výpočetní metody
    }
}
