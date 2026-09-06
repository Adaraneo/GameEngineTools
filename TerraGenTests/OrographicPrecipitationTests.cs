using GameEngineTools.World.Data;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class OrographicPrecipitationTests
{
    private static TerrainHeightmap MakeFlatGrid(int size, double cellSize)
        => new("test", 0.0, 0.0, cellSize, size, size, new float[size * size]);

    private static TerrainHeightmap MakeSingleRidgeGrid(int size, double cellSize, float peakHeight)
    {
        var values = new float[size * size];
        var centerX = size / 2.0;
        var sigma = size / 6.0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // A ridge running along Y, centered in X — ridge crest perpendicular to an
                // east/west (x-axis) wind so windward/leeward falls cleanly on either side of it.
                var dx = x - centerX;
                values[y * size + x] = (float)(peakHeight * Math.Exp(-(dx * dx) / (2 * sigma * sigma)));
            }
        }
        return new TerrainHeightmap("test", 0.0, 0.0, cellSize, size, size, values);
    }

    [TestMethod]
    public void ComputePrecipitationField_FlatTerrain_ReturnsExactlyUniform()
    {
        var grid = MakeFlatGrid(32, 100.0);
        var field = OrographicPrecipitation.ComputePrecipitationField(grid, new OrographicPrecipitation.Parameters());

        Assert.IsTrue(field.All(v => Math.Abs(v - 1.0) < 1e-9), "Flat terrain has no perturbation to speak of — every cell should normalize to exactly 1.0.");
    }

    [TestMethod]
    public void ComputePrecipitationField_AlwaysPositive_EvenAtExtremeNormalizedPerturbation()
    {
        var grid = MakeSingleRidgeGrid(32, 100.0, peakHeight: 3000f);
        var field = OrographicPrecipitation.ComputePrecipitationField(grid, new OrographicPrecipitation.Parameters());

        Assert.IsTrue(field.All(v => v is >= 0.05 and <= 3.0), "Field must stay clamped to a safe positive range so it's never a degenerate (zero/negative) drainage-area multiplier.");
    }

    [TestMethod]
    public void ComputePrecipitationField_SingleRidge_WindwardSideWetterThanLeeward()
    {
        // Task 4.2's rain-shadow signature: wind FROM the west (blowing eastward, +x) means the
        // ridge's west (low-x) side is windward, east (high-x) side is leeward/rain-shadowed.
        const int size = 64;
        var grid = MakeSingleRidgeGrid(size, cellSize: 200.0, peakHeight: 2500f);
        var p = new OrographicPrecipitation.Parameters(WindDirectionFromDeg: 270.0, WindSpeedMs: 15.0);

        var field = OrographicPrecipitation.ComputePrecipitationField(grid, p);

        double MeanOverXRange(int xStart, int xEnd)
        {
            var sum = 0.0;
            var count = 0;
            for (var y = 0; y < size; y++)
                for (var x = xStart; x < xEnd; x++)
                    if (x >= 0 && x < size) { sum += field[y * size + x]; count++; }
            return count > 0 ? sum / count : 0.0;
        }

        var centerX = size / 2;
        var windwardMean = MeanOverXRange(0, centerX - 4); // upwind base, well clear of the crest itself
        var leewardMean = MeanOverXRange(centerX + 4, size);

        Assert.IsTrue(windwardMean > leewardMean,
            $"Expected the windward (west) base to average wetter than the leeward (east) rain shadow — got windward={windwardMean:F3}, leeward={leewardMean:F3}.");
    }

    [TestMethod]
    public void Erode_WithoutPrecipitationWeight_LeavesResultUnchanged()
    {
        // Byte-identical-when-disabled regression per the plan's cross-cutting requirement.
        var gridA = MakeSingleRidgeGrid(20, 100.0, 500f);
        var gridB = MakeSingleRidgeGrid(20, 100.0, 500f);
        var p = new StreamPowerErosion.Parameters(Iterations: 40);
        var uplift = new double[20 * 20];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.001;

        StreamPowerErosion.Erode(gridA, p, uplift, precipitationWeightPerCell: null);
        StreamPowerErosion.Erode(gridB, p, uplift, precipitationWeightPerCell: Enumerable.Repeat(1.0, 20 * 20).ToArray());

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Erode_WithOrographicWeighting_WindwardSlopeErodesMoreThanLeewardSlope()
    {
        // Integration-level version of Task 4.2's rain-shadow smoke test: with a stronger drainage
        // area on the windward side (from ComputePrecipitationField), that side should show MORE
        // erosion (lower final relief) than a matched leeward point, everything else held equal.
        const int size = 40;
        var grid = MakeSingleRidgeGrid(size, cellSize: 200.0, peakHeight: 1500f);
        var precipParams = new OrographicPrecipitation.Parameters(WindDirectionFromDeg: 270.0, WindSpeedMs: 15.0);
        var precipitation = OrographicPrecipitation.ComputePrecipitationField(grid, precipParams);

        var uplift = new double[size * size];
        for (var i = 0; i < uplift.Length; i++) uplift[i] = 0.0005;

        StreamPowerErosion.Erode(grid, new StreamPowerErosion.Parameters(Iterations: 150), uplift,
            precipitationWeightPerCell: precipitation);

        var centerX = size / 2;
        // Compare symmetric flank points equidistant from the ridge crest — same starting elevation
        // before erosion, so any height difference now is erosion, not initial ridge shape.
        var windwardFlankHeight = grid.Values[(size / 2) * size + (centerX - 8)];
        var leewardFlankHeight = grid.Values[(size / 2) * size + (centerX + 8)];

        Assert.IsTrue(windwardFlankHeight < leewardFlankHeight,
            $"Expected the windward flank (more precipitation-weighted drainage area) to erode down MORE than the symmetric leeward flank — got windward={windwardFlankHeight:F2}, leeward={leewardFlankHeight:F2}.");
    }
}
