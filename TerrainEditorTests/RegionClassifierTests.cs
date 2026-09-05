using GameEngineTools.World.Data;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class RegionClassifierTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, Func<int, int, float> heightAt, byte[]? riverMask = null)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = heightAt(x, y);

        return new TerrainHeightmap(
            Id: "test", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 10.0,
            Width: width, Height: height, Values: values, RiverMask: riverMask);
    }

    private static readonly RegionClassifier.Parameters DefaultParams = new();

    [TestMethod]
    public void Classify_HighElevation_ReturnsMountains()
    {
        var grid = MakeGrid(20, 20, (_, _) => 400f);

        var region = RegionClassifier.Classify(grid, 100, 100, DefaultParams);

        Assert.AreEqual("Mountains", region);
    }

    [TestMethod]
    public void Classify_ModerateElevation_ReturnsHills()
    {
        var grid = MakeGrid(20, 20, (_, _) => 150f);

        var region = RegionClassifier.Classify(grid, 100, 100, DefaultParams);

        Assert.AreEqual("Hills", region);
    }

    [TestMethod]
    public void Classify_LowElevation_ReturnsLowlands()
    {
        var grid = MakeGrid(20, 20, (_, _) => 20f);

        var region = RegionClassifier.Classify(grid, 100, 100, DefaultParams);

        Assert.AreEqual("Lowlands", region);
    }

    [TestMethod]
    public void Classify_BelowSeaLevel_ReturnsCoast()
    {
        var grid = MakeGrid(20, 20, (_, _) => -5f);

        var region = RegionClassifier.Classify(grid, 100, 100, DefaultParams);

        Assert.AreEqual("Coast", region);
    }

    [TestMethod]
    public void Classify_NearRiverButAboveSeaLevel_ReturnsRiverside()
    {
        const int width = 20, gridHeight = 20;
        var mask = new byte[width * gridHeight];
        mask[10 * width + 11] = 1; // one river cell right next to the query point at (10,10)

        var grid = MakeGrid(width, gridHeight, (_, _) => 50f, mask);

        var region = RegionClassifier.Classify(grid, 100, 100, DefaultParams); // (gx=10, gy=10)

        Assert.AreEqual("Riverside", region);
    }

    [TestMethod]
    public void Classify_FarFromRiver_DoesNotReturnRiverside()
    {
        const int width = 30, gridHeight = 30;
        var mask = new byte[width * gridHeight];
        mask[0] = 1; // river far in the corner

        var grid = MakeGrid(width, gridHeight, (_, _) => 50f, mask);

        var region = RegionClassifier.Classify(grid, 200, 200, DefaultParams); // (gx=20, gy=20), far away

        Assert.AreNotEqual("Riverside", region);
    }
}
