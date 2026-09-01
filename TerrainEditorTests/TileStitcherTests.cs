using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class TileStitcherTests
{
    private static TerrainHeightmap MakeTile(string id, double originX, double originY, float fill,
        int size = 4, double cellSize = 10.0, byte[]? riverMask = null)
        => new(id, originX, originY, cellSize, size, size, Enumerable.Repeat(fill, size * size).ToArray(), riverMask);

    private static TerrainHeightmapSummary SummaryOf(TerrainHeightmap t)
        => new(t.Id, t.OriginX, t.OriginY, t.CellSizeMeters, t.Width, t.Height);

    [TestMethod]
    public void BuildCombinedGrid_NoOverlap_ReturnsNull()
    {
        var tile = MakeTile("a", 0, 0, 5f);
        var (combined, sources) = TileStitcher.BuildCombinedGrid([SummaryOf(tile)], id => tile, 1000, 1000, 1100, 1100);

        Assert.IsNull(combined);
        Assert.AreEqual(0, sources.Count);
    }

    [TestMethod]
    public void BuildCombinedGrid_SingleOverlappingTile_ReturnsItUnchanged()
    {
        var tile = MakeTile("a", 0, 0, 5f);
        var (combined, sources) = TileStitcher.BuildCombinedGrid([SummaryOf(tile)], id => tile, -10, -10, 10, 10);

        Assert.IsNotNull(combined);
        Assert.AreEqual("a", combined!.Id);
        Assert.AreSame(tile, combined);
        Assert.AreEqual(1, sources.Count);
        Assert.AreSame(tile, sources[0]);
    }

    [TestMethod]
    public void BuildCombinedGrid_TwoAdjacentTiles_StitchesIntoOneContinuousGrid()
    {
        // Two 4x4 tiles at 10m cells, side by side along X (west at 0, east at 40).
        var west = MakeTile("west", 0, 0, 1f);
        var east = MakeTile("east", 40, 0, 2f);
        var summaries = new[] { SummaryOf(west), SummaryOf(east) };

        var (combined, sources) = TileStitcher.BuildCombinedGrid(summaries, id => id == "west" ? west : east, -5, -5, 75, 45);

        Assert.IsNotNull(combined);
        Assert.AreEqual("combined", combined!.Id);
        Assert.AreEqual(0.0, combined.OriginX, 1e-9);
        Assert.AreEqual(0.0, combined.OriginY, 1e-9);
        Assert.AreEqual(8, combined.Width); // 4 + 4 cells wide
        Assert.AreEqual(4, combined.Height);
        Assert.AreEqual(2, sources.Count);

        // West half should read 1, east half should read 2.
        Assert.AreEqual(1.0, combined.SampleAt(5, 5), 1e-6);
        Assert.AreEqual(2.0, combined.SampleAt(45, 5), 1e-6);
    }

    [TestMethod]
    public void BuildCombinedGrid_MismatchedCellSize_FallsBackToFirstTile()
    {
        var a = MakeTile("a", 0, 0, 1f, cellSize: 10.0);
        var b = MakeTile("b", 40, 0, 2f, cellSize: 5.0);
        var summaries = new[] { SummaryOf(a), SummaryOf(b) };

        var (combined, sources) = TileStitcher.BuildCombinedGrid(summaries, id => id == "a" ? a : b, -5, -5, 75, 45);

        Assert.IsNotNull(combined);
        Assert.AreEqual("a", combined!.Id); // bailed out rather than mis-stitching
        Assert.AreEqual(1, sources.Count);
        Assert.AreEqual("a", sources[0].Id);
    }

    [TestMethod]
    public void BuildCombinedGrid_RiverMask_PreservedFromContributingTiles()
    {
        var westMask = new byte[16]; // 4x4
        westMask[5] = 1;
        var west = MakeTile("west", 0, 0, 1f, riverMask: westMask);
        var east = MakeTile("east", 40, 0, 2f); // no river mask

        var summaries = new[] { SummaryOf(west), SummaryOf(east) };
        var (combined, _) = TileStitcher.BuildCombinedGrid(summaries, id => id == "west" ? west : east, -5, -5, 75, 45);

        Assert.IsNotNull(combined);
        Assert.IsNotNull(combined!.RiverMask);
        Assert.IsTrue(combined.IsRiver(1, 1)); // west tile's river cell (index 5 = row1,col1 in a 4-wide tile)
        Assert.IsFalse(combined.IsRiver(5, 1)); // east half had no mask
    }

    [TestMethod]
    public void BuildCombinedGrid_ThreeTilesInAnLShape_CoversAllOfThem()
    {
        var a = MakeTile("a", 0, 0, 1f);
        var b = MakeTile("b", 40, 0, 2f);
        var c = MakeTile("c", 0, 40, 3f);
        var summaries = new[] { SummaryOf(a), SummaryOf(b), SummaryOf(c) };
        TerrainHeightmap Load(string id) => id switch { "a" => a, "b" => b, _ => c };

        var (combined, sources) = TileStitcher.BuildCombinedGrid(summaries, Load, -5, -5, 75, 75);

        Assert.IsNotNull(combined);
        Assert.AreEqual(8, combined!.Width);
        Assert.AreEqual(8, combined.Height);
        Assert.AreEqual(3, sources.Count);
        Assert.AreEqual(1.0, combined.SampleAt(5, 5), 1e-6);
        Assert.AreEqual(2.0, combined.SampleAt(45, 5), 1e-6);
        Assert.AreEqual(3.0, combined.SampleAt(5, 45), 1e-6);
    }

    [TestMethod]
    public void SplitAndSave_TwoAdjacentTiles_RoundTripsEachTilesOriginalValues()
    {
        var west = MakeTile("west", 0, 0, 1f);
        var east = MakeTile("east", 40, 0, 2f);
        var summaries = new[] { SummaryOf(west), SummaryOf(east) };
        var (combined, sources) = TileStitcher.BuildCombinedGrid(summaries, id => id == "west" ? west : east, -5, -5, 75, 45);

        var saved = new Dictionary<string, TerrainHeightmap>();
        TileStitcher.SplitAndSave(combined!, sources, t => saved[t.Id] = t);

        Assert.AreEqual(2, saved.Count);
        CollectionAssert.AreEqual(west.Values, saved["west"].Values);
        CollectionAssert.AreEqual(east.Values, saved["east"].Values);
        Assert.AreEqual(west.OriginX, saved["west"].OriginX, 1e-9);
        Assert.AreEqual(east.OriginX, saved["east"].OriginX, 1e-9);
    }

    [TestMethod]
    public void SplitAndSave_EditInWestTile_OnlyAffectsWestTileOnSplit()
    {
        var west = MakeTile("west", 0, 0, 1f);
        var east = MakeTile("east", 40, 0, 2f);
        var summaries = new[] { SummaryOf(west), SummaryOf(east) };
        var (combined, sources) = TileStitcher.BuildCombinedGrid(summaries, id => id == "west" ? west : east, -5, -5, 75, 45);

        // Simulate an edit (e.g. painting) somewhere inside the west tile's region of the combined grid.
        var edited = combined! with { Values = (float[])combined.Values.Clone() };
        edited.Values[0] = 99f; // combined index 0 = west tile's cell (0,0)

        var saved = new Dictionary<string, TerrainHeightmap>();
        TileStitcher.SplitAndSave(edited, sources, t => saved[t.Id] = t);

        Assert.AreEqual(99f, saved["west"].Values[0]);
        CollectionAssert.AreEqual(east.Values, saved["east"].Values); // untouched
    }

    [TestMethod]
    public void SplitAndSave_SingleTileSource_RoundTripsUnchanged()
    {
        var tile = MakeTile("solo", 0, 0, 7f);
        var (combined, sources) = TileStitcher.BuildCombinedGrid([SummaryOf(tile)], id => tile, -10, -10, 10, 10);

        var saved = new List<TerrainHeightmap>();
        TileStitcher.SplitAndSave(combined!, sources, saved.Add);

        Assert.AreEqual(1, saved.Count);
        Assert.AreEqual("solo", saved[0].Id);
        CollectionAssert.AreEqual(tile.Values, saved[0].Values);
    }
}
