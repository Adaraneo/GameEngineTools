// SqlScriptLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using GameEngineTools.Constants;

    /// <summary>
    /// Loads SQL script files with a two-tier resolution strategy:
    /// <list type="number">
    ///   <item>
    ///     <b>Disk override</b> — looks for the file at
    ///     <c>SourceFiles\World\SQL\{filename}</c>. When present, this version
    ///     is used, allowing game designers to customise world data without
    ///     recompiling the engine.
    ///   </item>
    ///   <item>
    ///     <b>Embedded resource fallback</b> — if no disk file is found, loads
    ///     the script embedded in <c>GameEngineTools.dll</c> at
    ///     <c>GameEngineTools.World.Data.SQL.{filename}</c>. This guarantees
    ///     the engine always has a valid schema and seed data available.
    ///   </item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// SQL files must be added to <c>GameEngineTools.csproj</c> as
    /// <c>&lt;EmbeddedResource&gt;</c> items to be available as fallbacks.
    /// </remarks>
    internal static class SqlScriptLoader
    {
        #region Constants

        /// <summary>
        /// Root namespace prefix for embedded SQL resources.
        /// Must match the folder path: <c>World/Data/SQL/</c> inside the project.
        /// </summary>
        private const string EmbeddedResourcePrefix = "GameEngineTools.World.Data.SQL.";

        #endregion Constants

        #region Public API

        /// <summary>
        /// Loads a SQL script by filename.
        /// Checks the disk override location first; falls back to embedded resource.
        /// </summary>
        /// <param name="filename">
        /// Script filename including extension, e.g. <c>"schema.sql"</c> or
        /// <c>"seed_data.sql"</c>.
        /// </param>
        /// <returns>Full SQL script content as a UTF-8 string.</returns>
        /// <exception cref="FileNotFoundException">
        /// Thrown when the script is not found on disk and is also absent from
        /// the embedded resources (i.e. was not added to the .csproj).
        /// </exception>
        public static string Load(string filename)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filename);

            // ── Tier 1: Disk override ─────────────────────────────────────────
            var diskPath = Path.Combine(FileSystemConstant.SourceFilePath.WorldSqlDirectory, filename);

            if (File.Exists(diskPath))
                return File.ReadAllText(diskPath, Encoding.UTF8);

            // ── Tier 2: Embedded resource fallback ────────────────────────────
            var resourceName = EmbeddedResourcePrefix + filename;
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
                throw new FileNotFoundException(
                    $"SQL script '{filename}' was not found at '{diskPath}' " +
                    $"and is not embedded in the assembly as '{resourceName}'. " +
                    $"Add it to GameEngineTools.csproj as <EmbeddedResource>.");

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Returns <c>true</c> when a disk override for the given filename exists.
        /// Useful for logging which source was used during startup diagnostics.
        /// </summary>
        /// <param name="filename">Script filename including extension.</param>
        public static bool HasDiskOverride(string filename)
        {
            var diskPath = Path.Combine(FileSystemConstant.SourceFilePath.WorldSqlDirectory, filename);
            return File.Exists(diskPath);
        }

        #endregion Public API
    }
}
