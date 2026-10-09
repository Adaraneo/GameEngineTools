using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class StableTileSeedTests
{
    [TestMethod]
    public void StableTileSeed_IsProcessIndependent_PinnedValue()
    {
        // FNV-1a over little-endian (seed,row,col) bytes; a pinned constant catches any switch back to
        // a per-process salted hash (HashCode.Combine), which no same-process comparison can.
        Assert.AreEqual(787902027, TileGenerator.StableTileSeed(42, 3, 7));
    }

    [TestMethod]
    public void StableTileSeed_DiffersPerRowAndCol()
    {
        var a = TileGenerator.StableTileSeed(42, 3, 7);
        Assert.AreNotEqual(a, TileGenerator.StableTileSeed(42, 7, 3));
        Assert.AreNotEqual(a, TileGenerator.StableTileSeed(42, 3, 8));
        Assert.AreNotEqual(a, TileGenerator.StableTileSeed(43, 3, 7));
    }
}
