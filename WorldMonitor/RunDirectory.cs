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
        /// <summary>Creates a fresh folder holding copies of the config files and SourceFiles (without any world.db unless <paramref name="worldDb"/> supplies a ready one).</summary>
        public static string Create(string? worldDb = null, bool barter = false, string? planet = null)
        {
            var path = Path.Combine(Path.GetTempPath(), "WorldMonitor", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");
            Directory.CreateDirectory(path);

            // The engine requires appsettings.json as well as the appsettings.<area>.json files.
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "appsettings*.json"))
                File.Copy(file, Path.Combine(path, Path.GetFileName(file)), overwrite: true);

            CopyTree(Path.Combine(AppContext.BaseDirectory, "SourceFiles"), Path.Combine(path, "SourceFiles"));
            Directory.CreateDirectory(Path.Combine(path, "SourceFiles", "World"));
            if (planet is not null)
            {
                try
                {
                    SelectPlanet(Path.Combine(path, "appsettings.World.json"), planet);
                }
                catch
                {
                    TryDelete(path);
                    throw;
                }
            }

            if (barter)
                EnableBarter(Path.Combine(path, "appsettings.Economy.json"));
            if (worldDb is not null)
                File.Copy(worldDb, Path.Combine(path, "SourceFiles", "World", "world.db"), overwrite: true);
            return path;
        }

        private static void SelectPlanet(string file, string planet)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var world = root["World"]?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            world[GameEngineTools.Config.WorldSettings.PlanetKey.Split(':')[1]] = planet;
            root["World"] = world;
            File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GameEngineTools.Config.WorldSettings.Validate(file);
        }

        private static void EnableBarter(string file)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var economy = root["Economy"]?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
            economy["BarterOnly"] = true;
            root["Economy"] = economy;
            File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
