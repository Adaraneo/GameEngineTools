using TerrainEditor.Rendering;

namespace TerrainEditorTests;

[TestClass]
public class TerrainColorRampTests
{
    [TestMethod]
    public void ForHeight_ShortTerrain_DoesNotStretchToFullSnowAtItsOwnPeak()
    {
        // A gridMax of 30m is well below the absolute ceiling floor — the peak of this SHORT
        // terrain must NOT render as full snow just because it happens to be this grid's own max.
        // (Regression: the old code used ceiling = Max(gridMax, 10.0), so a 30m peak always hit
        // t=1.0 — snow — regardless of how short the terrain genuinely was.)
        var peakColor = TerrainColorRamp.ForHeight(heightMeters: 30f, gridMin: 0f, gridMax: 30f);
        var snowColor = TerrainColorRamp.ForHeight(heightMeters: 150f, gridMin: 0f, gridMax: 150f);

        Assert.AreNotEqual(snowColor, peakColor,
            "A 30m peak on a short-terrain grid must read as visibly less snowy than a genuine 150m+ peak.");
    }

    [TestMethod]
    public void ForHeight_TallTerrain_StillGetsFullContrastFromItsOwnRange()
    {
        // Genuinely tall terrain (gridMax well above the absolute floor) keeps full grid-relative
        // contrast — its own summit still reads as snow.
        var summitColor = TerrainColorRamp.ForHeight(heightMeters: 400f, gridMin: 0f, gridMax: 400f);
        var baseColor = TerrainColorRamp.ForHeight(heightMeters: 0f, gridMin: 0f, gridMax: 400f);

        Assert.AreNotEqual(baseColor, summitColor);
        // Snow is the palest stop — the summit's color should be close to white.
        Assert.IsTrue(summitColor.R > 200 && summitColor.G > 200 && summitColor.B > 200,
            $"Expected the summit of a genuinely tall grid to read as snow-white, got {summitColor}.");
    }

    [TestMethod]
    public void ForHeight_SameAbsoluteHeight_RendersDifferentlyOnShortVsTallGrid()
    {
        // The whole point of the fix: identical absolute elevation (e.g. from two different
        // planet-gravity generations) must render differently depending on how tall the terrain
        // genuinely is, not get silently renormalized away.
        var onShortGrid = TerrainColorRamp.ForHeight(heightMeters: 60f, gridMin: 0f, gridMax: 60f);
        var onTallGrid = TerrainColorRamp.ForHeight(heightMeters: 60f, gridMin: 0f, gridMax: 400f);

        Assert.AreNotEqual(onShortGrid, onTallGrid,
            "The same 60m point must look 'higher up the ramp' on a 60m-tall grid than on a 400m-tall one.");
    }

    [TestMethod]
    public void ForCell_NoRiverNoOxbow_MatchesForHeight()
    {
        var cellColor = TerrainColorRamp.ForCell(heightMeters: 60f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 0, isOxbow: false);
        var heightColor = TerrainColorRamp.ForHeight(heightMeters: 60f, gridMin: 0f, gridMax: 400f);

        Assert.AreEqual(heightColor, cellColor);
    }

    [TestMethod]
    public void ForCell_LargerShreveMagnitude_RendersDarkerThanASmallCreek()
    {
        var creek = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 1, isOxbow: false);
        var trunk = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 5000, isOxbow: false);

        Assert.AreNotEqual(creek, trunk);
        Assert.IsTrue(trunk.R + trunk.G + trunk.B < creek.R + creek.G + creek.B,
            $"Expected a large-magnitude trunk river to render darker than a magnitude-1 creek — creek={creek}, trunk={trunk}.");
    }

    [TestMethod]
    public void ForCell_VeryLargeMagnitude_SaturatesInsteadOfGrowingUnbounded()
    {
        var atSaturation = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 60, isOxbow: false);
        var wellPastSaturation = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 1_000_000, isOxbow: false);

        Assert.AreEqual(atSaturation, wellPastSaturation,
            "Magnitude is unbounded — the color ramp must clamp instead of producing an out-of-range or crashing result.");
    }

    [TestMethod]
    public void ForCell_Oxbow_OverridesRiverGradientEvenWithAPositiveMagnitude()
    {
        // A cutoff removes a cell from the active river, but stale magnitude data shouldn't matter —
        // isOxbow must win regardless.
        var oxbow = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 500, isOxbow: true);
        var river = TerrainColorRamp.ForCell(heightMeters: 100f, gridMin: 0f, gridMax: 400f, shreveMagnitude: 500, isOxbow: false);

        Assert.AreNotEqual(river, oxbow);
    }
}
