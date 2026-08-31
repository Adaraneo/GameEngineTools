using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class HydraulicErosionTests
{
    private static TerrainHeightmap MakeErodableGrid(int seed = 1)
    {
        var grid = new TerrainHeightmap(
            Id: "test", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 10.0,
            Width: 40, Height: 40, Values: new float[40 * 40]);
        TerrainGenerator.Generate(grid, new TerrainGenerator.Parameters(Seed: seed, AmplitudeMeters: 150.0));
        return grid;
    }

    [TestMethod]
    public void Erode_ZeroDroplets_LeavesGridUnchanged()
    {
        var grid = MakeErodableGrid();
        var before = (float[])grid.Values.Clone();

        HydraulicErosion.Erode(grid, new HydraulicErosion.Parameters(DropletCount: 0));

        CollectionAssert.AreEqual(before, grid.Values);
    }

    [TestMethod]
    public void Erode_TooSmallGrid_DoesNotThrow()
    {
        var grid = new TerrainHeightmap(
            Id: "test", OriginX: 0.0, OriginY: 0.0, CellSizeMeters: 10.0,
            Width: 2, Height: 2, Values: new float[4]);

        HydraulicErosion.Erode(grid, new HydraulicErosion.Parameters(DropletCount: 100));

        // No exception is the assertion — a 2x2 grid has no room for gradient sampling.
    }

    [TestMethod]
    public void Erode_WithDroplets_ChangesTheTerrain()
    {
        var grid = MakeErodableGrid();
        var before = (float[])grid.Values.Clone();

        HydraulicErosion.Erode(grid, new HydraulicErosion.Parameters(Seed: 1, DropletCount: 2000));

        CollectionAssert.AreNotEqual(before, grid.Values);
    }

    [TestMethod]
    public void Erode_SameSeedAndParameters_IsDeterministic()
    {
        var gridA = MakeErodableGrid();
        var gridB = MakeErodableGrid();
        var parameters = new HydraulicErosion.Parameters(Seed: 9, DropletCount: 1500);

        HydraulicErosion.Erode(gridA, parameters);
        HydraulicErosion.Erode(gridB, parameters);

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Erode_AllValuesFiniteAfterErosion()
    {
        var grid = MakeErodableGrid();

        HydraulicErosion.Erode(grid, new HydraulicErosion.Parameters(Seed: 2, DropletCount: 3000));

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v), "Erosion must never produce NaN.");
            Assert.IsFalse(float.IsInfinity(v), "Erosion must never produce Infinity.");
        }
    }

    [TestMethod]
    public void Erode_TotalElevationMass_IsApproximatelyConserved()
    {
        // Erosion redistributes material (erode here, deposit there) rather than creating or
        // destroying it — droplets deposit their remaining sediment before leaving the map or
        // running out of water, so the grid's total sum should stay close to its starting value.
        var grid = MakeErodableGrid();
        var totalBefore = grid.Values.Sum(v => (double)v);

        HydraulicErosion.Erode(grid, new HydraulicErosion.Parameters(Seed: 3, DropletCount: 5000));

        var totalAfter = grid.Values.Sum(v => (double)v);
        var relativeDrift = Math.Abs(totalAfter - totalBefore) / Math.Max(1.0, Math.Abs(totalBefore));
        Assert.IsTrue(relativeDrift < 0.05,
            $"Expected total elevation mass roughly conserved, drifted {relativeDrift:P1} (before={totalBefore}, after={totalAfter}).");
    }
}
