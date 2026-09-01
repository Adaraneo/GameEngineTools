using System.Text;
using GameEngineTools.World.Core.Astro;
using Microsoft.Extensions.Configuration;

namespace WorldGen;

/// <summary>
/// Loads a planet's physical parameters for a given database file path — independent port of
/// TerraGen's <c>PlanetSettings</c> (no project reference to TerraGen; see WorldGen.csproj) so
/// WorldGen derives the EXACT same seed/radius/tectonic-plate-count TerraGen used to generate the
/// terrain it's placing locations on, from the same <c>appsettings.World.json</c>, without the
/// user having to pass matching CLI flags to both tools by hand.
/// </summary>
public static class PlanetSettings
{
    public const string SettingsFileName = "appsettings.World.json";
    private const int MaxParentDirectoriesToSearch = 6;
    private const double EarthSurfaceGravityMs2 = 9.80665;
    private const double EarthRadiusMeters = 6_378_100.0;

    public sealed record Resolved(string PlanetName, double PlanetMassKg, double PlanetRadiusMeters,
        double GravityMs2, int Seed, int TectonicPlateCount);

    /// <summary>Searches upward from <paramref name="dbFilePath"/>'s folder for
    /// <see cref="SettingsFileName"/>, binds its <c>World:Universe</c> section (falling back to
    /// Earth/Sol defaults if the file or section is absent), and derives gravity/seed from it.</summary>
    public static Resolved Load(string dbFilePath)
    {
        var settingsPath = FindSettingsFile(dbFilePath);
        var planet = settingsPath is null
            ? new UniverseConfig()
            : new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false).Build()
                .GetSection("World:Universe").Get<UniverseConfig>() ?? new UniverseConfig();

        var radiusMeters = planet.PlanetEquatorialRadiusKm * 1000.0;
        if (radiusMeters <= 0) radiusMeters = EarthRadiusMeters;

        var gravityMs2 = GameEngineTools.Universe.PhysicalConstants.G * planet.PlanetMassKg / (radiusMeters * radiusMeters);
        if (!double.IsFinite(gravityMs2) || gravityMs2 <= 0) gravityMs2 = EarthSurfaceGravityMs2;

        var seed = ComputeSeed(planet.PlanetName, planet.PlanetMassKg, planet.PlanetEquatorialRadiusKm);

        return new Resolved(planet.PlanetName, planet.PlanetMassKg, radiusMeters, gravityMs2, seed,
            Math.Max(0, planet.PlanetTectonicPlateCount));
    }

    /// <summary>Stable FNV-1a hash of the planet's identity — same formula TerraGen's own
    /// <c>PlanetSettings.ComputeSeed</c> uses, so both tools agree on one planet's seed.</summary>
    public static int ComputeSeed(string planetName, double planetMassKg, double planetEquatorialRadiusKm)
    {
        var key = $"{planetName}|{planetMassKg:R}|{planetEquatorialRadiusKm:R}";
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return unchecked((int)hash);
    }

    private static string? FindSettingsFile(string dbFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbFilePath));

        for (var i = 0; i < MaxParentDirectoriesToSearch && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, SettingsFileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
