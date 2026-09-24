namespace LineScope.Core;

public enum ResampleMethod { Nearest, Tent, CatmullRom, Mitchell, Lanczos3, Sinc, Area }

public static class ResampleMethodInfo
{
    public static readonly ResampleMethod[] All = Enum.GetValues<ResampleMethod>();

    public static string DisplayName(this ResampleMethod m) => m switch
    {
        ResampleMethod.Nearest => "Nearest Neighbor",
        ResampleMethod.Tent => "Tent (Bilinear)",
        ResampleMethod.CatmullRom => "Catmull-Rom (Bicubic)",
        ResampleMethod.Mitchell => "Mitchell-Netravali",
        ResampleMethod.Lanczos3 => "Lanczos-3",
        ResampleMethod.Sinc => $"Sinc (truncated, r={(int)Resampler.SincRadius})",
        _ => "Area Average",
    };

    public static string ShortName(this ResampleMethod m) => m switch
    {
        ResampleMethod.Nearest => "Nearest",
        ResampleMethod.Tent => "Tent",
        ResampleMethod.CatmullRom => "Catmull-Rom",
        ResampleMethod.Mitchell => "Mitchell",
        ResampleMethod.Lanczos3 => "Lanczos",
        ResampleMethod.Sinc => "Sinc",
        _ => "Area",
    };
}

/// <summary>
/// Separable resampling with explicit, textbook kernels.
/// </summary>
/// <remarks>
/// Coordinates follow the pixel-center convention: input pixel <c>j</c> covers <c>[j, j+1)</c> and its center is <c>j + 0.5</c>.
/// Output pixel <c>i</c> maps to input position <c>(i + 0.5) / scale</c>.
/// <list type="bullet">
/// <item><c>Nearest</c> picks one source pixel and does no prefiltering, so it aliases when downscaling.</item>
/// <item>The kernel methods (tent, Catmull-Rom, Mitchell, Lanczos, sinc) widen their kernel by <c>1/scale</c>
/// when downscaling (antialiased).</item>
/// <item><c>Area</c> weights source pixels by how much of the output pixel's footprint they cover.</item>
/// </list>
/// </remarks>
public static class Resampler
{
    /// <summary>Half-width of the truncated sinc kernel, in source pixels (before widening).</summary>
    public const double SincRadius = 8.0;

    public static int OutputSize(int size, double scale) =>
        Math.Max(1, (int)Math.Round(size * scale, MidpointRounding.AwayFromZero));

    public static PixelBuffer Resample(PixelBuffer src, double scale, ResampleMethod method) =>
        Resample(src, OutputSize(src.Width, scale), OutputSize(src.Height, scale), method);

    public static PixelBuffer Resample(PixelBuffer src, int width, int height, ResampleMethod method)
    {
        var premul = src.Premultiplied();
        var xContrib = Contributions(src.Width, width, method);
        var yContrib = Contributions(src.Height, height, method);

        // Horizontal pass: (src.Width x src.Height) -> (width x src.Height)
        var tmp = new float[width * src.Height * 4];
        Parallel.For(0, src.Height, y =>
        {
            int srcRow = y * src.Width * 4;
            int dstRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                var c = xContrib[x];
                float r = 0, g = 0, b = 0, a = 0;
                for (int k = 0; k < c.Weights.Length; k++)
                {
                    float w = c.Weights[k];
                    int si = srcRow + (c.Start + k) * 4;
                    r += premul[si] * w;
                    g += premul[si + 1] * w;
                    b += premul[si + 2] * w;
                    a += premul[si + 3] * w;
                }
                int di = dstRow + x * 4;
                tmp[di] = r;
                tmp[di + 1] = g;
                tmp[di + 2] = b;
                tmp[di + 3] = a;
            }
        });

        // Vertical pass: (width x src.Height) -> (width x height)
        var output = new float[width * height * 4];
        int rowStride = width * 4;
        Parallel.For(0, height, y =>
        {
            var c = yContrib[y];
            int dstRow = y * rowStride;
            for (int k = 0; k < c.Weights.Length; k++)
            {
                float w = c.Weights[k];
                int srcRow = (c.Start + k) * rowStride;
                for (int i = 0; i < rowStride; i++)
                    output[dstRow + i] += tmp[srcRow + i] * w;
            }
        });

        return PixelBuffer.FromPremultiplied(width, height, output);
    }

    // Kernels

    internal readonly record struct Contribution(int Start, float[] Weights);

    internal static Contribution[] Contributions(int inSize, int outSize, ResampleMethod method)
    {
        double scale = (double)outSize / inSize;
        var result = new Contribution[outSize];

        for (int i = 0; i < outSize; i++)
        {
            double center = (i + 0.5) / scale;
            switch (method)
            {
                case ResampleMethod.Nearest:
                {
                    int j = Math.Clamp((int)Math.Floor(center), 0, inSize - 1);
                    result[i] = new Contribution(j, [1]);
                    break;
                }
                case ResampleMethod.Area:
                {
                    // Output pixel footprint in input coordinates, exact coverage weights.
                    double half = 0.5 / scale;
                    double lo = center - half;
                    double hi = center + half;
                    int first = Math.Max(0, (int)Math.Floor(lo));
                    int last = Math.Min(inSize - 1, (int)Math.Ceiling(hi) - 1);
                    var weights = new List<float>();
                    for (int j = first; j <= last; j++)
                    {
                        double overlap = Math.Min(hi, j + 1) - Math.Max(lo, j);
                        weights.Add((float)Math.Max(0, overlap));
                    }
                    result[i] = Normalized(first, weights, center, inSize);
                    break;
                }
                default:
                {
                    double filterScale = Math.Max(1, 1 / scale);
                    double support = KernelSupport(method) * filterScale;
                    int first = Math.Max(0, (int)Math.Floor(center - support));
                    int last = Math.Min(inSize - 1, (int)Math.Ceiling(center + support));
                    var weights = new List<float>();
                    for (int j = first; j <= last; j++)
                    {
                        double x = (j + 0.5 - center) / filterScale;
                        weights.Add((float)Kernel(method, x));
                    }
                    result[i] = Normalized(first, weights, center, inSize);
                    break;
                }
            }
        }
        return result;
    }

    private static Contribution Normalized(int start, List<float> weights, double center, int inSize)
    {
        // Trim zero weights at both ends to keep inner loops short.
        int first = 0;
        int last = weights.Count - 1;
        while (first <= last && weights[first] == 0) first++;
        while (last >= first && weights[last] == 0) last--;
        int fallback = Math.Clamp((int)Math.Floor(center), 0, inSize - 1);
        if (first > last) return new Contribution(fallback, [1]);
        var trimmed = weights.GetRange(first, last - first + 1);
        float sum = 0;
        foreach (float w in trimmed) sum += w;
        if (!(Math.Abs(sum) > 1e-12)) return new Contribution(fallback, [1]);
        return new Contribution(start + first, trimmed.Select(w => w / sum).ToArray());
    }

    internal static double KernelSupport(ResampleMethod method) => method switch
    {
        ResampleMethod.Nearest or ResampleMethod.Area => 0.5,
        ResampleMethod.Tent => 1,
        ResampleMethod.CatmullRom or ResampleMethod.Mitchell => 2,
        ResampleMethod.Lanczos3 => 3,
        _ => SincRadius,
    };

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-9) return 1;
        double px = Math.PI * x;
        return Math.Sin(px) / px;
    }

    /// <summary>Mitchell-Netravali / Keys family: (B, C) = (1/3, 1/3) is Mitchell, (0, 1/2) is Catmull-Rom.</summary>
    private static double BcSpline(double ax, double b, double c)
    {
        if (ax < 1)
            return ((12 - 9 * b - 6 * c) * ax * ax * ax + (-18 + 12 * b + 6 * c) * ax * ax + (6 - 2 * b)) / 6;
        if (ax < 2)
            return ((-b - 6 * c) * ax * ax * ax + (6 * b + 30 * c) * ax * ax
                + (-12 * b - 48 * c) * ax + (8 * b + 24 * c)) / 6;
        return 0;
    }

    internal static double Kernel(ResampleMethod method, double x)
    {
        double ax = Math.Abs(x);
        return method switch
        {
            ResampleMethod.Nearest or ResampleMethod.Area => ax < 0.5 ? 1 : 0,
            ResampleMethod.Tent => Math.Max(0, 1 - ax),
            ResampleMethod.CatmullRom => BcSpline(ax, 0, 0.5),
            ResampleMethod.Mitchell => BcSpline(ax, 1.0 / 3, 1.0 / 3),
            ResampleMethod.Lanczos3 => ax < 3 ? Sinc(ax) * Sinc(ax / 3) : 0,
            // Ideal low-pass, truncated (rectangular window): expect ringing near edges.
            _ => ax < SincRadius ? Sinc(ax) : 0,
        };
    }
}
