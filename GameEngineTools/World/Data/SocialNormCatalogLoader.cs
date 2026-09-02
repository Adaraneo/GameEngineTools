// SocialNormCatalogLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Constants;

    /// <summary>
    /// Loads the default <see cref="SocialNormRow"/> catalog from a semicolon-delimited CSV —
    /// same three-tier resolution as <see cref="NutritionCatalogLoader"/>:
    /// <list type="number">
    ///   <item>an explicit <c>diskPath</c> passed by the caller, when given and present;</item>
    ///   <item>the disk override at <see cref="FileSystemConstant.SourceFilePath.SocialNorms"/>
    ///   (<c>SourceFiles\World\SocialNorms.csv</c>) — lets a designer customise/replace the
    ///   default norms entirely without recompiling (it fully replaces the embedded set, it does
    ///   not merge with it);</item>
    ///   <item>the default catalog embedded in <c>GameEngineTools.dll</c>.</item>
    /// </list>
    /// <see cref="WorldDatabaseSeeder.Initialize"/> inserts every row from this catalog (via
    /// <see cref="SqliteWorldDatabase.InsertSocialNorm"/>, <c>INSERT OR IGNORE</c>) the first time
    /// a database is seeded — this replaces the SocialNorms rows that used to be hand-written SQL
    /// literals in <c>seed_data.sql</c>.
    /// </summary>
    public static class SocialNormCatalogLoader
    {
        private const string EmbeddedResourceName = "GameEngineTools.World.Data.SocialNorms.csv";
        private const int ExpectedColumnCount = 9;

        /// <summary>
        /// Loads the catalog using the three-tier resolution described on the type.
        /// </summary>
        public static IReadOnlyList<SocialNormRow> Load(string? diskPath = null)
        {
            string csv;
            if (diskPath is not null && File.Exists(diskPath))
            {
                csv = File.ReadAllText(diskPath, Encoding.UTF8);
            }
            else if (File.Exists(FileSystemConstant.SourceFilePath.SocialNorms))
            {
                csv = File.ReadAllText(FileSystemConstant.SourceFilePath.SocialNorms, Encoding.UTF8);
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                    ?? throw new FileNotFoundException(
                        $"Embedded resource '{EmbeddedResourceName}' not found — add SocialNorms.csv to GameEngineTools.csproj as <EmbeddedResource>.");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                csv = reader.ReadToEnd();
            }

            return Parse(csv);
        }

        /// <summary>Parses CSV text into rows. Public so tests and callers can validate a custom
        /// catalog string without needing a file on disk. Validates <c>Kind</c>/<c>RelationalModel</c>
        /// against their enums at parse time (fail fast) even though the row itself stores them as
        /// plain strings, matching <see cref="SocialNormRow"/>'s column shape.</summary>
        public static IReadOnlyList<SocialNormRow> Parse(string csv)
        {
            var rows = new List<SocialNormRow>();
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
                    throw new FormatException($"SocialNorms.csv line {lineNumber}: expected {ExpectedColumnCount} columns, got {cols.Length}.");

                var id = cols[0].Trim();
                if (id.Length == 0)
                    throw new FormatException($"SocialNorms.csv line {lineNumber}: Id is empty.");
                if (!seenIds.Add(id))
                    throw new FormatException($"SocialNorms.csv line {lineNumber}: duplicate Id '{id}'.");

                var kind = cols[2].Trim();
                if (!Enum.TryParse<SocialNormKind>(kind, ignoreCase: true, out _))
                    throw new FormatException($"SocialNorms.csv line {lineNumber}: '{kind}' is not a valid SocialNormKind.");

                var relationalModel = ParseNullableString(cols[5]);
                if (relationalModel is not null && !Enum.TryParse<RelationalModel>(relationalModel, ignoreCase: true, out _))
                    throw new FormatException($"SocialNorms.csv line {lineNumber}: '{relationalModel}' is not a valid RelationalModel.");

                rows.Add(new SocialNormRow(
                    Id: id,
                    DisplayName: cols[1].Trim(),
                    Kind: kind,
                    Severity: ParseDouble(cols[3], lineNumber, "Severity"),
                    EnforcementProbability: ParseDouble(cols[4], lineNumber, "EnforcementProbability"),
                    RelationalModel: relationalModel,
                    CultureId: ParseNullableString(cols[6]),
                    ValidFromYear: ParseNullableInt(cols[7]),
                    ValidToYear: ParseNullableInt(cols[8])));
            }

            return rows;
        }

        private static string? ParseNullableString(string field)
        {
            var trimmed = field.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static int? ParseNullableInt(string field)
        {
            var trimmed = field.Trim();
            return trimmed.Length == 0 ? null : int.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static double ParseDouble(string field, int lineNumber, string columnName)
        {
            var trimmed = field.Trim();
            if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new FormatException($"SocialNorms.csv line {lineNumber}: '{columnName}' is not a valid number ('{field}').");
            return value;
        }
    }
}
