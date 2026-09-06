using TerraGen;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class PrevailingWindModelTests
{
    private static PlanetSettings.Resolved EarthDefaults() =>
        PlanetSettings.Load(Path.Combine(Directory.CreateTempSubdirectory().FullName, "terrain.db"));

    [TestMethod]
    public void WindDirectionFromDeg_TropicalBand_IsEasterlyAndFlipsAcrossEquator()
    {
        var north = PrevailingWindModel.WindDirectionFromDeg(10.0, subtropicalBeltBoundaryDeg: 30.0);
        var south = PrevailingWindModel.WindDirectionFromDeg(-10.0, subtropicalBeltBoundaryDeg: 30.0);

        Assert.AreEqual(45.0, north);
        Assert.AreEqual(135.0, south);
    }

    [TestMethod]
    public void WindDirectionFromDeg_MidLatitudeBand_IsWesterlyAndFlipsAcrossEquator()
    {
        var north = PrevailingWindModel.WindDirectionFromDeg(45.0, subtropicalBeltBoundaryDeg: 30.0);
        var south = PrevailingWindModel.WindDirectionFromDeg(-45.0, subtropicalBeltBoundaryDeg: 30.0);

        Assert.AreEqual(225.0, north);
        Assert.AreEqual(315.0, south);
    }

    [TestMethod]
    public void WindDirectionFromDeg_PolarBand_IsEasterlyAndFlipsAcrossEquator()
    {
        var north = PrevailingWindModel.WindDirectionFromDeg(75.0, subtropicalBeltBoundaryDeg: 30.0);
        var south = PrevailingWindModel.WindDirectionFromDeg(-75.0, subtropicalBeltBoundaryDeg: 30.0);

        Assert.AreEqual(45.0, north);
        Assert.AreEqual(135.0, south);
    }

    [TestMethod]
    public void SubtropicalBeltBoundaryDeg_FasterRotation_NarrowsTheBelt()
    {
        var earth = EarthDefaults();
        var fastRotator = earth with { PlanetSiderealRotationHrs = earth.PlanetSiderealRotationHrs / 4.0 };

        var earthBoundary = PrevailingWindModel.SubtropicalBeltBoundaryDeg(earth);
        var fastBoundary = PrevailingWindModel.SubtropicalBeltBoundaryDeg(fastRotator);

        Assert.IsTrue(fastBoundary < earthBoundary,
            "A faster-rotating planet should have a narrower Held-Hou belt than an Earth-rotation-rate planet with the same temperature baseline.");
    }

    [TestMethod]
    public void SubtropicalBeltBoundaryDeg_IsWithinPlausibleRange()
    {
        var boundary = PrevailingWindModel.SubtropicalBeltBoundaryDeg(EarthDefaults());

        Assert.IsTrue(boundary is > 5.0 and < 55.0);
    }
}
