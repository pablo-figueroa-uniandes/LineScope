namespace LineScope.Core;

/// <summary>A color sample with straight (non-premultiplied) alpha, sRGB-encoded components in 0…1.</summary>
public readonly record struct Rgba(float R, float G, float B, float A = 1);

/// <summary>
/// Interleaved RGBA Float32 image. Row 0 is the top row.
/// Components are sRGB-encoded (not linearized) with straight alpha, so values match what is displayed.
/// Treat instances as values: operations return new buffers instead of mutating their input.
/// </summary>
public sealed class PixelBuffer : IEquatable<PixelBuffer>
{
    public int Width { get; }
    public int Height { get; }
    public float[] Data { get; }

    public PixelBuffer(int width, int height, float[] data)
    {
        if (width <= 0 || height <= 0 || data.Length != width * height * 4)
            throw new ArgumentException("Invalid pixel buffer dimensions.");
        Width = width;
        Height = height;
        Data = data;
    }

    public PixelBuffer(int width, int height) : this(width, height, new Rgba(0, 0, 0, 1)) { }

    public PixelBuffer(int width, int height, Rgba fill)
        : this(width, height, new float[Math.Max(width, 0) * Math.Max(height, 0) * 4])
    {
        for (int i = 0; i < Data.Length; i += 4)
        {
            Data[i] = fill.R;
            Data[i + 1] = fill.G;
            Data[i + 2] = fill.B;
            Data[i + 3] = fill.A;
        }
    }

    public Rgba this[int x, int y]
    {
        get
        {
            int i = (y * Width + x) * 4;
            return new Rgba(Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
        }
        set
        {
            int i = (y * Width + x) * 4;
            Data[i] = value.R;
            Data[i + 1] = value.G;
            Data[i + 2] = value.B;
            Data[i + 3] = value.A;
        }
    }

    public PixelBuffer Clone() => new(Width, Height, (float[])Data.Clone());

    // Premultiplication (used by resampling and the blur filters, which work on premultiplied data)

    internal float[] Premultiplied()
    {
        var output = (float[])Data.Clone();
        for (int i = 0; i < output.Length; i += 4)
        {
            float a = output[i + 3];
            output[i] *= a;
            output[i + 1] *= a;
            output[i + 2] *= a;
        }
        return output;
    }

    /// <summary>Un-premultiplies (in place) and clamps to 0…1.</summary>
    internal static PixelBuffer FromPremultiplied(int width, int height, float[] p)
    {
        for (int i = 0; i < p.Length; i += 4)
        {
            float a = Math.Clamp(p[i + 3], 0, 1);
            p[i + 3] = a;
            if (a > 1e-6f)
            {
                p[i] = Math.Clamp(p[i] / a, 0, 1);
                p[i + 1] = Math.Clamp(p[i + 1] / a, 0, 1);
                p[i + 2] = Math.Clamp(p[i + 2] / a, 0, 1);
            }
            else
            {
                p[i] = 0;
                p[i + 1] = 0;
                p[i + 2] = 0;
            }
        }
        return new PixelBuffer(width, height, p);
    }

    // 8-bit bridging (the Windows counterpart of the Core Graphics bridge). The byte layout is
    // premultiplied BGRA, which is what WIC's and WPF's Pbgra32 pixel format expects.

    /// <summary>Converts decoded 8-bit premultiplied BGRA pixels to a float buffer.</summary>
    public static PixelBuffer FromPremultipliedBgra8(int width, int height, byte[] bytes)
    {
        if (bytes.Length < width * height * 4) throw new ArgumentException("Too few bytes.");
        var p = new float[width * height * 4];
        for (int i = 0; i < p.Length; i += 4)
        {
            p[i] = bytes[i + 2] / 255f;
            p[i + 1] = bytes[i + 1] / 255f;
            p[i + 2] = bytes[i] / 255f;
            p[i + 3] = bytes[i + 3] / 255f;
        }
        return FromPremultiplied(width, height, p);
    }

    /// <summary>Renders the buffer as 8-bit premultiplied BGRA for display or export.</summary>
    public byte[] ToPremultipliedBgra8()
    {
        var bytes = new byte[Data.Length];
        for (int i = 0; i < Data.Length; i += 4)
        {
            float a = Math.Clamp(Data[i + 3], 0, 1);
            bytes[i + 2] = ToByte(Math.Clamp(Data[i], 0, 1) * a);
            bytes[i + 1] = ToByte(Math.Clamp(Data[i + 1], 0, 1) * a);
            bytes[i] = ToByte(Math.Clamp(Data[i + 2], 0, 1) * a);
            bytes[i + 3] = ToByte(a);
        }
        return bytes;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(v * 255, MidpointRounding.AwayFromZero);

    public bool Equals(PixelBuffer? other) =>
        other is not null && Width == other.Width && Height == other.Height && Data.AsSpan().SequenceEqual(other.Data);

    public override bool Equals(object? obj) => Equals(obj as PixelBuffer);

    public override int GetHashCode() => HashCode.Combine(Width, Height, Data.Length);
}
