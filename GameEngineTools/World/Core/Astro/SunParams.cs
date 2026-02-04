namespace GameEngineTools.World.Core.Astro
{
    /// <summary>
    /// Parametry slunce / oběhu – vol je podle svého světa.
    /// - AxialTiltDeg: sklon rotační osy planety (°).
    /// - Eccentricity: excentricita dráhy (0..0.3 typicky, 0 = kruh).
    /// - PeriapsisPhase: fáze perihélia v [0..1) (0 = na začátku roku).
    /// - RefractionDeg: atmosférická refrakce pro východ/západ (°).
    /// - ApparentRadiusDeg: zdánlivý poloměr kotouče (°), ovlivní h0.
    /// - TwilightCivil/Nautical/Astronomical: prahy soumraku (° pod obzorem).
    /// </summary>
    public readonly struct SunParams
    {
        public readonly double ApparentRadiusDeg;
        public readonly double AxialTiltDeg;
        public readonly double Eccentricity;
        public readonly double PeriapsisPhase;
        public readonly double RefractionDeg;
        public readonly double TwilightAstronomicalDeg;
        public readonly double TwilightCivilDeg;
        public readonly double TwilightNauticalDeg;

        public SunParams(
            double axialTiltDeg,
            double eccentricity = 0.0167,
            double periapsisPhase = 0.0,
            double refractionDeg = 0.566,
            double apparentRadiusDeg = 0.266,
            double twilightCivilDeg = 6,
            double twilightNauticalDeg = 12,
            double twilightAstronomicalDeg = 18)
        {
            AxialTiltDeg = axialTiltDeg;
            Eccentricity = Math.Clamp(eccentricity, 0, 0.5);
            PeriapsisPhase = periapsisPhase - Math.Floor(periapsisPhase); // wrap
            RefractionDeg = refractionDeg;
            ApparentRadiusDeg = apparentRadiusDeg;
            TwilightCivilDeg = twilightCivilDeg;
            TwilightNauticalDeg = twilightNauticalDeg;
            TwilightAstronomicalDeg = twilightAstronomicalDeg;
        }

        public double H0DegSunrise => -(RefractionDeg + ApparentRadiusDeg); // standardní “-0.833°” na Zemi jako výchozí
    }
}