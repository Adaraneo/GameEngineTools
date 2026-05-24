// FileSystemConstant.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Constants
{
    /// <summary>
    /// Central registry of all file system paths used by the engine.
    /// All source-file paths are relative to the working directory
    /// (resolved at runtime by <c>prebuild.bat</c>).
    /// </summary>
    internal static class FileSystemConstant
    {
        #region Source directories

        /// <summary>
        /// Root directories for source CSV and JSON data files.
        /// </summary>
        private static class SourceDirectory
        {
            internal const string Root = @"SourceFiles\";
            internal const string Armory = Root + @"Armory\";
            internal const string Names = Root + @"Names\";

            /// <summary>Character generation data: occupations, personalities…</summary>
            internal const string Characters = Root + @"Characters\";

            /// <summary>World layout: locations, connections, and per-location objects.</summary>
            internal const string World = Root + @"World\";

            /// <summary>
            /// Per-location object files. Each file is named {locationId}.csv
            /// and loaded lazily on first access to that location.
            /// </summary>
            internal const string WorldObjects = World + @"Objects\";

            /// <summary>
            /// Directory containing SQL script overrides (schema.sql, seed_data.sql).
            /// When files are present here, they take precedence over embedded resources.
            /// </summary>
            internal const string WorldSql = World + @"SQL\";
        }

        #endregion Source directories

        #region Source filenames (without extension)

        /// <summary>
        /// Base filenames of source files, without path or extension.
        /// </summary>
        private static class SourceFilename
        {
            internal const string ArmorParts = "ArmorParts";
            internal const string FemaleNames = "Female";
            internal const string MaleNames = "Male";
            internal const string Surnames = "Surnames";
            internal const string Weapons = "Weapons";

            /// <summary>All location descriptors (Id, DisplayName, Type, Region, noise params…).</summary>
            internal const string Locations = "Locations";

            /// <summary>Adjacency graph between locations (FromId, ToId, TravelMinutes).</summary>
            internal const string Connections = "Connections";
        }

        #endregion Source filenames (without extension)

        #region Source file paths

        /// <summary>
        /// Full relative paths to source files consumed at startup.
        /// </summary>
        internal static class SourceFilePath
        {
            // ── Existing ──────────────────────────────────────────────────────
            public const string ArmorParts = SourceDirectory.Armory + SourceFilename.ArmorParts + Extension.SourceCsv;

            public const string FemaleNames = SourceDirectory.Names + SourceFilename.FemaleNames + Extension.SourceCsv;
            public const string MaleNames = SourceDirectory.Names + SourceFilename.MaleNames + Extension.SourceCsv;
            public const string Surnames = SourceDirectory.Names + SourceFilename.Surnames + Extension.SourceCsv;
            public const string Weapons = SourceDirectory.Armory + SourceFilename.Weapons + Extension.SourceCsv;

            // ── Characters ────────────────────────────────────────────────────

            /// <summary>
            /// Custom occupation definitions loaded at startup.
            /// Built-in occupations are registered in code; this file provides
            /// game-specific or modded additions.
            /// </summary>
            public const string Occupations = SourceDirectory.Characters + "Occupations" + Extension.SourceCsv;

            // ── World ─────────────────────────────────────────────────────────

            /// <summary>
            /// Path to Locations.csv.
            /// Loaded once at startup; defines all <see cref="LocationDescriptor"/> records.
            /// </summary>
            public const string Locations = SourceDirectory.World + SourceFilename.Locations + Extension.SourceCsv;

            /// <summary>
            /// Path to Connections.csv.
            /// Loaded once at startup; defines the adjacency graph used by <see cref="WorldMap"/>.
            /// </summary>
            public const string Connections = SourceDirectory.World + SourceFilename.Connections + Extension.SourceCsv;

            /// <summary>
            /// Directory containing per-location object files.
            /// Each file is named <c>{locationId}.csv</c> and loaded lazily.
            /// </summary>
            public const string WorldObjectsDirectory = SourceDirectory.WorldObjects;

            /// <summary>
            /// Default path to the world SQLite database.
            /// Created automatically on first run via <see cref="Data.WorldDatabaseSeeder"/>.
            /// </summary>
            public const string WorldDatabase = SourceDirectory.World + "world.db";

            /// <summary>
            /// Directory for optional SQL script overrides.
            /// Files here take precedence over the embedded SQL resources in the assembly.
            /// When absent, <see cref="SqlScriptLoader"/> falls back to embedded resources.
            /// </summary>
            public const string WorldSqlDirectory = SourceDirectory.WorldSql;
        }

        #endregion Source file paths

        #region Extensions

        /// <summary>File extensions used across source and generated files.</summary>
        public static class Extension
        {
            public const string GeneratedJson = ".json";
            public const string SourceCsv = ".csv";
        }

        #endregion Extensions

        #region Generated directories

        /// <summary>
        /// Directories for runtime-generated character files.
        /// </summary>
        public static class GeneratedDirectory
        {
            private const string root = @"Generated\";
            public const string Npcs = root + @"NPCs\";
            public const string Player = root + @"Player\";
            public const string Root = root;
        }

        #endregion Generated directories
    }

    /// <summary>For test purposes only!</summary>
    public static class FileSystemConstantsForTest
    {
        public const string Npc = FileSystemConstant.GeneratedDirectory.Npcs;
        public const string Pc = FileSystemConstant.GeneratedDirectory.Player;
        public const string Root = FileSystemConstant.GeneratedDirectory.Root;
    }

    /// <summary>
    /// TODO: Remove! For test purposes only!
    /// </summary>
    public static class TestFSConstatns
    {
        public static string gfiles = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\gfiles\";
        public static string NPCs = gfiles + @"NPCs\";
        public static string player = gfiles + @"Player\";
    }
}
