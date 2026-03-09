// SunParams.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro
{
    /// <summary>
    /// Neměnná sada parametrů popisující hvězdu a oběžnou dráhu planety kolem ní.
    /// Použij ji pro libovolný svět — nejen Zemi.
    /// </summary>
    /// <remarks>
    /// Všechny úhlové hodnoty jsou ve stupních (°). Příklad pro Zemi:
    /// <code>
    /// var earth = new SunParams(axialTiltDeg: 23.44);
    /// </code>
    /// </remarks>
    public readonly struct SunParams
    {
        #region Vlastnosti

        /// <summary>
        /// Sklon rotační osy planety ve stupních (°).
        /// Řídí střídání ročních období — 0° = žádná roční období, 23.44° = Země.
        /// </summary>
        public readonly double AxialTiltDeg;

        /// <summary>
        /// Excentricita eliptické dráhy planety (bezrozměrná, 0..0.5).
        /// <c>0</c> = kruhová dráha, <c>0.0167</c> = Země, <c>0.2</c> = výrazně eliptická.
        /// Hodnoty nad 0.5 jsou oříznuty v konstruktoru.
        /// </summary>
        public readonly double Eccentricity;

        /// <summary>
        /// Fáze perihélia (nejbližší bod k hvězdě) jako zlomek roku v rozsahu [0, 1).
        /// <c>0.0</c> = perihélium je na začátku roku (1. den 1. měsíce).
        /// Hodnoty mimo rozsah jsou automaticky zabaleny (wrap).
        /// </summary>
        public readonly double PeriapsisPhase;

        /// <summary>
        /// Atmosférická refrakce světla při východu/západu Slunce ve stupních (°).
        /// Způsobuje, že Slunce je viditelné i mírně pod geometrickým obzorem.
        /// Výchozí hodnota pro Zemi: <c>0.566</c>°.
        /// </summary>
        public readonly double RefractionDeg;

        /// <summary>
        /// Zdánlivý poloměr slunečního kotouče ve stupních (°).
        /// Ovlivňuje přesný okamžik úplného východu/západu Slunce.
        /// Výchozí hodnota pro Zemi: <c>0.266</c>°.
        /// </summary>
        public readonly double ApparentRadiusDeg;

        /// <summary>
        /// Hloubka Slunce pod obzorem pro civilní soumrak (°).
        /// Výchozí <c>6</c>° — stále světlo bez umělého osvětlení.
        /// </summary>
        public readonly double TwilightCivilDeg;

        /// <summary>
        /// Hloubka Slunce pod obzorem pro nautický soumrak (°).
        /// Výchozí <c>12</c>° — horizont ještě viditelný pro navigaci.
        /// </summary>
        public readonly double TwilightNauticalDeg;

        /// <summary>
        /// Hloubka Slunce pod obzorem pro astronomický soumrak (°).
        /// Výchozí <c>18</c>° — úplná tma, ideální pro pozorování hvězd.
        /// </summary>
        public readonly double TwilightAstronomicalDeg;

        #endregion Vlastnosti

        #region Odvozené vlastnosti

        /// <summary>
        /// Výška Slunce nad obzorem při standardním východu/západu ve stupních (°).
        /// Záporná hodnota — Slunce je při svém východu/západu ještě mírně pod geometrickým obzorem
        /// kvůli atmosférické refrakci a zdánlivému poloměru kotouče.
        /// Na Zemi odpovídá standardním <c>−0.833°</c>.
        /// </summary>
        public double H0DegSunrise => -(RefractionDeg + ApparentRadiusDeg);

        #endregion Odvozené vlastnosti

        #region Konstrukce

        /// <summary>
        /// Inicializuje parametry hvězdy. Zadej pouze <paramref name="axialTiltDeg"/> —
        /// ostatní hodnoty odpovídají přibližně Zemi a jsou vhodné jako výchozí bod
        /// pro fiktivní světy.
        /// </summary>
        /// <param name="axialTiltDeg">
        /// Sklon rotační osy planety ve stupních. Povinný parametr — přímo řídí sílu ročních období.
        /// </param>
        /// <param name="eccentricity">
        /// Excentricita dráhy (0..0.5). Výchozí <c>0.0167</c> (Země).
        /// Hodnoty nad 0.5 jsou oříznuty.
        /// </param>
        /// <param name="periapsisPhase">
        /// Fáze perihélia jako zlomek roku [0, 1). Výchozí <c>0.0</c>.
        /// Hodnoty mimo rozsah jsou zabaleny (wrap).
        /// </param>
        /// <param name="refractionDeg">Atmosférická refrakce ve stupních. Výchozí <c>0.566</c>.</param>
        /// <param name="apparentRadiusDeg">Zdánlivý poloměr kotouče ve stupních. Výchozí <c>0.266</c>.</param>
        /// <param name="twilightCivilDeg">Práh civilního soumraku. Výchozí <c>6</c>°.</param>
        /// <param name="twilightNauticalDeg">Práh nautického soumraku. Výchozí <c>12</c>°.</param>
        /// <param name="twilightAstronomicalDeg">Práh astronomického soumraku. Výchozí <c>18</c>°.</param>
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
            PeriapsisPhase = periapsisPhase - Math.Floor(periapsisPhase); // wrap do [0,1)
            RefractionDeg = refractionDeg;
            ApparentRadiusDeg = apparentRadiusDeg;
            TwilightCivilDeg = twilightCivilDeg;
            TwilightNauticalDeg = twilightNauticalDeg;
            TwilightAstronomicalDeg = twilightAstronomicalDeg;
        }

        #endregion Konstrukce
    }
}
