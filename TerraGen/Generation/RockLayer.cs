using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>Task 2.2's spatial rock-type assignment — continental crust gets a lithology bucketed from coherent noise, oceanic crust is basalt (literally true, not a simplification). ⚠ Design simplification per the plan: no deposition/stratigraphy history, just a noise-bucketed snapshot (Cordonnier et al. 2018's volumetric stratigraphy is the "if we ever need it" upgrade path, not implemented here).</summary>
public static class RockLayer
{
    public sealed record Parameters(
        /// <summary>⚠ Design simplification: arbitrary lithology-patch scale, not literature-derived — tune to taste for bigger/smaller rock-type regions.</summary>
        double NoiseWavelengthMeters = 5000.0,
        int Seed = 1);

    private static readonly RockType[] ContinentalTypes =
    {
        RockType.Granite, RockType.Gneiss, RockType.Schist, RockType.Quartzite,
        RockType.Marble, RockType.Limestone, RockType.Sandstone, RockType.Shale,
    };

    /// <summary>Assigns one <see cref="RockType"/> per cell of <paramref name="grid"/>. No plates (<paramref name="plates"/> null/empty) defaults every cell to continental — there's no oceanic/continental concept to consult without them.</summary>
    public static RockType[] ComputeRockTypeMap(TerrainHeightmap grid, TectonicPlates.Plate[]? plates,
        double refLatDeg, double refLonDeg, double planetRadiusMeters, Parameters p)
    {
        var width = grid.Width;
        var height = grid.Height;
        var result = new RockType[width * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var worldX = grid.OriginX + x * grid.CellSizeMeters;
                var worldY = grid.OriginY + y * grid.CellSizeMeters;
                var (lat, lon) = PlanetNoise.OffsetToLatLon(worldX, worldY, refLatDeg, refLonDeg, planetRadiusMeters);

                var isContinental = true;
                if (plates is { Length: > 0 })
                {
                    var (px, py, pz) = PlanetNoise.LatLonToUnitVector(lat, lon);
                    isContinental = TectonicPlates.Sample(plates, px, py, pz).IsContinental;
                }

                var idx = y * width + x;
                if (!isContinental)
                {
                    result[idx] = RockType.Basalt;
                    continue;
                }

                var n = PlanetNoise.SampleCoherentField(lat, lon, p.NoiseWavelengthMeters, p.Seed, planetRadiusMeters); // [-1, 1]
                var bucket = Math.Clamp((int)((n + 1.0) / 2.0 * ContinentalTypes.Length), 0, ContinentalTypes.Length - 1);
                result[idx] = ContinentalTypes[bucket];
            }
        }

        return result;
    }

    /// <summary>Looks up each cell's <see cref="RockProperties.ErodibilityK"/> from <see cref="RockPropertiesTable"/> — the array <see cref="StreamPowerErosion.Erode"/> consumes in place of its scalar <see cref="StreamPowerErosion.Parameters.K"/>.</summary>
    public static double[] ErodibilityKPerCell(RockType[] rockTypes, double referenceK = TableReferenceK, double compression = 1.0)
    {
        var result = new double[rockTypes.Length];
        for (var i = 0; i < rockTypes.Length; i++)
            result[i] = referenceK * Math.Pow(RockPropertiesTable.Values[rockTypes[i]].ErodibilityK / TableReferenceK, compression);
        return result;
    }

    /// <summary>Table K (gneiss, mid-table) that maps to the caller's own reference K.</summary>
    public const double TableReferenceK = 7.5e-6;

    /// <summary>⚠ Design simplification: exponent applied to each rock's K relative to <see cref="TableReferenceK"/>. The table spans 5 decades (Stock &amp; Montgomery 1999 — resistance across ALL lithologies and climates); used verbatim, quartzite/granite (K ≤ 2e-6) barely erode under 5-13 mm/yr uplift and relief grows to hundreds of km. 0.4 compresses that to ~2 decades (quartzite ≈ 0.18×, schist ≈ 18× the reference K).</summary>
    public const double ErodibilityCompression = 0.4;

    /// <summary>Looks up each cell's <see cref="RockProperties.DensityKgM3"/> — Task 3.1.2's reuse of this table for <see cref="Isostasy"/>'s crustal-side density instead of a second hardcoded constant.</summary>
    public static double[] DensityPerCell(RockType[] rockTypes)
    {
        var result = new double[rockTypes.Length];
        for (var i = 0; i < rockTypes.Length; i++)
            result[i] = RockPropertiesTable.Values[rockTypes[i]].DensityKgM3;
        return result;
    }
}
