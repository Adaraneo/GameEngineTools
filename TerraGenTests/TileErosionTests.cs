using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TileErosionTests
{
    private static TerrainHeightmap MakeGrid(int width, int height, double cellSize, Func<int, int, float> seedFn)
    {
        var values = new float[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                values[y * width + x] = seedFn(x, y);
        return new TerrainHeightmap("test", 0.0, 0.0, cellSize, width, height, values);
    }

    [TestMethod]
    public void Erode_NonFiniteInputHeight_ThrowsActionableExceptionInsteadOfCrashingInSampleHeight()
    {
        // Regression for a reported crash: a NaN/Infinity height already in the grid (e.g. from an
        // upstream SPIM divergence) used to corrupt a droplet's position into NaN, which then failed
        // every bounds check silently (NaN comparisons are always false) and crashed deep inside
        // SampleHeight with an inscrutable IndexOutOfRangeException. This must fail loud up front instead.
        const int size = 20;
        var grid = MakeGrid(size, size, 1.0, (x, y) => (x + y) * 1.5f);
        grid.Values[size * size / 2] = float.NaN;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TileErosion.Erode(grid, new TileErosion.Parameters(Seed: 1, DropletCount: 1000)));

        StringAssert.Contains(ex.Message, "non-finite");
    }

    [TestMethod]
    public void Erode_LockedCells_AreNeverWritten()
    {
        const int size = 20;
        var grid = MakeGrid(size, size, 5.0, (x, y) => (x + y) * 3.7f);
        var before = (float[])grid.Values.Clone();

        var locked = new bool[size * size];
        for (var i = 0; i < locked.Length; i++) locked[i] = true;
        // Leave exactly one cell (away from any edge) unlocked.
        const int freeX = 10, freeY = 10;
        locked[freeY * size + freeX] = false;

        TileErosion.Erode(grid, new TileErosion.Parameters(Seed: 1, DropletCount: 5000, MaxDropletLifetime: 20), locked);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var idx = y * size + x;
                if (x == freeX && y == freeY) continue;
                Assert.AreEqual(before[idx], grid.Values[idx], 1e-6f,
                    $"Locked cell ({x},{y}) changed even though it was locked.");
            }
        }
    }

    [TestMethod]
    public void Erode_NoLockedCells_BehavesLikeUnlockedErosion()
    {
        var gridA = MakeGrid(30, 30, 5.0, (x, y) => (x * x + y * y) * 0.05f);
        var gridB = MakeGrid(30, 30, 5.0, (x, y) => (x * x + y * y) * 0.05f);
        var p = new TileErosion.Parameters(Seed: 4, DropletCount: 3000);

        TileErosion.Erode(gridA, p, locked: null);
        TileErosion.Erode(gridB, p, locked: new bool[30 * 30]); // all false == equivalent to null

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Erode_AllValuesFiniteAfterErosion()
    {
        var grid = MakeGrid(25, 25, 4.0, (x, y) => (x - y) * 1.3f);
        TileErosion.Erode(grid, new TileErosion.Parameters(Seed: 2, DropletCount: 4000));

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v));
            Assert.IsFalse(float.IsInfinity(v));
        }
    }
}
