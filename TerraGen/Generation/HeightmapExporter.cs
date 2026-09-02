using System.Text.Json;
using System.Text.Json.Serialization;
using GameEngineTools.World.Data;

namespace TerraGen.Generation;

/// <summary>
/// Exports one generated <see cref="TerrainHeightmap"/> tile as three files a downstream tool
/// (e.g. a Blender Python import script) can consume: exact float32 elevation data, a 16-bit
/// grayscale PNG for quick visual preview, and a JSON sidecar with everything needed to place and
/// scale either of them correctly (origin, cell size, elevation range).
/// </summary>
public static class HeightmapExporter
{
    public sealed record Metadata(
        [property: JsonPropertyName("tileId")] string TileId,
        [property: JsonPropertyName("planetName")] string PlanetName,
        [property: JsonPropertyName("seed")] int Seed,
        [property: JsonPropertyName("originXMeters")] double OriginXMeters,
        [property: JsonPropertyName("originYMeters")] double OriginYMeters,
        [property: JsonPropertyName("cellSizeMeters")] double CellSizeMeters,
        [property: JsonPropertyName("width")] int Width,
        [property: JsonPropertyName("height")] int Height,
        [property: JsonPropertyName("elevationMinMeters")] double ElevationMinMeters,
        [property: JsonPropertyName("elevationMaxMeters")] double ElevationMaxMeters,
        /// <summary>Byte layout of the paired <c>.f32</c> file — little-endian float32, row-major,
        /// row 0 first (matches <see cref="TerrainHeightmap.ToBytes"/>).</summary>
        [property: JsonPropertyName("rawFormat")] string RawFormat,
        /// <summary>How the paired <c>.png</c>'s 16-bit samples map back to meters: linear from
        /// <see cref="ElevationMinMeters"/> (sample 0) to <see cref="ElevationMaxMeters"/> (sample
        /// 65535) — lossy (see <see cref="PngHeightmapWriter"/>'s own remarks), use the .f32 file
        /// instead when exact values matter.</summary>
        [property: JsonPropertyName("pngFormat")] string PngFormat);

    public sealed record ExportResult(string TileId, string RawPath, string PngPath, string MetadataPath);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ExportResult Export(TerrainHeightmap tile, string outputDir, string planetName, int seed)
    {
        ArgumentNullException.ThrowIfNull(tile);
        Directory.CreateDirectory(outputDir);
        var basePath = Path.Combine(outputDir, tile.Id);

        var rawPath = basePath + ".f32";
        File.WriteAllBytes(rawPath, tile.ToBytes());

        var min = tile.Values.Length > 0 ? tile.Values.Min() : 0f;
        var max = tile.Values.Length > 0 ? tile.Values.Max() : 0f;
        var pngPath = basePath + ".png";
        PngHeightmapWriter.WriteGrayscale16(pngPath, tile.Values, tile.Width, tile.Height, min, max);

        var metadata = new Metadata(
            tile.Id, planetName, seed,
            tile.OriginX, tile.OriginY, tile.CellSizeMeters,
            tile.Width, tile.Height,
            min, max,
            RawFormat: "float32-little-endian-row-major",
            PngFormat: "grayscale16-row-major-linear-min-to-max");
        var metadataPath = basePath + ".json";
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, JsonOptions));

        return new ExportResult(tile.Id, rawPath, pngPath, metadataPath);
    }
}
