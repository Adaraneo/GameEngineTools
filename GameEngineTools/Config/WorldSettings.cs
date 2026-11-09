// WorldSettings.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Config
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.Extensions.Configuration;

    /// <summary><c>World:Planet</c> in appsettings.World.json layers appsettings.World.&lt;Planet&gt;.json (Universe, Astro, Calendar) over it: a file next to the base wins, else the planet shipped inside this assembly.</summary>
    public static class WorldSettings
    {
        /// <summary>Base file name; every consumer looks for it by this name.</summary>
        public const string BaseFileName = "appsettings.World.json";

        /// <summary>Configuration key that selects the planet file.</summary>
        public const string PlanetKey = "World:Planet";

        private const string ResourcePrefix = "GameEngineTools.Planets.appsettings.World.";
        private const string FilePrefix = "appsettings.World.";

        /// <summary>Adds <paramref name="baseFilePath"/> and, when it selects a planet, the matching planet after it.</summary>
        /// <exception cref="FileNotFoundException">The base file or the selected planet does not exist, on disk or shipped.</exception>
        public static IConfigurationBuilder AddWorldFile(this IConfigurationBuilder builder, string baseFilePath)
        {
            var fullPath = Path.GetFullPath(baseFilePath);
            builder.AddJsonFile(fullPath, optional: false);

            var planet = SelectedPlanet(fullPath);
            if (planet is null)
                return builder;

            var diskFile = Path.Combine(Path.GetDirectoryName(fullPath)!, $"{FilePrefix}{planet}.json");
            if (File.Exists(diskFile))
                return builder.AddJsonFile(diskFile, optional: false);

            using var stream = typeof(WorldSettings).Assembly.GetManifestResourceStream($"{ResourcePrefix}{planet}.json")
                ?? throw new FileNotFoundException(
                    $"{PlanetKey} is '{planet}' but there is no {FilePrefix}{planet}.json next to {BaseFileName} and no such shipped planet. Available planets: {Describe(AvailablePlanets(fullPath))}.", diskFile);
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return builder.AddJsonStream(copy);
        }

        /// <summary>Builds a configuration from the base file plus its selected planet.</summary>
        public static IConfigurationRoot Load(string baseFilePath)
            => new ConfigurationBuilder().AddWorldFile(baseFilePath).Build();

        /// <summary>Planet names that are shipped in the assembly or have a file next to <paramref name="baseFilePath"/>.</summary>
        public static IReadOnlyList<string> AvailablePlanets(string baseFilePath)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(baseFilePath))!;
            var onDisk = Directory.GetFiles(dir, $"{FilePrefix}*.json").Select(f => Path.GetFileNameWithoutExtension(f)[FilePrefix.Length..]);
            var shipped = typeof(WorldSettings).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n[ResourcePrefix.Length..^".json".Length]);
            return onDisk.Concat(shipped)
                .Where(n => n.Length > 0 && !n.Equals("Default", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Checks that the planet named by the base file exists; throws like <see cref="AddWorldFile"/> when it does not.</summary>
        public static void Validate(string baseFilePath) => Load(baseFilePath);

        private static string? SelectedPlanet(string fullPath)
        {
            var planet = new ConfigurationBuilder().AddJsonFile(fullPath, optional: false).Build()[PlanetKey];
            if (string.IsNullOrWhiteSpace(planet))
                return null;
            if (!planet.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                throw new InvalidOperationException($"{PlanetKey} '{planet}' must be a plain name (letters, digits, '-' or '_').");
            return planet;
        }

        private static string Describe(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
