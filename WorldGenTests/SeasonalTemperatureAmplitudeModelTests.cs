using WorldGen;
using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class SeasonalTemperatureAmplitudeModelTests
{
    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "world.db"));

    [TestMethod]
    public void AmplitudeC_AtEquator_IsZero()
    {
        Assert.AreEqual(0.0, SeasonalTemperatureAmplitudeModel.AmplitudeC(EarthDefaults(), 0.0), 1e-6);
    }

    [TestMethod]
    public void AmplitudeC_ZeroObliquity_IsZeroEverywhere()
    {
        var noTilt = EarthDefaults() with { PlanetObliquityDeg = 0.0 };

        Assert.AreEqual(0.0, SeasonalTemperatureAmplitudeModel.AmplitudeC(noTilt, 45.0), 1e-6);
        Assert.AreEqual(0.0, SeasonalTemperatureAmplitudeModel.AmplitudeC(noTilt, 89.0), 1e-6);
    }

    [TestMethod]
    public void AmplitudeC_GrowsWithLatitude()
    {
        var planet = EarthDefaults();

        var at15 = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 15.0);
        var at45 = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 45.0);
        var at75 = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 75.0);

        Assert.IsTrue(at15 < at45);
        Assert.IsTrue(at45 < at75);
    }

    [TestMethod]
    public void AmplitudeC_EarthDefaultsAt45Deg_MatchesLandOceanBlendedClosedForm()
    {
        // Cross-checked against a from-scratch python/numpy reimplementation of the same formula
        // (independent of this C# code) before picking this tolerance.
        var amplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(EarthDefaults(), 45.0);

        Assert.AreEqual(12.87, amplitude, 0.1);
    }

    [TestMethod]
    public void AmplitudeC_EarthDefaultsAt60Deg_IsInRealisticRange()
    {
        // Real Earth mid/high-latitude land+ocean-blended seasonal amplitude is roughly 10-20C —
        // this is the actual sanity check the plan's Stage 8 always wanted; only satisfiable once
        // land/ocean thermal-inertia contrast was added (the pre-fix version failed this badly).
        var amplitude = SeasonalTemperatureAmplitudeModel.AmplitudeC(EarthDefaults(), 60.0);

        Assert.IsTrue(amplitude is > 10.0 and < 20.0);
    }

    [TestMethod]
    public void AmplitudeC_LandOnlyWouldOverestimate_OceanFractionMeaningfullyLowersIt()
    {
        // Regression guard against silently regressing back to the pre-fix single-heat-capacity
        // behavior: the ocean fraction must actually pull the blended value well below what a
        // land-only planet (OceanFraction implicitly 0) would show at the same latitude.
        var blended = SeasonalTemperatureAmplitudeModel.AmplitudeC(EarthDefaults(), 45.0);

        Assert.IsTrue(blended < 25.0, "Ocean thermal inertia should pull the blend well below a land-only estimate (~38C at 45deg).");
    }

    [TestMethod]
    public void AmplitudeC_ExplicitOceanFraction_OverridesPlanetConfigTarget()
    {
        var planet = EarthDefaults();

        var atConfigDefault = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 45.0);
        var atMeasuredMostlyLand = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 45.0, oceanFraction: 0.1);
        var atMeasuredMostlyOcean = SeasonalTemperatureAmplitudeModel.AmplitudeC(planet, 45.0, oceanFraction: 0.95);

        Assert.AreNotEqual(atConfigDefault, atMeasuredMostlyLand);
        Assert.IsTrue(atMeasuredMostlyLand > atConfigDefault, "A mostly-land measured fraction should raise the amplitude above the config-default (0.71 ocean) blend.");
        Assert.IsTrue(atMeasuredMostlyOcean < atConfigDefault, "A mostly-ocean measured fraction should lower the amplitude below the config-default blend.");
    }

    [TestMethod]
    public void OrbitalPeriodSeconds_EarthDefaults_MatchesOneYear()
    {
        var periodDays = SeasonalTemperatureAmplitudeModel.OrbitalPeriodSeconds(EarthDefaults()) / 86400.0;

        Assert.AreEqual(365.25, periodDays, 1.0);
    }

    [TestMethod]
    public void OrbitalPeriodSeconds_FartherOrbit_IsLonger()
    {
        var earth = EarthDefaults();
        var farther = earth with { OrbitSemiMajorAxisAu = earth.OrbitSemiMajorAxisAu * 2.0 };

        var earthPeriod = SeasonalTemperatureAmplitudeModel.OrbitalPeriodSeconds(earth);
        var fartherPeriod = SeasonalTemperatureAmplitudeModel.OrbitalPeriodSeconds(farther);

        Assert.IsTrue(fartherPeriod > earthPeriod);
    }
}
