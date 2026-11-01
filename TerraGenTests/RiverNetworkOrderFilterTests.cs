using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class RiverNetworkOrderFilterTests
{
    private static RiverReach Reach(string id, string from, string to, int order)
        => new(id, "n", from, to, new List<(double X, double Y)> { (0, 0), (1, 1) }, order, 1, 1.0);

    private static RiverNetwork Sample()
    {
        var nodes = new[] { "a", "b", "c", "m" }
            .Select(i => new RiverNode(i, "n", 0, 0, RiverNodeKind.Confluence)).ToList();
        // two order-1 headwaters (a, b) join at c, order-2 trunk c -> mouth m
        var reaches = new List<RiverReach> { Reach("r1", "a", "c", 1), Reach("r2", "b", "c", 1), Reach("r3", "c", "m", 2) };
        return new RiverNetwork("n", nodes, reaches, new List<OxbowLoop>());
    }

    [TestMethod]
    public void DropReachesBelowOrder_RemovesHeadwatersAndTheirOrphanedNodes()
    {
        var filtered = RiverMeander.DropReachesBelowOrder(Sample(), 2);

        CollectionAssert.AreEqual(new[] { "r3" }, filtered.Reaches.Select(r => r.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { "c", "m" }, filtered.Nodes.Select(n => n.Id).ToArray());
    }

    [TestMethod]
    public void DropReachesBelowOrder_OrderOne_KeepsEverything()
    {
        var network = Sample();

        Assert.AreSame(network, RiverMeander.DropReachesBelowOrder(network, 1));
    }
}

[TestClass]
public class RiverPolylineSmoothingTests
{
    [TestMethod]
    public void SmoothPolyline_KeepsEndpointsAndFlattensSawtooth()
    {
        var saw = Enumerable.Range(0, 11).Select(i => ((double)i * 50.0, i % 2 == 0 ? 0.0 : 50.0)).ToList();

        var smooth = RiverMeander.SmoothPolyline(saw, 2);

        Assert.AreEqual(saw.Count, smooth.Count);
        Assert.AreEqual(saw[0], smooth[0]);
        Assert.AreEqual(saw[^1], smooth[^1]);
        var interiorAmplitude = smooth.Skip(2).Take(6).Select(p => p.Y).Max() - smooth.Skip(2).Take(6).Select(p => p.Y).Min();
        Assert.IsTrue(interiorAmplitude < 15.0, $"One-cell sawtooth of 50 m should shrink well below that, got {interiorAmplitude:F1} m.");
    }

    [TestMethod]
    public void SmoothPolyline_ZeroPasses_ReturnsInputUnchanged()
    {
        var pts = new List<(double X, double Y)> { (0, 0), (1, 5), (2, 0) };

        Assert.AreSame(pts, RiverMeander.SmoothPolyline(pts, 0));
    }
}
