namespace LineScope.Core;

public abstract record FilterKind
{
    public sealed record GaussianBlur(double Radius) : FilterKind;
    public sealed record BoxBlur(double Radius) : FilterKind;
    public sealed record Median : FilterKind;
    public sealed record UnsharpMask(double Radius, double Intensity) : FilterKind;
    public sealed record NoiseReduction(double Level, double Sharpness) : FilterKind;
    public sealed record Sobel : FilterKind;
    public sealed record Laplacian : FilterKind;
    public sealed record Emboss : FilterKind;
    public sealed record Grayscale : FilterKind;
    public sealed record Invert : FilterKind;

    public string DisplayName => this switch
    {
        GaussianBlur g => $"Gaussian r={Fmt.Number(g.Radius)}",
        BoxBlur b => $"Box r={Fmt.Number(b.Radius)}",
        Median => "Median 3×3",
        UnsharpMask u => $"Sharpen r={Fmt.Number(u.Radius)} i={Fmt.Number(u.Intensity)}",
        NoiseReduction n => $"Denoise {Fmt.Number(n.Level)}/{Fmt.Number(n.Sharpness)}",
        Sobel => "Sobel",
        Laplacian => "Laplacian",
        Emboss => "Emboss",
        Grayscale => "Grayscale",
        Invert => "Invert",
        _ => "",
    };
}

/// <summary>
/// All filters run on the CPU and work directly on encoded values (no color management), with clamped edges.
/// On macOS the blur, median, unsharp-mask and noise-reduction filters came from Core Image; here they are
/// explicit implementations of the same operations:
/// <list type="bullet">
/// <item>Gaussian: separable kernel with σ = radius, truncated at 3σ (Core Image's radius is σ).</item>
/// <item>Box: separable box covering [−(r+½), r+½], with fractional coverage at the ends.</item>
/// <item>Median: 3×3 per-channel median.</item>
/// <item>Unsharp mask: src + intensity·(src − Gaussian(src, radius)).</item>
/// <item>Noise reduction: a threshold ("sigma") filter. Neighbors whose luminance differs from the center by less
/// than the noise level are averaged; pixels on edges (a neighbor above the threshold) are sharpened instead,
/// by <c>sharpness</c> times a 3×3 unsharp mask. This follows Core Image's documented behavior, not its exact kernel.</item>
/// </list>
/// The blurs work on premultiplied data, as Core Image did.
/// </summary>
public static class Filters
{
    public static PixelBuffer Apply(FilterKind filter, PixelBuffer src) => filter switch
    {
        FilterKind.GaussianBlur g => Premultiplied(src, p => GaussianBlur(p, src.Width, src.Height, g.Radius)),
        FilterKind.BoxBlur b => Premultiplied(src, p => BoxBlur(p, src.Width, src.Height, b.Radius)),
        FilterKind.Median => Premultiplied(src, p => Median3x3(p, src.Width, src.Height)),
        FilterKind.UnsharpMask u => Premultiplied(src, p => UnsharpMask(p, src.Width, src.Height, u.Radius, u.Intensity)),
        FilterKind.NoiseReduction n => Premultiplied(src, p => NoiseReduction(p, src.Width, src.Height, n.Level, n.Sharpness)),
        FilterKind.Sobel => Sobel(src),
        // Absolute response so the edges are visible.
        FilterKind.Laplacian => Convolve3x3(src, [0, 1, 0, 1, -4, 1, 0, 1, 0], absolute: true),
        FilterKind.Emboss => Convolve3x3(src, [-2, -1, 0, -1, 1, 1, 0, 1, 2], absolute: false),
        FilterKind.Grayscale => MapRgb(src, c =>
        {
            float y = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
            return new Rgba(y, y, y, c.A);
        }),
        FilterKind.Invert => MapRgb(src, c => new Rgba(1 - c.R, 1 - c.G, 1 - c.B, c.A)),
        _ => src,
    };

    private static PixelBuffer Premultiplied(PixelBuffer src, Func<float[], float[]> body) =>
        PixelBuffer.FromPremultiplied(src.Width, src.Height, body(src.Premultiplied()));

    // Separable filters on premultiplied RGBA

    /// <summary>Convolves every channel with a symmetric 1-D kernel (index <c>radius</c> is the center) along x, then y.</summary>
    internal static float[] ConvolveSeparable(float[] src, int w, int h, float[] kernel)
    {
        int r = kernel.Length / 2;
        var tmp = new float[src.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                for (int k = -r; k <= r; k++)
                {
                    int xx = Math.Clamp(x + k, 0, w - 1);
                    int i = (y * w + xx) * 4;
                    float wt = kernel[k + r];
                    a0 += src[i] * wt; a1 += src[i + 1] * wt; a2 += src[i + 2] * wt; a3 += src[i + 3] * wt;
                }
                int o = (y * w + x) * 4;
                tmp[o] = a0; tmp[o + 1] = a1; tmp[o + 2] = a2; tmp[o + 3] = a3;
            }
        });
        var output = new float[src.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float a0 = 0, a1 = 0, a2 = 0, a3 = 0;
                for (int k = -r; k <= r; k++)
                {
                    int yy = Math.Clamp(y + k, 0, h - 1);
                    int i = (yy * w + x) * 4;
                    float wt = kernel[k + r];
                    a0 += tmp[i] * wt; a1 += tmp[i + 1] * wt; a2 += tmp[i + 2] * wt; a3 += tmp[i + 3] * wt;
                }
                int o = (y * w + x) * 4;
                output[o] = a0; output[o + 1] = a1; output[o + 2] = a2; output[o + 3] = a3;
            }
        });
        return output;
    }

    internal static float[] GaussianKernel(double sigma)
    {
        if (sigma < 1e-3) return [1];
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new float[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++)
        {
            double v = Math.Exp(-(i * i) / (2 * sigma * sigma));
            k[i + r] = (float)v;
            sum += v;
        }
        for (int i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
        return k;
    }

    internal static float[] BoxKernel(double radius)
    {
        double half = Math.Max(radius, 0) + 0.5;
        int r = (int)Math.Ceiling(half - 0.5);
        var k = new float[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++)
        {
            double overlap = Math.Min(i + 0.5, half) - Math.Max(i - 0.5, -half);
            k[i + r] = (float)Math.Max(0, overlap);
            sum += k[i + r];
        }
        for (int i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
        return k;
    }

    private static float[] GaussianBlur(float[] p, int w, int h, double radius) =>
        ConvolveSeparable(p, w, h, GaussianKernel(radius));

    private static float[] BoxBlur(float[] p, int w, int h, double radius) =>
        ConvolveSeparable(p, w, h, BoxKernel(radius));

    private static float[] UnsharpMask(float[] p, int w, int h, double radius, double intensity)
    {
        var blurred = GaussianBlur(p, w, h, radius);
        float amount = (float)intensity;
        var output = new float[p.Length];
        for (int i = 0; i < p.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
                output[i + c] = p[i + c] + amount * (p[i + c] - blurred[i + c]);
            output[i + 3] = p[i + 3];
        }
        return output;
    }

    private static float[] Median3x3(float[] p, int w, int h)
    {
        var output = new float[p.Length];
        Parallel.For(0, h, () => new float[9], (y, _, window) =>
        {
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                for (int c = 0; c < 4; c++)
                {
                    int n = 0;
                    for (int ky = -1; ky <= 1; ky++)
                    {
                        int yy = Math.Clamp(y + ky, 0, h - 1);
                        for (int kx = -1; kx <= 1; kx++)
                        {
                            int xx = Math.Clamp(x + kx, 0, w - 1);
                            window[n++] = p[(yy * w + xx) * 4 + c];
                        }
                    }
                    Array.Sort(window);
                    output[o + c] = window[4];
                }
            }
            return window;
        }, _ => { });
        return output;
    }

    private static float[] NoiseReduction(float[] p, int w, int h, double level, double sharpness)
    {
        float threshold = (float)level;
        float amount = (float)sharpness;
        var luma = new float[w * h];
        for (int i = 0; i < luma.Length; i++)
            luma[i] = 0.2126f * p[i * 4] + 0.7152f * p[i * 4 + 1] + 0.0722f * p[i * 4 + 2];

        var output = new float[p.Length];
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int ci = y * w + x;
                float center = luma[ci];
                bool edge = false;
                float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                float m0 = 0, m1 = 0, m2 = 0, m3 = 0;
                int count = 0;
                for (int ky = -1; ky <= 1; ky++)
                {
                    int yy = Math.Clamp(y + ky, 0, h - 1);
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int xx = Math.Clamp(x + kx, 0, w - 1);
                        int ni = yy * w + xx;
                        int i = ni * 4;
                        m0 += p[i]; m1 += p[i + 1]; m2 += p[i + 2]; m3 += p[i + 3];
                        if (MathF.Abs(luma[ni] - center) < threshold)
                        {
                            s0 += p[i]; s1 += p[i + 1]; s2 += p[i + 2]; s3 += p[i + 3];
                            count++;
                        }
                        else
                        {
                            edge = true;
                        }
                    }
                }
                int o = ci * 4;
                if (edge || count == 0)
                {
                    // Edge: unsharp mask against the 3×3 mean.
                    output[o] = p[o] + amount * (p[o] - m0 / 9);
                    output[o + 1] = p[o + 1] + amount * (p[o + 1] - m1 / 9);
                    output[o + 2] = p[o + 2] + amount * (p[o + 2] - m2 / 9);
                    output[o + 3] = p[o + 3];
                }
                else
                {
                    output[o] = s0 / count;
                    output[o + 1] = s1 / count;
                    output[o + 2] = s2 / count;
                    output[o + 3] = s3 / count;
                }
            }
        });
        return output;
    }

    // CPU filters on straight RGB

    private static PixelBuffer MapRgb(PixelBuffer src, Func<Rgba, Rgba> f)
    {
        var output = src.Clone();
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
                output[x, y] = f(src[x, y]);
        return output;
    }

    /// <summary>3×3 convolution on RGB with clamped edges; alpha is preserved.</summary>
    internal static PixelBuffer Convolve3x3(PixelBuffer src, float[] k, bool absolute)
    {
        var output = src.Clone();
        int w = src.Width, h = src.Height;
        var s = src.Data;
        var o = output.Data;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float a0 = 0, a1 = 0, a2 = 0;
                for (int ky = -1; ky <= 1; ky++)
                {
                    int yy = Math.Clamp(y + ky, 0, h - 1);
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int xx = Math.Clamp(x + kx, 0, w - 1);
                        float wgt = k[(ky + 1) * 3 + (kx + 1)];
                        int i = (yy * w + xx) * 4;
                        a0 += s[i] * wgt;
                        a1 += s[i + 1] * wgt;
                        a2 += s[i + 2] * wgt;
                    }
                }
                if (absolute) { a0 = MathF.Abs(a0); a1 = MathF.Abs(a1); a2 = MathF.Abs(a2); }
                int j = (y * w + x) * 4;
                o[j] = Math.Clamp(a0, 0, 1);
                o[j + 1] = Math.Clamp(a1, 0, 1);
                o[j + 2] = Math.Clamp(a2, 0, 1);
            }
        });
        return output;
    }

    /// <summary>Per-channel Sobel gradient magnitude.</summary>
    internal static PixelBuffer Sobel(PixelBuffer src)
    {
        float[] gx = [-1, 0, 1, -2, 0, 2, -1, 0, 1];
        float[] gy = [-1, -2, -1, 0, 0, 0, 1, 2, 1];
        var output = src.Clone();
        int w = src.Width, h = src.Height;
        var s = src.Data;
        var o = output.Data;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float sx0 = 0, sx1 = 0, sx2 = 0, sy0 = 0, sy1 = 0, sy2 = 0;
                for (int ky = -1; ky <= 1; ky++)
                {
                    int yy = Math.Clamp(y + ky, 0, h - 1);
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int xx = Math.Clamp(x + kx, 0, w - 1);
                        int ki = (ky + 1) * 3 + (kx + 1);
                        int i = (yy * w + xx) * 4;
                        sx0 += s[i] * gx[ki]; sy0 += s[i] * gy[ki];
                        sx1 += s[i + 1] * gx[ki]; sy1 += s[i + 1] * gy[ki];
                        sx2 += s[i + 2] * gx[ki]; sy2 += s[i + 2] * gy[ki];
                    }
                }
                int j = (y * w + x) * 4;
                o[j] = MathF.Min(MathF.Sqrt(sx0 * sx0 + sy0 * sy0), 1);
                o[j + 1] = MathF.Min(MathF.Sqrt(sx1 * sx1 + sy1 * sy1), 1);
                o[j + 2] = MathF.Min(MathF.Sqrt(sx2 * sx2 + sy2 * sy2), 1);
            }
        });
        return output;
    }
}
