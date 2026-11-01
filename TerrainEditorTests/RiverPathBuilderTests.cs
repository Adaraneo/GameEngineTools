using GameEngineTools.World.Data;
using TerrainEditor.Rendering;

namespace TerrainEditorTests;

[TestClass]
public class RiverPathBuilderTests
{
    private static RiverReach Reach(string id, int order, params (double X, double Y)[] points)
        => new(id, "n", "a", "b", points, order, 1, 1.0);

    [TestMethod]
    public void BuildByOrder_GroupsByOrderAndMapsWorldToCellUnits()
    {
        var reaches = new[]
        {
            Reach("r1", 2, (1000, 2000), (1100, 2000), (1200, 2100)),
            Reach("r2", 2, (1300, 2100), (1400, 2100)),
            Reach("r3", 5, (1000, 2500), (1500, 2500)),
        };

        var paths = RiverPathBuilder.BuildByOrder(reaches, originX: 1000, originY: 2000, cellSizeMeters: 50, widthCells: 100, heightCells: 100);

        CollectionAssert.AreEquivalent(new[] { 2, 5 }, paths.Keys.ToArray());
        Assert.AreEqual(0f, paths[2].Bounds.Left, 1e-4f, "World x of the origin maps to cell 0.");
        Assert.AreEqual(8f, paths[2].Bounds.Right, 1e-3f, "World x 1400 is cell (1400-1000)/50 = 8.");
        Assert.AreEqual(10f, paths[5].Bounds.Top, 1e-4f, "World y 2500 is cell (2500-2000)/50 = 10.");
        foreach (var path in paths.Values) path.Dispose();
    }

    [TestMethod]
    public void BuildByOrder_SkipsReachesOutsideTheGrid()
    {
        var far = Reach("far", 3, (900000, 900000), (900100, 900100));

        var paths = RiverPathBuilder.BuildByOrder(new[] { far }, 1000, 2000, 50, 100, 100);

        Assert.AreEqual(0, paths.Count);
    }
}
