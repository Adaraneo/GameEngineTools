using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class ProgressiveScannerTests
{
    private static readonly PlanetNoise.Parameters NoiseParams = new(Seed: 7);
    private const double PlanetRadiusMeters = PlanetNoise.EarthRadiusMeters;

    [TestMethod]
    public void Run_ProducesRequestedNumberOfLevels()
    {
        var initial = new PlanetScanner.Options(Width: 40, Height: 20, LatMin: -30, LatMax: 30, LonMin: -30, LonMax: 30);
        var options = new ProgressiveScanner.Options(Levels: 4, ZoomFactor: 3.0, initial);

        var results = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);

        Assert.AreEqual(4, results.Count);
        for (var i = 0; i < results.Count; i++)
            Assert.AreEqual(i, results[i].Level);
    }

    [TestMethod]
    public void Run_SingleLevel_UsesTheInitialWindowUnchanged()
    {
        var initial = new PlanetScanner.Options(Width: 20, Height: 10, LatMin: 5, LatMax: 15, LonMin: 5, LonMax: 15);
        var options = new ProgressiveScanner.Options(Levels: 1, ZoomFactor: 4.0, initial);

        var results = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(initial.LatMin, results[0].WindowUsed.LatMin);
        Assert.AreEqual(initial.LatMax, results[0].WindowUsed.LatMax);
        Assert.AreEqual(initial.LonMin, results[0].WindowUsed.LonMin);
        Assert.AreEqual(initial.LonMax, results[0].WindowUsed.LonMax);
    }

    [TestMethod]
    public void Run_EachLevelWindow_IsNarrowerByExactlyTheZoomFactor()
    {
        var initial = new PlanetScanner.Options(Width: 40, Height: 20, LatMin: -40, LatMax: 40, LonMin: -40, LonMax: 40);
        var options = new ProgressiveScanner.Options(Levels: 3, ZoomFactor: 5.0, initial);

        var results = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);

        for (var i = 1; i < results.Count; i++)
        {
            var previous = results[i - 1].WindowUsed;
            var current = results[i].WindowUsed;
            var expectedLatSpan = (previous.LatMax - previous.LatMin) / options.ZoomFactor;
            var expectedLonSpan = (previous.LonMax - previous.LonMin) / options.ZoomFactor;

            Assert.AreEqual(expectedLatSpan, current.LatMax - current.LatMin, 1e-9);
            Assert.AreEqual(expectedLonSpan, current.LonMax - current.LonMin, 1e-9);
        }
    }

    [TestMethod]
    public void Run_NoCoastlinePossible_ZoomsTowardThePreviousWindowsOwnCenter()
    {
        // A 1x1 grid can never find a coastline (no neighbors to compare against at all) — this
        // deterministically exercises the "CoastlineTarget is null" fallback path.
        var initial = new PlanetScanner.Options(Width: 1, Height: 1, LatMin: 0, LatMax: 20, LonMin: 0, LonMax: 20);
        var options = new ProgressiveScanner.Options(Levels: 3, ZoomFactor: 2.0, initial);

        var results = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);

        foreach (var level in results)
            Assert.IsNull(level.CoastlineTarget);

        for (var i = 1; i < results.Count; i++)
        {
            var previous = results[i - 1].WindowUsed;
            var previousCenterLat = (previous.LatMin + previous.LatMax) / 2.0;
            var previousCenterLon = (previous.LonMin + previous.LonMax) / 2.0;
            var current = results[i].WindowUsed;
            var currentCenterLat = (current.LatMin + current.LatMax) / 2.0;
            var currentCenterLon = (current.LonMin + current.LonMax) / 2.0;

            Assert.AreEqual(previousCenterLat, currentCenterLat, 1e-9);
            Assert.AreEqual(previousCenterLon, currentCenterLon, 1e-9);
        }
    }

    [TestMethod]
    public void Run_SameInputs_IsDeterministic()
    {
        var initial = new PlanetScanner.Options(Width: 30, Height: 15, LatMin: -20, LatMax: 20, LonMin: -20, LonMax: 20);
        var options = new ProgressiveScanner.Options(Levels: 3, ZoomFactor: 3.0, initial);

        var a = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);
        var b = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);

        Assert.AreEqual(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.AreEqual(a[i].WindowUsed, b[i].WindowUsed);
            Assert.AreEqual(a[i].CoastlineTarget, b[i].CoastlineTarget);
        }
    }

    [TestMethod]
    public void Run_WhenCoastlineFound_NextWindowIsCenteredOnIt()
    {
        var initial = new PlanetScanner.Options(Width: 60, Height: 30, LatMin: -90, LatMax: 90, LonMin: -180, LonMax: 180);
        var options = new ProgressiveScanner.Options(Levels: 2, ZoomFactor: 10.0, initial);

        var results = ProgressiveScanner.Run(NoiseParams, PlanetRadiusMeters, plates: null, options);
        Assert.AreEqual(2, results.Count);

        if (results[0].CoastlineTarget is not { } target)
        {
            Assert.Inconclusive("Seed 7's global scan found no coastline at all — pick a different seed for this test.");
            return;
        }

        var nextWindow = results[1].WindowUsed;
        var nextCenterLat = (nextWindow.LatMin + nextWindow.LatMax) / 2.0;
        var nextCenterLon = (nextWindow.LonMin + nextWindow.LonMax) / 2.0;

        Assert.AreEqual(target.LatDeg, nextCenterLat, 1e-9);
        Assert.AreEqual(target.LonDeg, nextCenterLon, 1e-9);
    }
}
