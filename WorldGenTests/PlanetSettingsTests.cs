using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorldGen;

namespace WorldGenTests;

[TestClass]
public class PlanetSettingsTests
{
    [TestMethod]
    public void ComputeSeed_IsInvariantAcrossCommaDecimalCulture()
    {
        // Same regression as TerraGenTests' own PlanetSettingsTests — WorldGen ported this
        // formula independently, so it needed the identical fix to stay in lockstep with
        // TerraGen's (and TerrainEditor's) seed for the same planet config.
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
}
