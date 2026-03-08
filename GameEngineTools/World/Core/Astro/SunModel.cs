// SunModel.cs
// Copyright (c) 50PSoftware

using GameEngineTools.World.Utils.Time;

namespace GameEngineTools.World.Core.Astro
{
    public static class SunModel
    {
        private const double TwoPi = Math.PI * 2.0;

        private static double AngleToHours(double angleRad)
                    => angleRad / TwoPi * WDateTime.Spec.HoursPerDay;

        // === Matematické utility ==============================================
        private static double DegToRad(double x) => x * Math.PI / 180.0;

        private static double FractionOfDay(long worldTicks)
        {
            var spec = WDateTime.Spec;
            long dayTicks = worldTicks % spec.TicksPerDay;
            if (dayTicks < 0)
            {
                dayTicks += spec.TicksPerDay;
            }

            return (double)dayTicks / spec.TicksPerDay;
        }

        private static double HourAngleForAltitude(double latDeg, double declDeg, double h0Deg,
                                                           out bool polarDay, out bool polarNight)
        {
            double phi = DegToRad(latDeg);
            double delta = DegToRad(declDeg);
            double h0 = DegToRad(h0Deg);

            double cosH0 = (Math.Sin(h0) - Math.Sin(phi) * Math.Sin(delta)) / (Math.Cos(phi) * Math.Cos(delta));

            if (cosH0 < -1.0) { polarDay = true; polarNight = false; return 0; }
            if (cosH0 > 1.0) { polarDay = false; polarNight = true; return 0; }

            polarDay = polarNight = false;
            return Math.Acos(Math.Clamp(cosH0, -1, 1));
        }

        private static double HoursToAngle(double hours)
                    => hours / WDateTime.Spec.HoursPerDay * TwoPi;

        // === Pomocné převody (vázané na WorldTimeSpec) ========================
        private static double LongitudeHours(double longitudeDeg)
            => longitudeDeg / 360.0 * WDateTime.Spec.HoursPerDay;

        private static double RadToDeg(double x) => x * 180.0 / Math.PI;

        /// <summary>
        /// Declination δ (deg), Equation of Time (frakce dne), ekliptická délka λ (deg) a relativní vzdálenost r (AU-like).
        /// Vše škálované na libovolnou délku roku v tvém kalendáři.
        /// </summary>
        private static (double deltaDeg, double eotFracOfDay, double lambdaDeg, double rAu)
            SolarDeclinationEOT(WDateTime t, in SunParams p, double vernalPhase)
        {
            var spec = WDateTime.Spec;

            // den v roce (0..Y-1), Y = délka aktuálního roku
            var dayIndex = t.WorldTicks / spec.TicksPerDay;
            var (y, m, d) = spec.Calendar.DateFromDays(dayIndex);
            long doy = dayIndex - spec.Calendar.DaysFromDate(y, 1, 1);
            double Y = spec.Calendar.DaysInYear(y);

            // střední anomálie M a střední dloužka L (0..2π)
            double phase = (doy / Y + vernalPhase) * TwoPi;
            double M = (doy / Y + p.PeriapsisPhase) * TwoPi;
            // rovnice středu pro eliptickou dráhu (1. a 2. řád bohatě stačí)
            double C = 2 * p.Eccentricity * Math.Sin(M) + 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);
            double nu = M + C;              // pravá anomálie
            double lambda = WrapAngle(phase + C); // ekliptická délka Slunce (Sun’s ecliptic longitude ~ -Earth)

            // sklon ekliptiky ε (axial tilt)
            double eps = DegToRad(p.AxialTiltDeg);

            // deklinace δ
            double sinDelta = Math.Sin(eps) * Math.Sin(lambda);
            double delta = RadToDeg(Math.Asin(sinDelta));

            // equation of time (frakce dne) – aproximace (adaptovaná z klasických vzorců)
            // y = tan²(ε/2)
            double yv = Math.Tan(eps / 2.0);
            yv *= yv;

            // Mean longitude L0 (vztažená ke stejné nule jako 'phase')
            double L0 = phase;
            double EoT_rad =
                 yv * Math.Sin(2 * L0)
               - 2 * p.Eccentricity * Math.Sin(M)
               + 4 * p.Eccentricity * yv * Math.Sin(M) * Math.Cos(2 * L0)
               - 0.5 * yv * yv * Math.Sin(4 * L0)
               - 1.25 * p.Eccentricity * p.Eccentricity * Math.Sin(2 * M);

            // EOT ve zlomku dne (na Zemi by se převádělo na minuty; my rovnou do “světového dne”)
            double eotFrac = EoT_rad / TwoPi; // standardně EOT je ~ úhlový čas/2π

            // relativní vzdálenost (jednotka libovolná; 1 ~ střední vzd.)
            double r = 1 - p.Eccentricity * Math.Cos(M); // jednoduché 1st order

            return (delta, eotFrac, RadToDeg(lambda), r);
        }

        private static double WrapAngle(double a)
        {
            a %= TwoPi; if (a < 0)
            {
                a += TwoPi;
            }

            return a;
        }

        // === Vnitřnosti =======================================================
        private static double WrapHours(double h)
        {
            double d = WDateTime.Spec.HoursPerDay;
            h %= d; if (h < 0)
            {
                h += d;
            }

            return h;
        }

        /// <summary>Relativní ozáření (0..∞) ~ cos(zenit) / r², pro aktuální polohu a okamžik.</summary>
        public static double IrradianceFactor(WDateTime instant, double latitudeDeg, double longitudeDeg, in SunParams p, double vernalPhase = 0.0)
        {
            var (az, alt, _) = SolarPosition(instant, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var (_, _, _, rAu) = SolarDeclinationEOT(instant, in p, vernalPhase);
            double mu = Math.Max(0.0, Math.Sin(DegToRad(alt))); // ~ cos(zenit)
            double invR2 = 1.0 / (rAu * rAu);
            return mu * invR2;
        }

        /// <summary>Sluneční poledne (hodina dne), východ, západ a délka dne (hodiny) pro dané datum/zem. šířku/longitudu.</summary>
        public static (double solarNoonHour, double sunriseHour, double sunsetHour, double daylightHours)
            SolarDay(WDateTime date, double latitudeDeg, double longitudeDeg, in SunParams p, double vernalPhase = 0.0)
        {
            var spec = WDateTime.Spec;
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(date, in p, vernalPhase);

            // hour angle v rad pro daný prah výšky h0 (východ/západ)
            bool polarDay, polarNight;
            var H0 = HourAngleForAltitude(latitudeDeg, delta, p.H0DegSunrise, out polarDay, out polarNight);

            // Solar noon (hodina od půlnoci “světových hodin”)
            // = střed dne – posun longituda – EOT
            double noon = spec.HoursPerDay * 0.5
                        - LongitudeHours(longitudeDeg)
                        - eotFrac * spec.HoursPerDay;

            if (polarNight)
            {
                return (WrapHours(noon), double.NaN, double.NaN, 0.0);
            }

            if (polarDay)
            {
                return (WrapHours(noon), 0.0, spec.HoursPerDay, spec.HoursPerDay);
            }

            double H0h = AngleToHours(H0);
            double sunrise = WrapHours(noon - H0h);
            double sunset = WrapHours(noon + H0h);
            double daylight = 2 * H0h;

            return (noon, sunrise, sunset, daylight);
        }

        /// <summary>Azimut/altitude Slunce pro konkrétní okamžik a pozici (°).</summary>
        public static (double azimuthDeg, double altitudeDeg, double declinationDeg)
            SolarPosition(WDateTime instant, double latitudeDeg, double longitudeDeg, in SunParams p, double vernalPhase = 0.0)
        {
            var spec = WDateTime.Spec;

            var (delta, eotFrac, lambda, rAu) = SolarDeclinationEOT(instant, in p, vernalPhase);

            // Lokální sluneční čas (LST) v hodinách: frakce dne + posun longituda + EOT
            double fracDay = FractionOfDay(instant.WorldTicks);
            double lstHours = fracDay * spec.HoursPerDay
                            + LongitudeHours(longitudeDeg)
                            + eotFrac * spec.HoursPerDay;

            // Převod na hodinový úhel H (radiány), H=0 na lokálním poledníku, roste na západ
            double H = HoursToAngle(lstHours - spec.HoursPerDay * 0.5);

            // Výška a azimut (astronomické vzorce)
            double phi = DegToRad(latitudeDeg);
            double sinAlt = Math.Sin(DegToRad(delta)) * Math.Sin(phi) + Math.Cos(DegToRad(delta)) * Math.Cos(phi) * Math.Cos(H);
            double alt = RadToDeg(Math.Asin(sinAlt));

            // Azimut: měřeno od severu na východ (0..360)
            double cosAz = (Math.Sin(DegToRad(delta)) - Math.Sin(phi) * sinAlt) / (Math.Cos(phi) * Math.Cos(DegToRad(alt)));
            cosAz = Math.Clamp(cosAz, -1, 1);
            double az = RadToDeg(Math.Acos(cosAz));
            if (Math.Sin(H) > 0)
            {
                az = 360 - az;
            }

            return (az, alt, delta);
        }

        /// <summary>Soumraky pro den: civil/nautical/astronomical (hodiny dne).</summary>
        public static (double civilDawn, double sunrise, double solarNoon, double sunset, double civilDusk,
                       double nauticalDawn, double nauticalDusk,
                       double astroDawn, double astroDusk)
            Twilights(WDateTime date, double latitudeDeg, double longitudeDeg, in SunParams p, double vernalPhase = 0.0)
        {
            var (noon, sunrise, sunset, _) = SolarDay(date, latitudeDeg, longitudeDeg, in p, vernalPhase);
            var spec = WDateTime.Spec;
            var (delta, eotFrac, _, _) = SolarDeclinationEOT(date, in p, vernalPhase);

            // Helper k výpočtu časů pro libovolný prah výšky
            (double dawn, double dusk) Edge(double h0)
            {
                bool pd, pn;
                var H0 = HourAngleForAltitude(latitudeDeg, delta, h0, out pd, out pn);
                if (pn)
                {
                    return (double.NaN, double.NaN);
                }

                if (pd)
                {
                    return (0.0, spec.HoursPerDay);
                }

                var H0h = AngleToHours(H0);
                double dawnH = WrapHours(noon - H0h);
                double duskH = WrapHours(noon + H0h);
                return (dawnH, duskH);
            }

            var (civilDawn, civilDusk) = Edge(-p.TwilightCivilDeg);
            var (nautDawn, nautDusk) = Edge(-p.TwilightNauticalDeg);
            var (astroDawn, astroDusk) = Edge(-p.TwilightAstronomicalDeg);

            return (civilDawn, sunrise, noon, sunset, civilDusk,
                    nautDawn, nautDusk,
                    astroDawn, astroDusk);
        }
    }
}
