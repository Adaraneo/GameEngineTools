using GameEngineTools.World.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class TerrainGeneratorTests
{
    private static TerrainHeightmap MakeBlankGrid(int width = 40, int height = 40, double cellSize = 10.0)
        => new(
            Id: "test",
            OriginX: 0.0,
            OriginY: 0.0,
            CellSizeMeters: cellSize,
            Width: width,
            Height: height,
            Values: new float[width * height]);

    [TestMethod]
    public void Generate_SameSeedAndParameters_IsDeterministic()
    {
        var gridA = MakeBlankGrid();
        var gridB = MakeBlankGrid();
        var parameters = new TerrainGenerator.Parameters(Seed: 42);

        TerrainGenerator.Generate(gridA, parameters);
        TerrainGenerator.Generate(gridB, parameters);

        CollectionAssert.AreEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Generate_DifferentSeed_ProducesDifferentTerrain()
    {
        var gridA = MakeBlankGrid();
        var gridB = MakeBlankGrid();

        TerrainGenerator.Generate(gridA, new TerrainGenerator.Parameters(Seed: 1));
        TerrainGenerator.Generate(gridB, new TerrainGenerator.Parameters(Seed: 2));

        CollectionAssert.AreNotEqual(gridA.Values, gridB.Values);
    }

    [TestMethod]
    public void Generate_AllValuesFinite()
    {
        var grid = MakeBlankGrid();

        TerrainGenerator.Generate(grid, new TerrainGenerator.Parameters(Seed: 7));

        foreach (var v in grid.Values)
        {
            Assert.IsFalse(float.IsNaN(v), "Generated terrain must never produce NaN.");
            Assert.IsFalse(float.IsInfinity(v), "Generated terrain must never produce Infinity.");
        }
    }

    [TestMethod]
    public void Generate_StaysWithinDeclaredBounds()
    {
        // The landmass term is signed and bounded to ±Amplitude×frac/2; the mountain term is
        // UNSIGNED (uplift only) and bounded to [0, Amplitude×(1-frac)] — see Sample()'s remarks.
        var grid = MakeBlankGrid(60, 60);
        const double amplitude = 150.0;
        const double frac = 0.65;

        TerrainGenerator.Generate(grid, new TerrainGenerator.Parameters(
            Seed: 3, AmplitudeMeters: amplitude, LandmassAmplitudeFraction: frac));

        var min = amplitude * frac / -2.0;
        var max = amplitude * frac / 2.0 + amplitude * (1.0 - frac);
        foreach (var v in grid.Values)
        {
            Assert.IsTrue(v >= min - 1e-3, $"Expected height >= {min}, got {v}.");
            Assert.IsTrue(v <= max + 1e-3, $"Expected height <= {max}, got {v}.");
        }
    }

    [TestMethod]
    public void Generate_LargerAmplitude_ProducesLargerRange()
    {
        var smallGrid = MakeBlankGrid();
        var largeGrid = MakeBlankGrid();

        TerrainGenerator.Generate(smallGrid, new TerrainGenerator.Parameters(Seed: 5, AmplitudeMeters: 20.0));
        TerrainGenerator.Generate(largeGrid, new TerrainGenerator.Parameters(Seed: 5, AmplitudeMeters: 500.0));

        var smallRange = smallGrid.Values.Max() - smallGrid.Values.Min();
        var largeRange = largeGrid.Values.Max() - largeGrid.Values.Min();
        Assert.IsTrue(largeRange > smallRange);
    }

    [TestMethod]
    public void Generate_MountainUplift_IsNeverNegative()
    {
        // With LandmassAmplitudeFraction=0, elevation is the mountain layer alone — real uplift
        // only ever adds height on top of the landmass baseline, it never carves below it.
        var grid = MakeBlankGrid(50, 50);

        TerrainGenerator.Generate(grid, new TerrainGenerator.Parameters(
            Seed: 4, AmplitudeMeters: 200.0, LandmassAmplitudeFraction: 0.0));

        foreach (var v in grid.Values)
            Assert.IsTrue(v >= -1e-3, $"Expected mountain uplift to never go negative, got {v}.");
    }

    [TestMethod]
    public void Generate_SmallMountainWavelength_DoesNotFragmentCoastline()
    {
        // The old design let detail noise flip land/sea sign anywhere on the map when its
        // wavelength was small. Now mountain uplift is strictly non-negative, so it structurally
        // CANNOT flip a landmass-positive point negative — only the landmass layer (whose
        // wavelength is always tied to the map's own extent) controls where the coastline is.
        var grid = MakeBlankGrid(60, 60, cellSize: 10.0); // 600m map
        TerrainGenerator.Generate(grid, new TerrainGenerator.Parameters(
            Seed: 11, AmplitudeMeters: 200.0, MountainWavelengthMeters: 15.0));

        var y = grid.Height / 2;
        var changes = 0;
        var prevPositive = grid.Values[y * grid.Width] >= 0f;
        for (var x = 1; x < grid.Width; x++)
        {
            var positive = grid.Values[y * grid.Width + x] >= 0f;
            if (positive != prevPositive) changes++;
            prevPositive = positive;
        }

        Assert.IsTrue(changes <= 6, $"Expected a coherent coastline (few sea-level crossings), got {changes}.");
    }

    [TestMethod]
    public void Sample_MountainBelt_VariesLessAlongItsDirectionThanAcrossIt()
    {
        // Belt direction 0° means "along the belt" = the X axis (see Sample()'s rotation:
        // alongBelt = worldX·cos0 + worldY·sin0 = worldX). A stretched ridged fractal should
        // therefore change slowly as X varies (elongated ridges) and quickly as Y varies.
        var p = new TerrainGenerator.Parameters(
            Seed: 3, AmplitudeMeters: 200.0, LandmassAmplitudeFraction: 0.0,
            MountainWavelengthMeters: 50.0, MountainBeltDirectionDeg: 0.0, MountainBeltStretch: 6.0);

        double TotalVariation(bool alongX)
        {
            double total = 0;
            double? prev = null;
            for (double t = 0; t < 500; t += 5)
            {
                var h = alongX ? TerrainGenerator.Sample(t, 250, p) : TerrainGenerator.Sample(250, t, p);
                if (prev is { } pv) total += Math.Abs(h - pv);
                prev = h;
            }
            return total;
        }

        var alongBelt = TotalVariation(alongX: true);
        var acrossBelt = TotalVariation(alongX: false);

        Assert.IsTrue(alongBelt < acrossBelt * 0.8,
            $"Expected noticeably less variation along the belt direction than across it: along={alongBelt}, across={acrossBelt}.");
    }

    [TestMethod]
    public void Generate_ExpandingTheGrid_DoesNotRescaleExistingTerrain()
    {
        // Regression test: wavelengths must be a FIXED real-world scale, not derived from the
        // current grid's own extent — otherwise regenerating after Expand Map stretches the
        // existing landscape to fill the new area instead of revealing more terrain at the same
        // scale. Same origin/cell size/parameters, only the grid's Width/Height differ — every
        // cell the smaller grid covers must come out byte-for-byte identical on the bigger one.
        var smallGrid = MakeBlankGrid(width: 40, height: 40, cellSize: 10.0);
        var bigGrid = MakeBlankGrid(width: 90, height: 90, cellSize: 10.0);
        var parameters = new TerrainGenerator.Parameters(Seed: 6, AmplitudeMeters: 200.0);

        TerrainGenerator.Generate(smallGrid, parameters);
        TerrainGenerator.Generate(bigGrid, parameters);

        for (var gy = 0; gy < smallGrid.Height; gy++)
        {
            for (var gx = 0; gx < smallGrid.Width; gx++)
            {
                var small = smallGrid.Values[gy * smallGrid.Width + gx];
                var big = bigGrid.Values[gy * bigGrid.Width + gx];
                Assert.AreEqual(small, big, 1e-4f,
                    $"Terrain at the same world position must not depend on the grid's own size (cell {gx},{gy}).");
            }
        }
    }

    [TestMethod]
    public void Sample_HigherGravity_ProducesShorterMountains()
    {
        // Isostasy: rock can only support so much weight before it deforms, so higher gravity
        // compresses maximum mountain height. LandmassAmplitudeFraction=0 isolates the mountain
        // term so this measures gravity's effect on it alone.
        var earthG = TerrainGenerator.EarthSurfaceGravityMs2;
        var p = new TerrainGenerator.Parameters(
            Seed: 12, AmplitudeMeters: 200.0, LandmassAmplitudeFraction: 0.0, MountainWavelengthMeters: 60.0);
        var highGravity = p with { GravityMs2 = earthG * 3.0 };  // e.g. a much heavier super-Earth
        var lowGravity = p with { GravityMs2 = earthG / 3.0 };   // e.g. Mars-like

        double MaxSampledElevation(TerrainGenerator.Parameters parameters)
        {
            double max = 0;
            for (var x = 0; x < 400; x += 5)
                for (var y = 0; y < 400; y += 5)
                    max = Math.Max(max, TerrainGenerator.Sample(x, y, parameters));
            return max;
        }

        var earthMax = MaxSampledElevation(p);
        var highGravityMax = MaxSampledElevation(highGravity);
        var lowGravityMax = MaxSampledElevation(lowGravity);

        Assert.IsTrue(highGravityMax < earthMax,
            $"Expected higher gravity to compress mountain height: earth={earthMax}, high-g={highGravityMax}.");
        Assert.IsTrue(lowGravityMax > earthMax,
            $"Expected lower gravity to allow taller mountains: earth={earthMax}, low-g={lowGravityMax}.");
    }

    [TestMethod]
    public void Sample_GravityChange_DoesNotAffectLandmassLayer()
    {
        // LandmassAmplitudeFraction=1 isolates the landmass term (mountain contributes nothing) —
        // gravity must not perturb it at all, since coastline shape isn't a strength-of-materials
        // question the way peak height is.
        var earthG = TerrainGenerator.EarthSurfaceGravityMs2;
        var p = new TerrainGenerator.Parameters(Seed: 13, AmplitudeMeters: 200.0, LandmassAmplitudeFraction: 1.0);
        var highGravity = p with { GravityMs2 = earthG * 5.0 };

        for (var x = 0; x < 300; x += 17)
        {
            for (var y = 0; y < 300; y += 17)
            {
                var earthValue = TerrainGenerator.Sample(x, y, p);
                var highGravityValue = TerrainGenerator.Sample(x, y, highGravity);
                Assert.AreEqual(earthValue, highGravityValue, 1e-9,
                    $"Landmass-only elevation must be identical regardless of gravity (point {x},{y}).");
            }
        }
    }

    [TestMethod]
    public void Sample_PathologicalGravity_StaysClampedAndFinite()
    {
        // A near-zero-mass or absurd planet config must not produce runaway/non-finite terrain —
        // the gravity scale factor is clamped to [0.1, 10] specifically to guard against this.
        var p = new TerrainGenerator.Parameters(
            Seed: 14, AmplitudeMeters: 200.0, LandmassAmplitudeFraction: 0.0, GravityMs2: 1e-9);

        var value = TerrainGenerator.Sample(50, 50, p);

        Assert.IsFalse(double.IsNaN(value));
        Assert.IsFalse(double.IsInfinity(value));
        Assert.IsTrue(value <= 200.0 * 10.0 + 1e-6, "Expected the gravity scale to be clamped to at most 10x.");
    }
}
