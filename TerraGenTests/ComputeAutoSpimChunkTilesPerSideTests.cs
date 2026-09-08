using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class ComputeAutoSpimChunkTilesPerSideTests
{
    [TestMethod]
    public void NeverGoesBelowOne()
    {
        var tiles = TileGenerator.ComputeAutoSpimChunkTilesPerSide(desiredTilesPerSide: 50, cellsPerTile: 400, availableMemoryBytes: 1024);
        Assert.AreEqual(1, tiles);
    }

    [TestMethod]
    public void NeverExceedsTheDesiredSize_EvenWithHugeMemory()
    {
        var tiles = TileGenerator.ComputeAutoSpimChunkTilesPerSide(desiredTilesPerSide: 23, cellsPerTile: 400, availableMemoryBytes: long.MaxValue / 2);
        Assert.AreEqual(23, tiles);
    }

    [TestMethod]
    public void ShrinksBelowDesired_WhenMemoryIsTight()
    {
        // The user's real region: ~23 tiles/side wanted, 400 cells/tile -- with only 2 GB available
        // (well under the ~4.4 GB a 23x23 chunk needs at 150 B/cell), auto must shrink it.
        var tiles = TileGenerator.ComputeAutoSpimChunkTilesPerSide(desiredTilesPerSide: 23, cellsPerTile: 400, availableMemoryBytes: 2_000_000_000);
        Assert.IsTrue(tiles < 23, $"Expected a shrunk chunk size under a 2GB budget, got {tiles}.");
        Assert.IsTrue(tiles >= 1);
    }

    [TestMethod]
    public void ScalesUpWithMoreAvailableMemory()
    {
        var small = TileGenerator.ComputeAutoSpimChunkTilesPerSide(50, 400, availableMemoryBytes: 1_000_000_000);
        var large = TileGenerator.ComputeAutoSpimChunkTilesPerSide(50, 400, availableMemoryBytes: 8_000_000_000);
        Assert.IsTrue(large >= small, $"Expected more memory to allow an equal-or-larger chunk, got small={small}, large={large}.");
    }

    [TestMethod]
    public void ZeroCellsPerTile_FallsBackToDesiredWithoutDividingByZero()
    {
        var tiles = TileGenerator.ComputeAutoSpimChunkTilesPerSide(desiredTilesPerSide: 10, cellsPerTile: 0, availableMemoryBytes: 1_000_000_000);
        Assert.AreEqual(10, tiles);
    }
}
