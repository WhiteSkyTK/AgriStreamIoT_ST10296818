using System.Buffers.Binary;

namespace AgriStreamIoT.Shared.Imaging;

/// <summary>Format and pixel size read from an image header.</summary>
public sealed record ImageInfo(bool IsImage, string Format, int Width, int Height)
{
    public double Megapixels => Width > 0 && Height > 0 ? Math.Round(Width * (double)Height / 1_000_000, 1) : 0;

    public static ImageInfo Unknown { get; } = new(false, "Unknown", 0, 0);
}

/// <summary>
/// Reads the format and dimensions of an image from its first bytes only, so the blob trigger
/// can validate a 30 MB drone photo without downloading all of it.
/// Supports JPEG, PNG, GIF, BMP, WebP and TIFF (TIFF: format only).
/// </summary>
public static class ImageInspector
{
    /// <summary>How many bytes to download for inspection (JPEG headers with large EXIF blocks need room).</summary>
    public const int HeaderBytes = 512 * 1024;

    public static ImageInfo Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12) return ImageInfo.Unknown;

        // PNG: 89 50 4E 47 0D 0A 1A 0A, then IHDR width/height (big-endian) at offset 16.
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            if (data.Length < 24) return new ImageInfo(true, "PNG", 0, 0);
            return new ImageInfo(true, "PNG",
                (int)BinaryPrimitives.ReadUInt32BigEndian(data[16..20]),
                (int)BinaryPrimitives.ReadUInt32BigEndian(data[20..24]));
        }

        // JPEG: FF D8, then walk the segments until a Start-Of-Frame marker.
        if (data[0] == 0xFF && data[1] == 0xD8)
            return InspectJpeg(data);

        // GIF: "GIF87a"/"GIF89a", little-endian width/height at offset 6.
        if (data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return new ImageInfo(true, "GIF",
                BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]));

        // BMP: "BM", BITMAPINFOHEADER width/height at offset 18.
        if (data[0] == 'B' && data[1] == 'M' && data.Length >= 26)
            return new ImageInfo(true, "BMP",
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[18..22])),
                Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..26])));

        // WebP: "RIFF" .... "WEBP"
        if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
            data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
            return InspectWebP(data);

        // TIFF (common for multispectral drone sensors): "II*\0" or "MM\0*".
        if ((data[0] == 'I' && data[1] == 'I' && data[2] == 0x2A && data[3] == 0) ||
            (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 0x2A))
            return new ImageInfo(true, "TIFF", 0, 0);

        return ImageInfo.Unknown;
    }

    private static ImageInfo InspectJpeg(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF) { i++; continue; }
            var marker = data[i + 1];
            if (marker == 0xFF) { i++; continue; }                 // fill byte
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; } // no length
            if (marker == 0xD9 || marker == 0xDA) break;           // end of image / start of scan

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 2, 2));
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isStartOfFrame)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(i + 7, 2));
                return new ImageInfo(true, "JPEG", width, height);
            }
            i += 2 + length;
        }
        return new ImageInfo(true, "JPEG", 0, 0);   // valid JPEG, frame header beyond the bytes read
    }

    private static ImageInfo InspectWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30) return new ImageInfo(true, "WebP", 0, 0);
        var chunk = System.Text.Encoding.ASCII.GetString(data.Slice(12, 4));
        switch (chunk)
        {
            case "VP8X":
                return new ImageInfo(true, "WebP",
                    1 + (data[24] | data[25] << 8 | data[26] << 16),
                    1 + (data[27] | data[28] << 8 | data[29] << 16));
            case "VP8L":
                var b = data.Slice(21, 4);
                var width = 1 + (((b[1] & 0x3F) << 8) | b[0]);
                var height = 1 + (((b[3] & 0x0F) << 10) | (b[2] << 2) | ((b[1] & 0xC0) >> 6));
                return new ImageInfo(true, "WebP", width, height);
            case "VP8 ":
                return new ImageInfo(true, "WebP",
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF,
                    BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF);
            default:
                return new ImageInfo(true, "WebP", 0, 0);
        }
    }

    /// <summary>Content type to use when the browser did not send a useful one.</summary>
    public static string ContentTypeFor(string format) => format switch
    {
        "PNG" => "image/png",
        "JPEG" => "image/jpeg",
        "GIF" => "image/gif",
        "BMP" => "image/bmp",
        "WebP" => "image/webp",
        "TIFF" => "image/tiff",
        _ => "application/octet-stream"
    };
}
