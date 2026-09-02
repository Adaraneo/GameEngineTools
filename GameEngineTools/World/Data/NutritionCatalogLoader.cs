// NutritionCatalogLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using GameEngineTools.Constants;
    using GameEngineTools.World.Objects;

    /// <summary>
    /// One row of <c>Nutrition.csv</c> — a food/drink/rest object template that a procedural
    /// generator (e.g. WorldGen) instantiates once per matching location it places.
    /// </summary>
    public sealed record FoodTemplate(
        string TemplateId,
        string DisplayName,
        /// <summary>Which location classification this template applies to (e.g.
        /// <c>"Forest"</c>, <c>"Mountain"</c>), or <c>"Any"</c> for every location.</summary>
        string Biome,
        AffordanceType AffordanceType,
        double Satisfaction,
        PickupItemKind ItemKind,
        int WeightGrams,
        int RespawnMinutes,
        bool Pickable,
        double? CalorieGain,
        double? ProteinGain,
        double? IronGain,
        double? VitaminDGain,
        double? HydrationGain,
        double? HemeIronFraction,
        double? VitaminCMilligrams);

    /// <summary>
    /// Loads the <see cref="FoodTemplate"/> catalog from a semicolon-delimited CSV — three-tier
    /// resolution, same shape as <see cref="SqlScriptLoader"/>/<c>schema.sql</c>:
    /// <list type="number">
    ///   <item>an explicit <c>diskPath</c> passed by the caller (e.g. WorldGen's own
    ///   <c>--nutrition-csv</c>), when given and present;</item>
    ///   <item>the disk override at <see cref="FileSystemConstant.SourceFilePath.Nutrition"/>
    ///   (<c>SourceFiles\World\Nutrition.csv</c>) — lets a designer customise the catalog without
    ///   recompiling;</item>
    ///   <item>the default catalog embedded in <c>GameEngineTools.dll</c>, so every consumer
    ///   shares one source of truth even without any disk file present.</item>
    /// </list>
    /// </summary>
    public static class NutritionCatalogLoader
    {
        private const string EmbeddedResourceName = "GameEngineTools.World.Data.Nutrition.csv";
        private const int ExpectedColumnCount = 16;

        /// <summary>
        /// Loads the catalog using the three-tier resolution described on the type.
        /// </summary>
        public static IReadOnlyList<FoodTemplate> Load(string? diskPath = null)
        {
            string csv;
            if (diskPath is not null && File.Exists(diskPath))
            {
                csv = File.ReadAllText(diskPath, Encoding.UTF8);
            }
            else if (File.Exists(FileSystemConstant.SourceFilePath.Nutrition))
            {
                csv = File.ReadAllText(FileSystemConstant.SourceFilePath.Nutrition, Encoding.UTF8);
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                    ?? throw new FileNotFoundException(
                        $"Embedded resource '{EmbeddedResourceName}' not found — add Nutrition.csv to GameEngineTools.csproj as <EmbeddedResource>.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                csv = reader.ReadToEnd();
            }

            return Parse(csv);
        }

        /// <summary>Parses CSV text into templates. Public so tests and callers can validate a
        /// custom catalog string without needing a file on disk.</summary>
        public static IReadOnlyList<FoodTemplate> Parse(string csv)
        {
            var templates = new List<FoodTemplate>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var reader = new StringReader(csv);
            reader.ReadLine(); // header

            string? line;
            var lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var cols = line.Split(';');
                if (cols.Length != ExpectedColumnCount)
                    throw new FormatException($"Nutrition.csv line {lineNumber}: expected {ExpectedColumnCount} columns, got {cols.Length}.");

                var templateId = cols[0].Trim();
                if (templateId.Length == 0)
                    throw new FormatException($"Nutrition.csv line {lineNumber}: TemplateId is empty.");
                if (!seenIds.Add(templateId))
                    throw new FormatException($"Nutrition.csv line {lineNumber}: duplicate TemplateId '{templateId}'.");

                templates.Add(new FoodTemplate(
                    TemplateId: templateId,
                    DisplayName: cols[1].Trim(),
                    Biome: cols[2].Trim(),
                    AffordanceType: Enum.Parse<AffordanceType>(cols[3].Trim(), ignoreCase: true),
                    Satisfaction: ParseDouble(cols[4], lineNumber, "Satisfaction"),
                    ItemKind: Enum.Parse<PickupItemKind>(cols[5].Trim(), ignoreCase: true),
                    WeightGrams: ParseInt(cols[6], lineNumber, "WeightGrams"),
                    RespawnMinutes: ParseInt(cols[7], lineNumber, "RespawnMinutes"),
                    Pickable: bool.Parse(cols[8].Trim()),
                    CalorieGain: ParseNullableDouble(cols[9]),
                    ProteinGain: ParseNullableDouble(cols[10]),
                    IronGain: ParseNullableDouble(cols[11]),
                    VitaminDGain: ParseNullableDouble(cols[12]),
                    HydrationGain: ParseNullableDouble(cols[13]),
                    HemeIronFraction: ParseNullableDouble(cols[14]),
                    VitaminCMilligrams: ParseNullableDouble(cols[15])));
            }

            return templates;
        }

        private static double? ParseNullableDouble(string field)
        {
            var trimmed = field.Trim();
            return trimmed.Length == 0 ? null : double.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string field, int lineNumber, string columnName)
        {
            var trimmed = field.Trim();
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Nutrition.csv line {lineNumber}: '{columnName}' is not a valid number ('{field}').");
            return value;
        }

        private static int ParseInt(string field, int lineNumber, string columnName)
        {
            var trimmed = field.Trim();
            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"Nutrition.csv line {lineNumber}: '{columnName}' is not a valid integer ('{field}').");
            return value;
        }
    }
}
