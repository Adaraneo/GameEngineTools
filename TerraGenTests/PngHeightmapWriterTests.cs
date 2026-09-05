using TerraGen.Generation;

namespace TerraGenTests;

[TestClass]
public class PngHeightmapWriterTests
{
    private static string TempPngPath() => Path.Combine(Path.GetTempPath(), $"png_test_{Guid.NewGuid():N}.png");

    /// <summary>Minimal chunk reader — not a general PNG decoder, just enough to verify the
    /// writer produced structurally valid chunks (correct CRCs, correct IHDR fields) without
    /// pulling in an image library as a test dependency.</summary>
    private static List<(string Type, byte[] Data)> ReadChunks(byte[] bytes)
    {
        var expectedSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        CollectionAssert.AreEqual(expectedSignature, bytes[..8]);

        var chunks = new List<(string, byte[])>();
        var pos = 8;
        while (pos < bytes.Length)
        {
            var length = ReadUInt32BE(bytes, pos); pos += 4;
            var type = System.Text.Encoding.ASCII.GetString(bytes, pos, 4); pos += 4;
            var data = bytes[pos..(pos + (int)length)]; pos += (int)length;
            var crc = ReadUInt32BE(bytes, pos); pos += 4;

            Assert.AreEqual(Crc32(System.Text.Encoding.ASCII.GetBytes(type), data), crc, $"Bad CRC on chunk '{type}'.");
            chunks.Add((type, data));
        }
        return chunks;
    }

    private static uint ReadUInt32BE(byte[] b, int offset) =>
        (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);

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

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    [TestMethod]
    public void WriteGrayscale16_ProducesValidChunkStructure_WithCorrectIhdr()
    {
        var path = TempPngPath();
        try
        {
            var values = new float[4 * 3];
            for (var i = 0; i < values.Length; i++) values[i] = i;

            PngHeightmapWriter.WriteGrayscale16(path, values, width: 4, height: 3, min: 0, max: 11);

            var chunks = ReadChunks(File.ReadAllBytes(path));
            Assert.AreEqual("IHDR", chunks[0].Type);
            Assert.AreEqual("IEND", chunks[^1].Type);
            Assert.IsTrue(chunks.Any(c => c.Type == "IDAT"));

            var ihdr = chunks[0].Data;
            Assert.AreEqual(4u, ReadUInt32BE(ihdr, 0)); // width
            Assert.AreEqual(3u, ReadUInt32BE(ihdr, 4)); // height
            Assert.AreEqual(16, ihdr[8]); // bit depth
            Assert.AreEqual(0, ihdr[9]);  // color type: grayscale
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteGrayscale16_DecompressedPixels_MatchExpectedScaling()
    {
        var path = TempPngPath();
        try
        {
            // 2x1: one sample at min (-> 0), one at max (-> 65535).
            var values = new float[] { -50f, 50f };
            PngHeightmapWriter.WriteGrayscale16(path, values, width: 2, height: 1, min: -50f, max: 50f);

            var chunks = ReadChunks(File.ReadAllBytes(path));
            var idat = chunks.Single(c => c.Type == "IDAT").Data;

            using var compressed = new MemoryStream(idat);
            using var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            zlib.CopyTo(decompressed);
            var raw = decompressed.ToArray();

            // 1 filter-type byte + 2 samples * 2 bytes each = 5 bytes for this single row.
            Assert.AreEqual(5, raw.Length);
            Assert.AreEqual(0, raw[0]); // filter type: None
            var sample0 = (raw[1] << 8) | raw[2];
            var sample1 = (raw[3] << 8) | raw[4];
            Assert.AreEqual(0, sample0);
            Assert.AreEqual(65535, sample1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteGrayscale16_FlatTile_DoesNotThrow_DespiteZeroRange()
    {
        var path = TempPngPath();
        try
        {
            var values = new float[] { 5f, 5f, 5f, 5f };
            PngHeightmapWriter.WriteGrayscale16(path, values, width: 2, height: 2, min: 5f, max: 5f);
            Assert.IsTrue(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void WriteGrayscale16_MismatchedArrayLength_Throws()
    {
        var path = TempPngPath();
        try
        {
            Assert.Throws<ArgumentException>(() =>
                PngHeightmapWriter.WriteGrayscale16(path, new float[3], width: 2, height: 2, min: 0, max: 1));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
