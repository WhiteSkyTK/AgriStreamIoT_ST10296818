using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace AgriStreamIoT.Web.Services.Simulation;

/// <summary>
/// Draws a synthetic aerial photo of crop rows (with a few stressed patches) and encodes it as PNG.
/// Used to simulate a burst of high-resolution drone uploads without needing real imagery.
/// </summary>
public static class SyntheticDroneImage
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Generate(int width, int height, int seed)
    {
        width = Math.Clamp(width, 64, 6000);
        height = Math.Clamp(height, 64, 6000);
        var random = new Random(seed);

        var angle = random.NextDouble() * Math.PI;
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        var rowSpacing = 14 + random.Next(0, 10);
        var patches = Enumerable.Range(0, random.Next(2, 6))
            .Select(_ => (X: random.Next(width), Y: random.Next(height), R: 60 + random.Next(Math.Max(61, Math.Min(width, height) / 5))))
            .ToArray();

        // Each scanline: filter byte 0 followed by RGB triples.
        var stride = width * 3 + 1;
        var raw = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var offset = y * stride;
            raw[offset] = 0;
            for (var x = 0; x < width; x++)
            {
                var phase = (x * cos + y * sin) / rowSpacing;
                var canopy = Math.Pow(0.5 + 0.5 * Math.Cos(2 * Math.PI * phase), 1.6);   // 0 = soil, 1 = plant

                var stress = 0.0;
                foreach (var p in patches)
                {
                    var dx = x - p.X;
                    var dy = y - p.Y;
                    var d2 = (dx * dx + dy * dy) / (double)(p.R * p.R);
                    if (d2 < 1) stress = Math.Max(stress, 1 - d2);
                }

                var noise = Hash(x, y, seed) * 18 - 9;
                // Soil (dry brown) -> healthy canopy (green) -> stressed canopy (straw yellow).
                var plantR = Lerp(54, 176, stress);
                var plantG = Lerp(122, 158, stress);
                var plantB = Lerp(48, 72, stress);
                var r = Lerp(122, plantR, canopy) + noise;
                var g = Lerp(94, plantG, canopy) + noise;
                var b = Lerp(64, plantB, canopy) + noise * 0.6;

                var i = offset + 1 + x * 3;
                raw[i] = ClampByte(r);
                raw[i + 1] = ClampByte(g);
                raw[i + 2] = ClampByte(b);
            }
        }

        return EncodePng(width, height, raw);
    }

    private static byte[] EncodePng(int width, int height, byte[] scanlines)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;   // bit depth
        header[9] = 2;   // colour type: RGB
        WriteChunk(png, "IHDR", header);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(scanlines, 0, scanlines.Length);
            WriteChunk(png, "IDAT", compressed.ToArray());
        }

        WriteChunk(png, "tEXt", Encoding.Latin1.GetBytes("Software\0AgriStream IoT synthetic drone survey"));
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        stream.Write(buffer);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (double)uint.MaxValue;
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static byte ClampByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}
