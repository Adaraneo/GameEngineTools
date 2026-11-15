// DefaultConfigFilesTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Weather;
    using Microsoft.Extensions.Configuration;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// The <c>*.Default.json</c> twins hold the defaults of the config types: every setting of a config type must be there, with the C# default as its value.
    /// A new setting added to a live <c>appsettings.*.json</c> is added to the twins as well, as the default of the C# field.
    /// </summary>
    [TestClass]
    public sealed class DefaultConfigFilesTests
    {
        private static readonly string[] CharacterFiles =
        {
            "EngineTests", "GameSandbox", "WorldObserver", "CharacterGenerator", "UnityPoC/Server"
        };

        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EngineTests", "appsettings.Characters.Default.json")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }

        private static T Defaults<T>() where T : class
            => (T)Activator.CreateInstance(typeof(T), System.Reflection.BindingFlags.CreateInstance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.OptionalParamBinding, null, Array.Empty<object>(), null)!;

        private static List<string> Mismatches<T>(string file, string section) where T : class
        {
            var cfg = new ConfigurationBuilder().AddJsonFile(file, optional: false).Build();
            var node = cfg.GetSection(section);
            var bound = node.Get<T>() ?? Defaults<T>();
            var defaults = Defaults<T>();
            var keys = node.GetChildren().Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();

            foreach (var p in typeof(T).GetProperties().Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType == typeof(double?) || p.PropertyType == typeof(int?)))
            {
                if (!keys.Contains(p.Name))
                {
                    problems.Add($"{file}: {section}:{p.Name} is missing");
                    continue;
                }

                var a = p.GetValue(bound);
                var b = p.GetValue(defaults);
                var same = a is double x && b is double y ? Math.Abs(x - y) <= 1e-12 * Math.Max(1.0, Math.Abs(y)) : Equals(a, b);
                if (!same) problems.Add($"{file}: {section}:{p.Name} is {a}, the C# default is {b}");
            }

            return problems;
        }


        [TestMethod]
        public void CharactersDefaultFiles_HoldEveryPhysiologyAndBehaviorSetting_AtTheirCSharpDefault()
        {
            var root = Root();
            var problems = new List<string>();
            foreach (var proj in CharacterFiles)
            {
                var file = Path.Combine(root, proj, "appsettings.Characters.Default.json");
                problems.AddRange(Mismatches<PhysiologyConfig>(file, "Characters:Physiology"));
                problems.AddRange(Mismatches<BehaviorConfig>(file, "Characters:Behavior"));
            }

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems.Take(25)) + $"{Environment.NewLine}({problems.Count} in all)");
        }

        [TestMethod]
        public void WorldDefaultFiles_HoldTheWeatherSettings_AtTheirCSharpDefault()
        {
            var root = Root();
            var problems = new List<string>();
            foreach (var proj in CharacterFiles)
                problems.AddRange(Mismatches<WeatherConfig>(Path.Combine(root, proj, "appsettings.World.Default.json"), "World:Weather"));

            Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems.Take(25)));
        }
    }
}
