using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LineScope.Core;

namespace LineScope.App.Model;

/// <summary>Facts about the file as decoded, before conversion to the working float buffer.</summary>
public sealed record SourceInfo(
    string FileType,
    int PixelWidth,
    int PixelHeight,
    int BitsPerComponent,
    int BitsPerPixel,
    string ColorSpaceName,
    bool HasAlpha,
    double? Dpi);

/// <summary>
/// Read-only image document. Pixels are decoded once (with WIC) into a <see cref="PixelBuffer"/>. EXIF orientation
/// is ignored on purpose so analysis runs on the stored pixel grid. Images with an embedded ICC profile are
/// converted to sRGB, as on macOS.
/// </summary>
public sealed class ImageDocument
{
    public PixelBuffer Buffer { get; }
    public BitmapSource DisplayImage { get; }
    public SourceInfo Info { get; }

    private ImageDocument(PixelBuffer buffer, SourceInfo info)
    {
        Buffer = buffer;
        DisplayImage = Bitmaps.FromBuffer(buffer);
        Info = info;
    }

    /// <summary>Decodes an image file. Safe to call off the UI thread.</summary>
    public static ImageDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0) throw new InvalidDataException("The file contains no image.");
        BitmapFrame frame = decoder.Frames[0];
        int w = frame.PixelWidth, h = frame.PixelHeight;
        if (w <= 0 || h <= 0) throw new InvalidDataException("The image is empty.");

        var profile = frame.ColorContexts is { Count: > 0 } contexts ? contexts[0] : null;
        BitmapSource sRGB = ToSrgbPbgra32(frame, profile);
        var bytes = new byte[w * h * 4];
        sRGB.CopyPixels(bytes, w * 4, 0);
        var buffer = PixelBuffer.FromPremultipliedBgra8(w, h, bytes);

        var format = frame.Format;
        int channels = Math.Max(format.Masks.Count, 1);
        string codec = decoder.CodecInfo?.FriendlyName ?? "Image";
        var info = new SourceInfo(
            FileType: codec.Replace(" Decoder", "", StringComparison.OrdinalIgnoreCase).Trim() + " image",
            PixelWidth: w,
            PixelHeight: h,
            BitsPerComponent: format.BitsPerPixel / channels,
            BitsPerPixel: format.BitsPerPixel,
            ColorSpaceName: profile is null ? "sRGB (no embedded profile)" : ProfileDescription(profile) ?? "Embedded ICC profile",
            HasAlpha: HasAlphaChannel(format) || (frame.Palette?.Colors.Any(c => c.A < 255) ?? false),
            Dpi: frame.DpiX > 0 ? frame.DpiX : null);
        return new ImageDocument(buffer, info);
    }

    /// <summary>A generated test pattern, shown in the same analysis window as a file.</summary>
    public static ImageDocument FromPattern(TestPatternSpec spec)
    {
        var buffer = TestPatternGenerator.Make(spec);
        return new ImageDocument(buffer, new SourceInfo(
            FileType: "Generated test pattern",
            PixelWidth: buffer.Width,
            PixelHeight: buffer.Height,
            BitsPerComponent: 32,
            BitsPerPixel: 128,
            ColorSpaceName: "sRGB (float)",
            HasAlpha: false,
            Dpi: null));
    }

    private static BitmapSource ToSrgbPbgra32(BitmapFrame frame, ColorContext? profile)
    {
        if (profile is not null)
        {
            try
            {
                var converted = new ColorConvertedBitmap(frame, profile, new ColorContext(PixelFormats.Bgra32), PixelFormats.Pbgra32);
                converted.Freeze();
                return converted;
            }
            catch (Exception)
            {
                // Unsupported or broken profile: fall back to the stored values.
            }
        }
        var plain = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        plain.Freeze();
        return plain;
    }

    private static bool HasAlphaChannel(PixelFormat f) =>
        f == PixelFormats.Bgra32 || f == PixelFormats.Pbgra32 || f == PixelFormats.Rgba64 || f == PixelFormats.Prgba64
        || f == PixelFormats.Rgba128Float || f == PixelFormats.Prgba128Float;

    /// <summary>Reads the profile's description tag ('desc' in ICC v2, 'mluc' in v4).</summary>
    private static string? ProfileDescription(ColorContext profile)
    {
        try
        {
            using var s = profile.OpenProfileStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var d = ms.ToArray();
            if (d.Length < 132) return null;
            uint count = BE32(d, 128);
            for (int i = 0; i < count && 132 + i * 12 + 12 <= d.Length; i++)
            {
                int t = 132 + i * 12;
                if (Encoding.ASCII.GetString(d, t, 4) != "desc") continue;
                int off = (int)BE32(d, t + 4), size = (int)BE32(d, t + 8);
                if (off + size > d.Length || size < 12) return null;
                string type = Encoding.ASCII.GetString(d, off, 4);
                if (type == "desc")
                {
                    int len = (int)BE32(d, off + 8);
                    return Encoding.ASCII.GetString(d, off + 12, Math.Max(0, Math.Min(len, size - 12))).TrimEnd('\0');
                }
                if (type == "mluc" && size >= 28)
                {
                    int recLen = (int)BE32(d, off + 20), recOff = (int)BE32(d, off + 24);
                    if (off + recOff + recLen <= d.Length)
                        return Encoding.BigEndianUnicode.GetString(d, off + recOff, recLen).TrimEnd('\0');
                }
                return null;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static uint BE32(byte[] d, int i) => (uint)(d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3]);
}

public static class Bitmaps
{
    /// <summary>Renders a buffer as a frozen 8-bit premultiplied BGRA bitmap at 96 dpi (1 pixel = 1 DIP).</summary>
    public static BitmapSource FromBuffer(PixelBuffer buffer)
    {
        var bitmap = BitmapSource.Create(buffer.Width, buffer.Height, 96, 96, PixelFormats.Pbgra32, null,
            buffer.ToPremultipliedBgra8(), buffer.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
