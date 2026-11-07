// MonitorOptions.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using Microsoft.Extensions.Configuration;

    /// <summary>One named speed/size preset of a run.</summary>
    public sealed record RunProfile(int Characters = 6, int Days = 5, int TickStepMinutes = 64, int SubstepMinutes = 15, int Couples = 0);

    /// <summary>Pass/fail limits; a missing value means the check is not made.</summary>
    public sealed record Thresholds(double? MaxMedianStress = null, double? MinSleepWithPrivacyPercent = null, int? MaxDeaths = null, double? MaxChronicStressPercent = null, double ChronicStressLevel = 80);

    /// <summary>The <c>WorldMonitor</c> section of appsettings.json, with command-line overrides applied.</summary>
    public sealed record MonitorOptions(string ProfileName, RunProfile Run, double SampleEveryHours, string OutputDir, Thresholds Thresholds, string? WorldDb = null)
    {
        /// <summary>Reads appsettings.json; <c>--profile X</c>, <c>--days N</c>, <c>--characters N</c>, <c>--tick N</c>, <c>--substep N</c>, <c>--out DIR</c>, <c>--world-db FILE</c> override it.</summary>
        public static MonitorOptions Load(string[] args)
        {
            var overrides = ParseArgs(args);
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            var section = config.GetSection("WorldMonitor");

            var profileName = overrides.GetValueOrDefault("profile") ?? section["Profile"] ?? "Quick";
            var profileSection = section.GetSection("Profiles").GetSection(profileName);
            if (!profileSection.Exists())
                throw new InvalidOperationException($"Profile '{profileName}' is not defined under WorldMonitor:Profiles in appsettings.json.");

            var profile = profileSection.Get<RunProfile>() ?? new RunProfile();
            profile = profile with
            {
                Characters = IntOverride(overrides, "characters", profile.Characters),
                Days = IntOverride(overrides, "days", profile.Days),
                TickStepMinutes = IntOverride(overrides, "tick", profile.TickStepMinutes),
                SubstepMinutes = IntOverride(overrides, "substep", profile.SubstepMinutes),
                Couples = IntOverride(overrides, "couples", profile.Couples),
            };

            var sampleEveryHours = section.GetValue("SampleEveryHours", 6.0);
            if (sampleEveryHours <= 0 || profile.Days < 1 || profile.Characters < 1 || profile.TickStepMinutes < 1 || profile.SubstepMinutes < 1)
                throw new InvalidOperationException("SampleEveryHours, Days, Characters, TickStepMinutes and SubstepMinutes must all be positive.");

            return new MonitorOptions(
                profileName,
                profile,
                sampleEveryHours,
                overrides.GetValueOrDefault("out") ?? section["OutputDir"] ?? "reports",
                section.GetSection("Thresholds").Get<Thresholds>() ?? new Thresholds(),
                overrides.GetValueOrDefault("world-db"));
        }

        private static int IntOverride(Dictionary<string, string> overrides, string key, int fallback)
            => overrides.TryGetValue(key, out var text) && int.TryParse(text, out var value) ? value : fallback;

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                    result[args[i][2..]] = args[i + 1];
            }

            return result;
        }
    }
}
