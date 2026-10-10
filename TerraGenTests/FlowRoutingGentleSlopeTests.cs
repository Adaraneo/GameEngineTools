using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class FlowRoutingGentleSlopeTests
{
    // A plane falling toward +x by less than the old 1 mm fill epsilon per cell used to route most
    // cells toward the NEAREST grid edge (a Chebyshev-distance cone), i.e. along 45° diagonals.
    [TestMethod]
    [DataRow(2e-4f)]
    [DataRow(2e-3f)]
    public void Compute_GentleRamp_RoutesDownhillNotTowardNearestEdge(float slopePerCell)
    {
        const int n = 120;
        var values = new float[n * n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                values[y * n + x] = -5f - slopePerCell * x;
        var routing = FlowRouting.Compute(new TerrainHeightmap("ramp", 0, 0, 50.0, n, n, values));

        var downhill = 0;
        var total = 0;
        for (var y = 15; y < n - 15; y++)
            for (var x = 15; x < n - 15; x++)
            {
                total++;
                var next = routing.Downstream[y * n + x];
                if (next >= 0 && next % n - x > 0) downhill++;
            }

        Assert.IsTrue(downhill >= total * 0.99, $"Only {downhill * 100.0 / total:F1}% of cells routed downhill on a {slopePerCell:G3}/cell ramp.");
    }
}
