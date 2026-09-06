using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class RockLayerTests
{
    [TestMethod]
    public void RockPropertiesTable_HasAllNineRockTypes()
    {
        foreach (RockType rockType in Enum.GetValues(typeof(RockType)))
            Assert.IsTrue(RockPropertiesTable.Values.ContainsKey(rockType), $"Missing table entry for {rockType}.");
    }

    [TestMethod]
    public void RockPropertiesTable_ErodibilityKValues_SpanTheCitedFiveOrdersOfMagnitude()
    {
        var kValues = RockPropertiesTable.Values.Values.Select(v => v.ErodibilityK).ToList();
        Assert.IsTrue(kValues.Min() >= 1e-7 * 0.999);
        Assert.IsTrue(kValues.Max() <= 1e-2 * 1.001);
    }

    [TestMethod]
    public void ComputeRockTypeMap_NoPlates_EveryCellDefaultsToContinental()
    {
        var grid = new TerrainHeightmap("test", 0.0, 0.0, 5000.0, 10, 10, new float[10 * 10]);
        var rockTypes = RockLayer.ComputeRockTypeMap(grid, plates: null, refLatDeg: 0, refLonDeg: 0,
            planetRadiusMeters: PlanetNoise.EarthRadiusMeters, p: new RockLayer.Parameters(Seed: 1));

        Assert.IsTrue(rockTypes.All(r => r != RockType.Basalt), "Without plates every cell should default to a continental lithology bucket, never the oceanic-only Basalt.");
    }

    [TestMethod]
    public void ErodibilityKPerCell_MatchesTableLookup()
    {
        var rockTypes = new[] { RockType.Granite, RockType.Shale, RockType.Basalt };
        var k = RockLayer.ErodibilityKPerCell(rockTypes);

        for (var i = 0; i < rockTypes.Length; i++)
            Assert.AreEqual(RockPropertiesTable.Values[rockTypes[i]].ErodibilityK, k[i]);
    }

    [TestMethod]
    public void Erode_WithoutErodibilityOverride_MatchesScalarKBehavior()
    {
        // Byte-identical-when-disabled regression: passing erodibilityPerCell: null must behave
        // exactly like Stage 1 (the scalar Parameters.K everywhere).
        var gridA = MakeGrid(12, 12);
        var gridB = MakeGrid(12, 12);
        var p = new StreamPowerErosion.Parameters(Iterations: 30);
        var uplift = new double[12 * 12];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.002;

        StreamPowerErosion.Erode(gridA, p, uplift, locked: null, erodibilityPerCell: null);
        StreamPowerErosion.Erode(gridB, p, uplift, locked: null,
            erodibilityPerCell: Enumerable.Repeat(p.K, 12 * 12).ToArray());

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Erode_TwoLithologyStrip_SofterRockErodesFasterThanHarder()
    {
        // Task 2.3's differential-erosion smoke test: a synthetic soft/hard strip under uniform
        // uplift should show measurably MORE erosion (lower relief, since erosion caps how high
        // uplift can build terrain at steady state) on the soft side than the hard side.
        const int size = 20;
        var grid = MakeGrid(size, size);
        var uplift = new double[size * size];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.002;

        var softK = RockPropertiesTable.Values[RockType.Schist].ErodibilityK; // highest K in the table
        var hardK = RockPropertiesTable.Values[RockType.Quartzite].ErodibilityK; // lowest K in the table
        var erodibility = new double[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                erodibility[y * size + x] = x < size / 2 ? softK : hardK;

        var p = new StreamPowerErosion.Parameters(Iterations: 300, TimestepYears: 2.5e5);
        StreamPowerErosion.Erode(grid, p, uplift, locked: null, erodibilityPerCell: erodibility);

        double MeanHeight(Func<int, bool> xPredicate)
        {
            var sum = 0.0;
            var count = 0;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    if (xPredicate(x)) { sum += grid.Values[y * size + x]; count++; }
            return sum / count;
        }

        var softMean = MeanHeight(x => x < size / 2);
        var hardMean = MeanHeight(x => x >= size / 2);

        Assert.IsTrue(hardMean > softMean,
            $"Expected the harder rock (lower K) to hold more relief at steady state than the softer rock (higher K) — got soft={softMean:F2}, hard={hardMean:F2}.");
    }

    private static TerrainHeightmap MakeGrid(int width, int height)
        => new("test", 0.0, 0.0, 100.0, width, height, new float[width * height]);
}
