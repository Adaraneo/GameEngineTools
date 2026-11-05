// RunDirectory.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// A private working directory per run. The engine keeps its world database at a fixed path under the working directory,
    /// so runs that shared one folder would delete and rewrite each other's world.
    /// </summary>
    public static class RunDirectory
    {
        /// <summary>Creates a fresh folder holding copies of the config files and SourceFiles (without any world.db).</summary>
        public static string Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "WorldMonitor", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
            Directory.CreateDirectory(path);

            // The engine requires appsettings.json as well as the appsettings.<area>.json files.
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "appsettings*.json"))
                File.Copy(file, Path.Combine(path, Path.GetFileName(file)), overwrite: true);

            CopyTree(Path.Combine(AppContext.BaseDirectory, "SourceFiles"), Path.Combine(path, "SourceFiles"));
            Directory.CreateDirectory(Path.Combine(path, "SourceFiles", "World"));
            return path;
        }

        /// <summary>Removes the folder; SQLite may still hold the database for a moment, so it retries.</summary>
        public static void TryDelete(string path)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    Directory.Delete(path, recursive: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(200);
                }
            }
        }

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                if (Path.GetFileName(file).StartsWith("world.db", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            }

            foreach (var directory in Directory.GetDirectories(from))
                CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }
}
