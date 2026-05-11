// AstroConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Astro;

/// <summary>
/// Nastavení sluneční hvězdy a lokace pozorovatele pro výpočty
/// <see cref="CelestialContextComputer"/>. Vázáno z konfigurace
/// pod klíčem <c>World:Astro</c>.
/// </summary>
public sealed record AstroConfig(
    /// <summary>Parametry sluneční hvězdy a oběžné dráhy.</summary>
    SunParamsConfig Sun = null!,
    /// <summary>Zeměpisná šířka výchozí lokace světa (°). Ovlivňuje délku dne a intenzitu ozáření.</summary>
    double LatitudeDeg = 50.0,
    /// <summary>Zeměpisná délka výchozí lokace světa (°).</summary>
    double LongitudeDeg = 15.0,
    /// <summary>Průměrná roční teplota prostředí (°C).</summary>
    double BaseTemperatureCelsius = 11.0,
    /// <summary>Poloamplituda sezónního teplotního výkyvu (°C). Léto = +amplituda, zima = −amplituda.</summary>
    double SeasonalAmplitudeCelsius = 9.0,
    /// <summary>
    /// Fáze jarní rovnodennosti jako zlomek roku [0, 1).
    /// <c>0.0</c> = jarní rovnodennost na 1. den roku.
    /// </summary>
    double VernalPhase = 0.0)
{
    /// <summary>Výchozí instance s hodnotami blízkými Zemi.</summary>
    public AstroConfig() : this(new SunParamsConfig()) { }
}

/// <summary>
/// Serializovatelný zástupce <see cref="SunParams"/> pro IOptions binding.
/// Převod na <see cref="SunParams"/> provede <see cref="CelestialContextComputer"/>.
/// </summary>
public sealed record SunParamsConfig(
    double AxialTiltDeg = 23.44,
    double Eccentricity = 0.0167,
    double PeriapsisPhase = 0.0,
    double RefractionDeg = 0.566,
    double ApparentRadiusDeg = 0.266,
    double TwilightCivilDeg = 6.0,
    double TwilightNauticalDeg = 12.0,
    double TwilightAstronomicalDeg = 18.0)
{
    /// <summary>Výchozí instance s hodnotami přibližně odpovídajícími Zemi.</summary>
    public SunParamsConfig() : this(23.44) { }

    /// <summary>Převede config record na <see cref="SunParams"/> struct.</summary>
    public SunParams ToSunParams() => new(
        AxialTiltDeg,
        Eccentricity,
        PeriapsisPhase,
        RefractionDeg,
        ApparentRadiusDeg,
        TwilightCivilDeg,
        TwilightNauticalDeg,
        TwilightAstronomicalDeg);
}
