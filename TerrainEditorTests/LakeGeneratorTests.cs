using GameEngineTools.World.Data;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class LakeGeneratorTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, Func<int, int, float> heightAt)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = heightAt(x, y);

        return new TerrainHeightmap(
            Id: "test", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 10.0,
            Width: width, Height: height, Values: values, RiverMask: new byte[width * height]);
    }

    [TestMethod]
    public void Generate_SingleBowl_FloodsCellsNearTheMinimum()
    {
        // Paraboloid bowl, minimum (0) at the center (15,15), well above sea level everywhere.
        var grid = MakeGrid(31, 31, (x, y) => 50f + (x - 15) * (x - 15) + (y - 15) * (y - 15));

        var count = LakeGenerator.Generate(grid, new LakeGenerator.Parameters(MaxDepthMeters: 20.0));

        Assert.AreEqual(1, count);
        Assert.IsTrue(grid.IsRiver(15, 15), "Expected the basin's minimum to be flooded.");
        // Far up the bowl's rim, well beyond MaxDepthMeters, must stay dry.
        Assert.IsFalse(grid.IsRiver(0, 0));
    }

    [TestMethod]
    public void Generate_RespectsMaxDepth_DoesNotFloodBeyondTheCap()
    {
        var grid = MakeGrid(31, 31, (x, y) => 50f + (x - 15) * (x - 15) + (y - 15) * (y - 15));

        LakeGenerator.Generate(grid, new LakeGenerator.Parameters(MaxDepthMeters: 5.0, MaxCellsPerLake: 10000));

        // With a 5m depth cap, only cells within height 50..55 should ever be considered, i.e.
        // (x-15)^2+(y-15)^2 <= 5 — a small disc around the center. A cell far outside that must
        // never be flooded even though MaxCellsPerLake is effectively unlimited.
        Assert.IsFalse(grid.IsRiver(15, 20), "(15,20) is 25 units of squared-distance out — well past the 5m depth cap.");
    }

    [TestMethod]
    public void Generate_BasinAlreadyBelowSeaLevel_IsNotTreatedAsALake()
    {
        // A depression that's already underwater (ocean trench) — shouldn't be "lake"-ified.
        var grid = MakeGrid(21, 21, (x, y) => -50f + (x - 10) * (x - 10) + (y - 10) * (y - 10));

        var count = LakeGenerator.Generate(grid, new LakeGenerator.Parameters());

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void Generate_RespectsMaxLakesLimit()
    {
        // A 4x4 arrangement of small independent bowls, each a local minimum on its own.
        var grid = MakeGrid(41, 41, (x, y) =>
        {
            var cx = ((x / 10) * 10) + 5;
            var cy = ((y / 10) * 10) + 5;
            return 20f + (x - cx) * (x - cx) + (y - cy) * (y - cy);
        });

        var count = LakeGenerator.Generate(grid, new LakeGenerator.Parameters(MaxLakes: 2, MinBasinSeparationCells: 3.0));

        Assert.IsTrue(count <= 2, $"Expected at most 2 lakes, got {count}.");
    }

    [TestMethod]
    public void Generate_ProtectedLocationAtBasinMinimum_IsNotFlooded()
    {
        var grid = MakeGrid(31, 31, (x, y) => 50f + (x - 15) * (x - 15) + (y - 15) * (y - 15));
        var minimumWorldPos = (grid.OriginX + 15 * grid.CellSizeMeters, grid.OriginY + 15 * grid.CellSizeMeters);

        var count = LakeGenerator.Generate(grid, new LakeGenerator.Parameters(),
            protectedLocations: [minimumWorldPos]);

        Assert.AreEqual(0, count, "The only basin's minimum is a protected location — no lake should form there.");
        Assert.IsFalse(grid.IsRiver(15, 15));
    }

    [TestMethod]
    public void Generate_NoRiverMaskAllocated_Throws()
    {
        var grid = MakeGrid(10, 10, (_, _) => 0f) with { RiverMask = null };

        Assert.Throws<InvalidOperationException>(() => LakeGenerator.Generate(grid, new LakeGenerator.Parameters()));
    }
}
