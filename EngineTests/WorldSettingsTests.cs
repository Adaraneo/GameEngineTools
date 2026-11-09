// WorldSettingsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Config;
    using GameEngineTools.World.Core.Astro;
    using Microsoft.Extensions.Configuration;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.IO;

    /// <summary>World:Planet layers appsettings.World.&lt;Planet&gt;.json over appsettings.World.json.</summary>
    [TestClass]
    public sealed class WorldSettingsTests
    {
        private static string BaseFile => Path.Combine(AppContext.BaseDirectory, WorldSettings.BaseFileName);

        [TestMethod]
        public void TheShippedBaseFile_SelectsVigilia_WhichIsA26HourDay()
        {
            var config = WorldSettings.Load(BaseFile);

            var universe = config.GetSection("World:Universe").Get<UniverseConfig>()!;
            Assert.AreEqual("Vigilia Insectianis", universe.PlanetName);
            Assert.AreEqual(26.0, universe.PlanetSiderealRotationHrs, 1e-9);
            Assert.AreEqual(10, config.GetSection("World:Calendar").Get<CalendarConfig>()!.MonthCount);
        }

        [TestMethod]
        public void SwitchingThePlanetKey_LoadsEarth_WithoutTouchingTheOtherSections()
        {
            var dir = CopyShippedFiles();
            File.WriteAllText(Path.Combine(dir, WorldSettings.BaseFileName), File.ReadAllText(Path.Combine(dir, WorldSettings.BaseFileName)).Replace("\"Vigilia\"", "\"Earth\""));

            var config = WorldSettings.Load(Path.Combine(dir, WorldSettings.BaseFileName));

            var universe = config.GetSection("World:Universe").Get<UniverseConfig>()!;
            Assert.AreEqual("Earth", universe.PlanetName);
            Assert.AreEqual(23.9345, universe.PlanetSiderealRotationHrs, 1e-9);
            Assert.AreEqual(12, config.GetSection("World:Calendar").Get<CalendarConfig>()!.MonthCount);
            Assert.IsNotNull(config["World:Perception:MaxLocalOnlyTargets"], "the base file's own sections stay in place");
        }

        [TestMethod]
        public void AnUnknownPlanet_FailsAndListsTheAvailableOnes()
        {
            var dir = CopyShippedFiles();
            File.WriteAllText(Path.Combine(dir, WorldSettings.BaseFileName), "{ \"World\": { \"Planet\": \"Narnia\" } }");

            var ex = Assert.ThrowsExactly<FileNotFoundException>(() => WorldSettings.Load(Path.Combine(dir, WorldSettings.BaseFileName)));

            StringAssert.Contains(ex.Message, "Narnia");
            StringAssert.Contains(ex.Message, "Earth");
            StringAssert.Contains(ex.Message, "Vigilia");
        }

        [TestMethod]
        public void APlanetNameWithAPath_IsRejected()
        {
            var dir = CopyShippedFiles();
            File.WriteAllText(Path.Combine(dir, WorldSettings.BaseFileName), "{ \"World\": { \"Planet\": \"../Evil\" } }");

            Assert.ThrowsExactly<InvalidOperationException>(() => WorldSettings.Load(Path.Combine(dir, WorldSettings.BaseFileName)));
        }

        [TestMethod]
        public void WithoutThePlanetKey_TheBaseFileStandsAlone()
        {
            var dir = CopyShippedFiles();
            File.WriteAllText(Path.Combine(dir, WorldSettings.BaseFileName), "{ \"World\": { \"Universe\": { \"PlanetName\": \"Inline\" } } }");

            var config = WorldSettings.Load(Path.Combine(dir, WorldSettings.BaseFileName));

            Assert.AreEqual("Inline", config["World:Universe:PlanetName"]);
        }

        [TestMethod]
        public void AvailablePlanets_ListsTheShippedWorlds_AndIgnoresTheDefaultsTwin()
        {
            CollectionAssert.AreEqual(new[] { "Earth", "Vigilia" }, WorldSettings.AvailablePlanets(BaseFile).ToArray());
        }

        [TestMethod]
        public void APlanetFileNextToTheBase_OverridesTheShippedOne()
        {
            var dir = CopyShippedFiles();
            File.WriteAllText(Path.Combine(dir, "appsettings.World.Vigilia.json"), "{ \"World\": { \"Universe\": { \"PlanetName\": \"Custom\" } } }");

            var config = WorldSettings.Load(Path.Combine(dir, WorldSettings.BaseFileName));

            Assert.AreEqual("Custom", config["World:Universe:PlanetName"]);
        }

        private static string CopyShippedFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "WorldSettingsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "appsettings.World*.json"))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            return dir;
        }
    }
}
