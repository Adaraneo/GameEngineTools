using System.Globalization;
using TerraGen;

namespace TerraGenTests;

[TestClass]
public class PlanetSettingsTests
{
    [TestMethod]
    public void ComputeSeed_IsInvariantAcrossCommaDecimalCulture()
    {
        // Regression test for a real bug: {value:R} formats through the AMBIENT culture unless
        // told otherwise, so under a comma-decimal locale (e.g. Czech), the exact same planet
        // config used to hash to a DIFFERENT seed than on an English/invariant machine — silently
        // making "the same planet" generate different terrain depending on who ran the tool.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantSeed = PlanetSettings.ComputeSeed("Vigilia Insectianis", 5.9726e24, 6378.1);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");
            var czechSeed = PlanetSettings.ComputeSeed("Vigilia Insectianis", 5.9726e24, 6378.1);

            Assert.AreEqual(invariantSeed, czechSeed,
                "ComputeSeed must not depend on the ambient thread culture's decimal separator.");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void ComputeSeed_SameInputs_IsDeterministic()
    {
        var a = PlanetSettings.ComputeSeed("Test Planet", 1.234e24, 5000.5);
        var b = PlanetSettings.ComputeSeed("Test Planet", 1.234e24, 5000.5);

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeSeed_DifferentMass_ProducesDifferentSeed()
    {
        var a = PlanetSettings.ComputeSeed("Test Planet", 1.234e24, 5000.5);
        var b = PlanetSettings.ComputeSeed("Test Planet", 1.235e24, 5000.5);

        Assert.AreNotEqual(a, b);
    }
}
