// MonitorOptions.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using GameEngineTools.Characters.Hosting;
    using Microsoft.Extensions.Configuration;

    /// <summary>One named speed/size preset of a run.</summary>
    public sealed record RunProfile(int Characters = 6, int Days = 5, int TickStepMinutes = 64, int SubstepMinutes = 15, int Couples = 0, bool Seasons = false, int? Seed = null, string? WorldDb = null, bool Barter = false, string? CharacterLod = null);

    /// <summary>Pass/fail limits; a missing value means the check is not made.</summary>
    public sealed record Thresholds(double? MaxMedianStress = null, double? MinSleepWithPrivacyPercent = null, int? MaxDeaths = null, double? MaxChronicStressPercent = null, double ChronicStressLevel = 80, double HungerLevel = 80);

    /// <summary>The <c>WorldMonitor</c> section of appsettings.json, with command-line overrides applied.</summary>
    public sealed record MonitorOptions(string ProfileName, RunProfile Run, double SampleEveryHours, string OutputDir, Thresholds Thresholds, string? WorldDb = null, bool Barter = false, int? Seed = null, int FarmPeriodDays = 30, CognitiveResolutionLevel Lod = CognitiveResolutionLevel.Nearby)
    {
        /// <summary>Reads appsettings.json; <c>--profile X</c>, <c>--days N</c>, <c>--characters N</c>, <c>--tick N</c>, <c>--substep N</c>, <c>--out DIR</c>, <c>--world-db FILE</c>, <c>--barter true</c>, <c>--seasons true</c>, <c>--seed N</c>, <c>--lod Player|Nearby|Background</c> override it; each is also a profile key (or a WorldMonitor key for WorldDb, Barter, Seed, CharacterLod, FarmPeriodDays).</summary>
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
                Seasons = overrides.TryGetValue("seasons", out var seasonsText) ? seasonsText is "true" or "1" : profile.Seasons,
            };

            // A command-line value wins over the profile, the profile over the WorldMonitor section.
            var worldDb = overrides.GetValueOrDefault("world-db") ?? profile.WorldDb ?? section["WorldDb"];
            var barter = overrides.TryGetValue("barter", out var barterText) ? barterText is "true" or "1" : profile.Barter || section.GetValue("Barter", false);
            var seed = overrides.TryGetValue("seed", out var seedText) && int.TryParse(seedText, out var seedValue) ? seedValue : profile.Seed ?? section.GetValue<int?>("Seed");
            var lodText = overrides.GetValueOrDefault("lod") ?? profile.CharacterLod ?? section["CharacterLod"] ?? nameof(CognitiveResolutionLevel.Nearby);
            if (!Enum.TryParse<CognitiveResolutionLevel>(lodText, ignoreCase: true, out var lod))
                throw new InvalidOperationException($"CharacterLod '{lodText}' is not one of {string.Join(", ", Enum.GetNames<CognitiveResolutionLevel>())}.");
            var farmPeriodDays = section.GetValue("FarmPeriodDays", 30);
            if (farmPeriodDays < 1)
                throw new InvalidOperationException("FarmPeriodDays must be positive.");

            var sampleEveryHours = section.GetValue("SampleEveryHours", 6.0);
            if (sampleEveryHours <= 0 || profile.Days < 1 || profile.Characters < 1 || profile.TickStepMinutes < 1 || profile.SubstepMinutes < 1)
                throw new InvalidOperationException("SampleEveryHours, Days, Characters, TickStepMinutes and SubstepMinutes must all be positive.");

            return new MonitorOptions(
                profileName,
                profile,
                sampleEveryHours,
                overrides.GetValueOrDefault("out") ?? section["OutputDir"] ?? "reports",
                section.GetSection("Thresholds").Get<Thresholds>() ?? new Thresholds(),
                worldDb,
                barter,
                seed,
                farmPeriodDays,
                lod);
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
