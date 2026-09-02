using System.IO.Compression;

namespace TerraGen.Generation;

/// <summary>
/// Hand-rolled 16-bit grayscale PNG encoder — deliberately minimal (no external image library):
/// PNG's own ceiling is 16 bits per channel anyway (no 32-bit PNG exists), and everything needed
/// to write one is already in the BCL (<see cref="ZLibStream"/> for the IDAT payload, plus a
/// standard CRC-32). Not a general-purpose PNG writer — grayscale-16, uncompressed filter type 0
/// per row, single IDAT chunk — just enough for a quick visual heightmap preview. For exact
/// elevation values (no quantization), see <see cref="HeightmapExporter"/>'s raw float export
/// instead — this PNG is for eyeballing/other tools, not for precision round-tripping.
/// </summary>
public static class PngHeightmapWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Writes <paramref name="values"/> (row-major, length == width*height) as a 16-bit
    /// grayscale PNG, linearly scaled so <paramref name="min"/> maps to 0 and <paramref name="max"/>
    /// maps to 65535. Row order matches <paramref name="values"/>'s own (row 0 first) — same
    /// convention <see cref="HeightmapExporter"/>'s raw float export uses, so both stay consistent.</summary>
    public static void WriteGrayscale16(string path, float[] values, int width, int height, float min, float max)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != width * height)
            throw new ArgumentException($"values.Length ({values.Length}) must equal width*height ({width * height}).", nameof(values));

        using var stream = File.Create(path);
        stream.Write(Signature);

        var ihdr = new byte[13];
        WriteUInt32BE(ihdr, 0, (uint)width);
        WriteUInt32BE(ihdr, 4, (uint)height);
        ihdr[8] = 16; // bit depth
        ihdr[9] = 0;  // color type: grayscale
        ihdr[10] = 0; // compression method (only value the spec defines)
        ihdr[11] = 0; // filter method (only value the spec defines)
        ihdr[12] = 0; // interlace method: none
        WriteChunk(stream, "IHDR", ihdr);

        var range = Math.Max(max - min, 1e-6f); // guard against a perfectly flat tile (max == min)
        var raw = new byte[height * (1 + width * 2)]; // +1 per row for the filter-type byte
        var pos = 0;
        for (var y = 0; y < height; y++)
        {
            raw[pos++] = 0; // filter type: None
            for (var x = 0; x < width; x++)
            {
                var t = (values[y * width + x] - min) / range;
                var sample = (ushort)Math.Round(Math.Clamp(t, 0.0, 1.0) * 65535.0);
                raw[pos++] = (byte)(sample >> 8);   // PNG multi-byte samples are big-endian
                raw[pos++] = (byte)(sample & 0xFF);
            }
        }

        using (var idatBuffer = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idatBuffer, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw, 0, raw.Length);
            WriteChunk(stream, "IDAT", idatBuffer.ToArray());
        }

        WriteChunk(stream, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);

        var length = new byte[4];
        WriteUInt32BE(length, 0, (uint)data.Length);
        stream.Write(length);

        stream.Write(typeBytes);
        stream.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        WriteUInt32BE(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteUInt32BE(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    // Standard PNG/zlib CRC-32 (polynomial 0xEDB88320) — same algorithm every PNG encoder/decoder uses.
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] typeBytes, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in typeBytes)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
