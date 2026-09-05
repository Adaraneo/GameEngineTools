using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class WorldSettingsLoaderTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TerrainEditorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string DbPath => Path.Combine(_tempDir, "world.db");

    [TestMethod]
    public void TryLoadAstroConfig_NoSettingsFileNextToDb_ReturnsNull()
    {
        var result = WorldSettingsLoader.TryLoadAstroConfig(DbPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryLoadAstroConfig_SettingsFilePresent_BindsWorldAstroSection()
    {
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        {
          "World": {
            "Astro": {
              "LatitudeDeg": 42.5,
              "BaseTemperatureCelsius": 7.0,
              "Sun": { "AxialTiltDeg": 19.0 }
            }
          }
        }
        """);

        var result = WorldSettingsLoader.TryLoadAstroConfig(DbPath);

        Assert.IsNotNull(result);
        Assert.AreEqual(42.5, result.LatitudeDeg, 1e-9);
        Assert.AreEqual(7.0, result.BaseTemperatureCelsius, 1e-9);
        Assert.AreEqual(19.0, result.Sun.AxialTiltDeg, 1e-9);
    }

    [TestMethod]
    public void TryLoadAstroConfig_SettingsFileMissingAstroSection_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        { "World": { "Calendar": { "MonthCount": 10 } } }
        """);

        var result = WorldSettingsLoader.TryLoadAstroConfig(DbPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryLoadAstroConfig_ReadsFromDbsFolderNotCwd()
    {
        // A settings file in the current working directory must NOT leak into a database
        // opened from a different, unrelated folder.
        var otherDir = Path.Combine(Path.GetTempPath(), "TerrainEditorTests_Other_" + Guid.NewGuid());
        Directory.CreateDirectory(otherDir);
        try
        {
            File.WriteAllText(Path.Combine(otherDir, WorldSettingsLoader.FileName), """
            { "World": { "Astro": { "LatitudeDeg": 1.0 } } }
            """);

            var result = WorldSettingsLoader.TryLoadAstroConfig(DbPath); // DbPath's folder has no settings file

            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(otherDir, recursive: true);
        }
    }

    [TestMethod]
    public void TryLoadAstroConfig_SettingsFileTwoLevelsAboveDb_IsFound()
    {
        // Mirrors WorldObserver's actual layout: world.db self-seeds at SourceFiles\World\world.db
        // while appsettings.World.json sits at the project root, two directories up.
        var nestedDbFolder = Path.Combine(_tempDir, "SourceFiles", "World");
        Directory.CreateDirectory(nestedDbFolder);
        var nestedDbPath = Path.Combine(nestedDbFolder, "world.db");
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        { "World": { "Astro": { "LatitudeDeg": 50.0 } } }
        """);

        var result = WorldSettingsLoader.TryLoadAstroConfig(nestedDbPath);

        Assert.IsNotNull(result);
        Assert.AreEqual(50.0, result.LatitudeDeg, 1e-9);
    }

    [TestMethod]
    public void TryLoadAstroConfig_SettingsFileBeyondSearchDepth_ReturnsNull()
    {
        // Nest the db deep enough that the settings file at _tempDir falls outside
        // MaxParentDirectoriesToSearch — this must not search indefinitely upward.
        var deepFolder = _tempDir;
        for (var i = 0; i <= WorldSettingsLoader.MaxParentDirectoriesToSearch; i++)
            deepFolder = Path.Combine(deepFolder, $"lvl{i}");
        Directory.CreateDirectory(deepFolder);
        var deepDbPath = Path.Combine(deepFolder, "world.db");
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        { "World": { "Astro": { "LatitudeDeg": 50.0 } } }
        """);

        var result = WorldSettingsLoader.TryLoadAstroConfig(deepDbPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryLoadUniverseConfig_NoSettingsFile_ReturnsNull()
    {
        var result = WorldSettingsLoader.TryLoadUniverseConfig(DbPath);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryLoadUniverseConfig_SettingsFilePresent_BindsWorldUniverseSection()
    {
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        {
          "World": {
            "Universe": {
              "PlanetName": "Vigilia Insectianis",
              "PlanetMassKg": 5.9726E+24,
              "PlanetEquatorialRadiusKm": 6378.1
            }
          }
        }
        """);

        var result = WorldSettingsLoader.TryLoadUniverseConfig(DbPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("Vigilia Insectianis", result.PlanetName);
        Assert.AreEqual(5.9726e24, result.PlanetMassKg, 1e17);
        Assert.AreEqual(6378.1, result.PlanetEquatorialRadiusKm, 1e-6);
    }

    [TestMethod]
    public void TryLoadUniverseConfig_SettingsFileMissingUniverseSection_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, WorldSettingsLoader.FileName), """
        { "World": { "Astro": { "LatitudeDeg": 10.0 } } }
        """);

        var result = WorldSettingsLoader.TryLoadUniverseConfig(DbPath);

        Assert.IsNull(result);
    }
}
