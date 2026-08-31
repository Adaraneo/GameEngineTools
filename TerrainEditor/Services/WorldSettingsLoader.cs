using System.IO;
using GameEngineTools.World.Core.Astro;
using Microsoft.Extensions.Configuration;

namespace TerrainEditor.Services;

/// <summary>
/// Loads a world's cosmology settings (the <c>World:Astro</c> section of <c>appsettings.World.json</c>)
/// for a given <c>world.db</c>. Each world can have its own settings file — unlike the database,
/// they're not baked into anything persistent, so this re-reads the file fresh every time a database
/// is opened rather than caching it.
/// </summary>
/// <remarks>
/// <para>
/// The settings file isn't always right next to the database — e.g. WorldObserver's db self-seeds
/// at <c>SourceFiles\World\world.db</c> while its <c>appsettings.World.json</c> sits at the project
/// root, two levels up. Rather than hard-code that one offset (which would break for a db opened
/// from anywhere else — a different project's layout, or an arbitrary path the user picked via
/// "Open World DB…"), this walks upward through parent directories from the db's folder looking for
/// the file, the same way tools like <c>tsconfig.json</c>/<c>.gitignore</c> discovery do.
/// </para>
/// <para>
/// <see cref="TryLoadAstroConfig"/>'s result (latitude/seasonal temperature) isn't consumed by any
/// generator yet — that's the still-pending altitude→temperature follow-up.
/// <see cref="TryLoadUniverseConfig"/>'s result IS consumed: <c>TerrainGenerator</c> scales the
/// mountain layer's maximum uplift by the planet's surface gravity (derived from
/// <c>PlanetMassKg</c>/<c>PlanetEquatorialRadiusKm</c>) via <c>MainWindow.GoToLatLon</c>.
/// </para>
/// </remarks>
public static class WorldSettingsLoader
{
    public const string FileName = "appsettings.World.json";

    /// <summary>How many parent directories above the db's folder to search — covers "same folder"
    /// and "a couple of levels up" (WorldObserver's layout) without searching the whole drive.</summary>
    public const int MaxParentDirectoriesToSearch = 6;

    /// <summary>
    /// Searches upward from <paramref name="dbFilePath"/>'s folder for <see cref="FileName"/> and
    /// binds its <c>World:Astro</c> section, mirroring the same
    /// <c>configProvider.GetSection("World:Astro").Get&lt;AstroConfig&gt;()</c> pattern GameSandbox
    /// uses. Returns <c>null</c> if no settings file is found or it has no <c>World:Astro</c>
    /// section — the caller should fall back to <c>new AstroConfig()</c> defaults.
    /// </summary>
    public static AstroConfig? TryLoadAstroConfig(string dbFilePath)
    {
        var settingsPath = FindSettingsFile(dbFilePath);
        if (settingsPath is null) return null;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build();

        var section = configuration.GetSection("World:Astro");
        return section.Exists() ? section.Get<AstroConfig>() : null;
    }

    /// <summary>
    /// Same search as <see cref="TryLoadAstroConfig"/>, but binds the <c>World:Universe</c>
    /// section instead — the planet's physical parameters (mass, radius, atmosphere, ...).
    /// Returns <c>null</c> if no settings file is found or it has no <c>World:Universe</c>
    /// section — the caller should fall back to <c>new UniverseConfig()</c> (Earth/Sol) defaults.
    /// </summary>
    public static UniverseConfig? TryLoadUniverseConfig(string dbFilePath)
    {
        var settingsPath = FindSettingsFile(dbFilePath);
        if (settingsPath is null) return null;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build();

        var section = configuration.GetSection("World:Universe");
        return section.Exists() ? section.Get<UniverseConfig>() : null;
    }

    private static string? FindSettingsFile(string dbFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbFilePath));

        for (var i = 0; i < MaxParentDirectoriesToSearch && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
