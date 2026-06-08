// SunParams.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro
{
    /// <summary>
    /// An immutable set of parameters describing a star and the planet's orbit around it.
    /// Use it for any world — not just Earth.
    /// </summary>
    /// <remarks>
    /// All angular values are in degrees (°). Example for Earth:
    /// <code>
    /// var earth = new SunParams(axialTiltDeg: 23.44);
    /// </code>
    /// </remarks>
    public readonly struct SunParams
    {
        #region Vlastnosti

        /// <summary>
        /// Axial tilt of the planet in degrees (°).
        /// Drives the cycle of seasons — 0° = no seasons, 23.44° = Earth.
        /// </summary>
        public readonly double AxialTiltDeg;

        /// <summary>
        /// Eccentricity of the planet's elliptical orbit (dimensionless, 0..0.5).
        /// <c>0</c> = circular orbit, <c>0.0167</c> = Earth, <c>0.2</c> = strongly elliptical.
        /// Values above 0.5 are clamped in the constructor.
        /// </summary>
        public readonly double Eccentricity;

        /// <summary>
        /// Phase of perihelion (the closest point to the star) as a fraction of the year in [0, 1).
        /// <c>0.0</c> = perihelion at the start of the year (day 1 of month 1).
        /// Hodnoty mimo rozsah jsou automaticky zabaleny (wrap).
        /// </summary>
        public readonly double PeriapsisPhase;

        /// <summary>
        /// Atmospheric refraction of light at sunrise/sunset in degrees (°).
        /// Causes the Sun to be visible even slightly below the geometric horizon.
        /// Default value for Earth: <c>0.566</c>°.
        /// </summary>
        public readonly double RefractionDeg;

        /// <summary>
        /// Apparent radius of the solar disc in degrees (°).
        /// Affects the exact moment of full sunrise/sunset.
        /// Default value for Earth: <c>0.266</c>°.
        /// </summary>
        public readonly double ApparentRadiusDeg;

        /// <summary>
        /// Depth of the Sun below the horizon for civil twilight (°).
        /// Default <c>6</c>° — still light without artificial illumination.
        /// </summary>
        public readonly double TwilightCivilDeg;

        /// <summary>
        /// Depth of the Sun below the horizon for nautical twilight (°).
        /// Default <c>12</c>° — the horizon is still visible for navigation.
        /// </summary>
        public readonly double TwilightNauticalDeg;

        /// <summary>
        /// Depth of the Sun below the horizon for astronomical twilight (°).
        /// Default <c>18</c>° — full darkness, ideal for stargazing.
        /// </summary>
        public readonly double TwilightAstronomicalDeg;

        #endregion Vlastnosti

        #region Odvozené vlastnosti

        /// <summary>
        /// The Sun's altitude above the horizon at standard sunrise/sunset in degrees (°).
        /// Negative value — at sunrise/sunset the Sun is still slightly below the geometric horizon
        /// because of atmospheric refraction and the apparent radius of the disc.
        /// On Earth this corresponds to the standard <c>−0.833°</c>.
        /// </summary>
        public double H0DegSunrise => -(RefractionDeg + ApparentRadiusDeg);

        #endregion Odvozené vlastnosti

        #region Konstrukce

        /// <summary>
        /// Initialises the star parameters. Specify only <paramref name="axialTiltDeg"/> —
        /// the other values approximate Earth and serve as a good starting point
        /// for fictional worlds.
        /// </summary>
        /// <param name="axialTiltDeg">
        /// Axial tilt of the planet in degrees. Required parameter — directly controls the strength of the seasons.
        /// </param>
        /// <param name="eccentricity">
        /// Orbital eccentricity (0..0.5). Default <c>0.0167</c> (Earth).
        /// Values above 0.5 are clamped.
        /// </param>
        /// <param name="periapsisPhase">
        /// Phase of perihelion as a fraction of the year [0, 1). Default <c>0.0</c>.
        /// Hodnoty mimo rozsah jsou zabaleny (wrap).
        /// </param>
        /// <param name="refractionDeg">Atmospheric refraction in degrees. Default <c>0.566</c>.</param>
        /// <param name="apparentRadiusDeg">Apparent disc radius in degrees. Default <c>0.266</c>.</param>
        /// <param name="twilightCivilDeg">Civil-twilight threshold. Default <c>6</c>°.</param>
        /// <param name="twilightNauticalDeg">Nautical-twilight threshold. Default <c>12</c>°.</param>
        /// <param name="twilightAstronomicalDeg">Astronomical-twilight threshold. Default <c>18</c>°.</param>
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
