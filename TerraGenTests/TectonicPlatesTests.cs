using Microsoft.VisualStudio.TestTools.UnitTesting;
using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class TectonicPlatesTests
{
    [TestMethod]
    public void Generate_SameSeedAndCount_IsDeterministic()
    {
        var a = TectonicPlates.Generate(seed: 42, count: 10);
        var b = TectonicPlates.Generate(seed: 42, count: 10);

        Assert.AreEqual(a.Length, b.Length);
        for (var i = 0; i < a.Length; i++)
            Assert.AreEqual(a[i], b[i]);
    }

    [TestMethod]
    public void Generate_DifferentSeeds_ProduceDifferentPlates()
    {
        var a = TectonicPlates.Generate(seed: 1, count: 10);
        var b = TectonicPlates.Generate(seed: 2, count: 10);

        Assert.IsFalse(a.SequenceEqual(b));
    }

    [TestMethod]
    public void Generate_EveryPlate_SitsOnTheUnitSphere()
    {
        var plates = TectonicPlates.Generate(seed: 5, count: 20);

        foreach (var p in plates)
        {
            var lengthSquared = p.X * p.X + p.Y * p.Y + p.Z * p.Z;
            Assert.AreEqual(1.0, lengthSquared, 1e-6, $"Plate {p.Id} isn't on the unit sphere.");
        }
    }

    [TestMethod]
    public void Generate_ZeroOrNegativeCount_ClampsToAtLeastOnePlate()
    {
        var plates = TectonicPlates.Generate(seed: 1, count: 0);
        Assert.AreEqual(1, plates.Length);

        var plates2 = TectonicPlates.Generate(seed: 1, count: -5);
        Assert.AreEqual(1, plates2.Length);
    }

    [TestMethod]
    public void Sample_SinglePlate_AlwaysReturnsNoBoundary()
    {
        var plates = TectonicPlates.Generate(seed: 1, count: 1);
        var sample = TectonicPlates.Sample(plates, 0.5, 0.5, Math.Sqrt(1 - 0.25 - 0.25));

        Assert.AreEqual(TectonicPlates.BoundaryType.None, sample.Boundary);
        Assert.AreEqual(0.0, sample.BoundaryInfluence);
    }

    [TestMethod]
    public void Sample_SameInputs_IsDeterministic()
    {
        var plates = TectonicPlates.Generate(seed: 7, count: 12);

        var a = TectonicPlates.Sample(plates, 0.3, -0.4, 0.866);
        var b = TectonicPlates.Sample(plates, 0.3, -0.4, 0.866);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Sample_AtAPlatesOwnCenter_HasLowBoundaryInfluence()
    {
        var plates = TectonicPlates.Generate(seed: 3, count: 12);
        var plate = plates[0];

        var sample = TectonicPlates.Sample(plates, plate.X, plate.Y, plate.Z);

        Assert.AreEqual(plate.Id, sample.PlateId);
        Assert.IsTrue(sample.BoundaryInfluence < 0.3,
            $"Expected low boundary influence right at a plate's own center, got {sample.BoundaryInfluence}.");
    }

    [TestMethod]
    public void Sample_ManyPointsOverTheSphere_AllValuesFiniteAndInRange()
    {
        var plates = TectonicPlates.Generate(seed: 11, count: 14);

        for (var latDeg = -90.0; latDeg <= 90.0; latDeg += 15.0)
        {
            for (var lonDeg = -180.0; lonDeg < 180.0; lonDeg += 15.0)
            {
                var latRad = latDeg * Math.PI / 180.0;
                var lonRad = lonDeg * Math.PI / 180.0;
                var cosLat = Math.Cos(latRad);
                var x = cosLat * Math.Cos(lonRad);
                var y = cosLat * Math.Sin(lonRad);
                var z = Math.Sin(latRad);

                var sample = TectonicPlates.Sample(plates, x, y, z);

                Assert.IsFalse(double.IsNaN(sample.BoundaryInfluence));
                Assert.IsFalse(double.IsInfinity(sample.BoundaryInfluence));
                Assert.IsTrue(sample.BoundaryInfluence is >= 0.0 and <= 1.0,
                    $"BoundaryInfluence {sample.BoundaryInfluence} out of [0,1] at lat={latDeg}, lon={lonDeg}.");
                Assert.IsTrue(sample.PlateId >= 0 && sample.PlateId < plates.Length);
            }
        }
    }
}
