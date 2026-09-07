using WorldGen.Generation;

namespace WorldGenTests;

[TestClass]
public class KoppenClassifierTests
{
    private static double[] Constant(double value) => Enumerable.Repeat(value, 12).ToArray();

    [TestMethod]
    public void Classify_ConstantHotAndWetAllYear_IsAf()
    {
        var temp = Constant(27.0);
        var precip = Constant(150.0);

        Assert.AreEqual("Af", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_HotWithOneVeryDryMonth_IsAm()
    {
        var temp = Constant(27.0);
        var precip = Constant(200.0);
        precip[0] = 20.0;

        Assert.AreEqual("Am", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_HotWithLongDrySeason_IsAw()
    {
        var temp = Constant(27.0);
        var precip = new[] { 10.0, 10, 10, 150, 150, 150, 150, 150, 150, 10, 10, 10 };

        Assert.AreEqual("Aw", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_HotAndBoneDry_IsBWh()
    {
        var temp = Constant(30.0);
        var precip = Constant(5.0);

        Assert.AreEqual("BWh", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_ColdAndSparselyWet_IsBSk()
    {
        var temp = Constant(5.0);
        var precip = Constant(15.0);

        Assert.AreEqual("BSk", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    private static double[] MildSeasonalTemps(double coldC, double hotC)
    {
        var mid = (coldC + hotC) / 2.0;
        var amp = (hotC - coldC) / 2.0;
        return Enumerable.Range(0, 12)
            .Select(m => mid - amp * Math.Cos(2.0 * Math.PI * m / 12.0))
            .ToArray();
    }

    [TestMethod]
    public void Classify_TemperateEvenRainfall_IsCf()
    {
        var temp = MildSeasonalTemps(coldC: 5.0, hotC: 25.0);
        var precip = Constant(80.0);

        Assert.AreEqual("Cf", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_TemperateDrySummer_IsCs()
    {
        var temp = MildSeasonalTemps(coldC: 5.0, hotC: 25.0);
        // Indices 3-8 (Apr-Sep) = "summer" for the Northern-hemisphere convention this classifier uses.
        var precip = new[] { 100.0, 100, 100, 10, 10, 10, 10, 10, 10, 100, 100, 100 };

        Assert.AreEqual("Cs", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_TemperateDryWinter_IsCw()
    {
        var temp = MildSeasonalTemps(coldC: 5.0, hotC: 25.0);
        var precip = new[] { 5.0, 5, 5, 100, 100, 100, 100, 100, 100, 5, 5, 5 };

        Assert.AreEqual("Cw", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_ContinentalEvenRainfall_IsDf()
    {
        var temp = MildSeasonalTemps(coldC: -10.0, hotC: 20.0);
        var precip = Constant(80.0);

        Assert.AreEqual("Df", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_TundraWarmestMonthAboveFreezing_IsET()
    {
        var temp = MildSeasonalTemps(coldC: -30.0, hotC: 5.0);
        var precip = Constant(40.0);

        Assert.AreEqual("ET", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_IceCapNeverAboveFreezing_IsEF()
    {
        var temp = MildSeasonalTemps(coldC: -40.0, hotC: -5.0);
        var precip = Constant(10.0);

        Assert.AreEqual("EF", KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true));
    }

    [TestMethod]
    public void Classify_HemisphereFlag_FlipsWhichHalfYearCountsAsWinter()
    {
        var temp = MildSeasonalTemps(coldC: 5.0, hotC: 25.0);
        var precip = new[] { 100.0, 100, 100, 10, 10, 10, 10, 10, 10, 100, 100, 100 };

        var north = KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: true);
        var south = KoppenClassifier.Classify(temp, precip, isNorthernHemisphere: false);

        Assert.AreNotEqual(north, south);
    }

    [TestMethod]
    public void Classify_WrongArrayLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            KoppenClassifier.Classify(new double[11], new double[12], isNorthernHemisphere: true));
    }
}
