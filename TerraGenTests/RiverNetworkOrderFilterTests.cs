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
