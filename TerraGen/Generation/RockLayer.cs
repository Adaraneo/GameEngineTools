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
    public static double[] ErodibilityKPerCell(RockType[] rockTypes)
    {
        var result = new double[rockTypes.Length];
        for (var i = 0; i < rockTypes.Length; i++)
            result[i] = RockPropertiesTable.Values[rockTypes[i]].ErodibilityK;
        return result;
    }
}
