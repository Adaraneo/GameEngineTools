namespace WorldGen.Generation;

/// <summary>Köppen–Geiger climate classification from 12 monthly (T,P) samples — see docs/plans/planet-physics-driven-climate.md Stage 7.</summary>
// [PRIMARY] Peel, M.C., Finlayson, B.L. & McMahon, T.A., 2007, "Updated world map of the Koppen-Geiger climate classification," Hydrol. Earth Syst. Sci. 11:1633-1644, doi:10.5194/hess-11-1633-2007, Table 1.
// Standardizes on Peel's 0C C/D boundary and 70% arid-seasonality threshold (not Kottek 2006's 66.7%/Koppen's original -3C) — see the plan's own flagged competing-convention note.
// Deliberately does NOT implement the C/D third (temperature-subtype) letter (a/b/c/d) — not covered by this plan's own Thresholds summary; codes stop at 2 letters for A/E, 2 for C/D, 3 for B.
public static class KoppenClassifier
{
    /// <summary>Classifies one point from 12 calendar-ordered (Jan..Dec) monthly samples. <paramref name="isNorthernHemisphere"/> picks which 6 months count as the "winter"/"summer" half-year for the arid-seasonality and C/D second-letter rules.</summary>
    public static string Classify(double[] monthlyTempC, double[] monthlyPrecipMm, bool isNorthernHemisphere)
    {
        if (monthlyTempC.Length != 12 || monthlyPrecipMm.Length != 12)
            throw new ArgumentException("Köppen classification needs exactly 12 monthly samples.");

        var mat = monthlyTempC.Average();
        var map = monthlyPrecipMm.Sum();
        var tHot = monthlyTempC.Max();
        var tCold = monthlyTempC.Min();

        var (winterIdx, summerIdx) = isNorthernHemisphere
            ? (new[] { 9, 10, 11, 0, 1, 2 }, new[] { 3, 4, 5, 6, 7, 8 })
            : (new[] { 3, 4, 5, 6, 7, 8 }, new[] { 9, 10, 11, 0, 1, 2 });

        var winterPrecip = winterIdx.Sum(i => monthlyPrecipMm[i]);
        var summerPrecip = summerIdx.Sum(i => monthlyPrecipMm[i]);

        var pTh = winterPrecip >= 0.70 * map ? 2.0 * mat
            : summerPrecip >= 0.70 * map ? 2.0 * mat + 28.0
            : 2.0 * mat + 14.0;

        if (map < 10.0 * pTh)
        {
            var thirdLetter = mat >= 18.0 ? "h" : "k";
            return map < 5.0 * pTh ? $"BW{thirdLetter}" : $"BS{thirdLetter}";
        }

        if (tCold >= 18.0)
        {
            var pDry = monthlyPrecipMm.Min();
            if (pDry >= 60.0) return "Af";
            if (pDry >= 100.0 - map / 25.0) return "Am";
            return "Aw";
        }

        if (tHot < 10.0) return tHot > 0.0 ? "ET" : "EF";

        var mainClass = tCold <= 0.0 ? "D" : "C";
        var secondLetter = CToDSecondLetter(monthlyPrecipMm, winterIdx, summerIdx);
        return mainClass + secondLetter;
    }

    private static string CToDSecondLetter(double[] monthlyPrecipMm, int[] winterIdx, int[] summerIdx)
    {
        var pSDry = summerIdx.Min(i => monthlyPrecipMm[i]);
        var pSWet = summerIdx.Max(i => monthlyPrecipMm[i]);
        var pWDry = winterIdx.Min(i => monthlyPrecipMm[i]);
        var pWWet = winterIdx.Max(i => monthlyPrecipMm[i]);

        if (pSDry < 40.0 && pSDry < pWWet / 3.0) return "s";
        if (pWDry < pSWet / 10.0) return "w";
        return "f";
    }
}
