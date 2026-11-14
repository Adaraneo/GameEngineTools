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
        public static string Create(string? worldDb = null, bool barter = false, string? planet = null, IReadOnlyList<string>? overrides = null)
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
            try
            {
                foreach (var setting in overrides ?? Array.Empty<string>())
                    ApplyOverride(path, setting);
            }
            catch
            {
                TryDelete(path);
                throw;
            }

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

        /// <summary>Sets one value of the run's own copy of the config files: "Characters:Physiology:PloughExertion=1" edits appsettings.Characters.json.</summary>
        private static void ApplyOverride(string runDir, string setting)
        {
            var equals = setting.IndexOf('=');
            if (equals <= 0)
                throw new InvalidOperationException($"Override '{setting}' is not of the form Section:Key=value.");

            var keys = setting[..equals].Split(':');
            var file = Path.Combine(runDir, $"appsettings.{keys[0]}.json");
            if (keys.Length < 2 || !File.Exists(file))
                throw new InvalidOperationException($"Override '{setting}': there is no file appsettings.{keys[0]}.json (use Characters, Economy or World as the first part).");

            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var node = root;
            for (var i = 0; i < keys.Length - 1; i++)
            {
                if (node[keys[i]] is not System.Text.Json.Nodes.JsonObject child)
                {
                    child = new System.Text.Json.Nodes.JsonObject();
                    node[keys[i]] = child;
                }

                node = child;
            }

            var leaf = keys[^1];
            if (!node.ContainsKey(leaf))
                Console.Error.WriteLine($"Warning: '{setting[..equals]}' is not in {Path.GetFileName(file)}; it is added (a typo would be ignored by the engine).");

            var text = setting[(equals + 1)..];
            System.Text.Json.Nodes.JsonNode? value;
            try
            {
                value = System.Text.Json.Nodes.JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                value = text;
            }

            node[leaf] = value;
            File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
