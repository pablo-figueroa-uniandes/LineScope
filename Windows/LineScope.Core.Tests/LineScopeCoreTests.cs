using LineScope.Core;

namespace LineScope.Core.Tests;

internal static class Helpers
{
    public static PixelBuffer Gradient(int width, int height)
    {
        var buf = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                buf[x, y] = new Rgba((float)x / width, (float)y / height, 0.25f, 1);
        return buf;
    }

    public static void Near(double expected, double actual, double accuracy, string? label = null) =>
        Assert.True(Math.Abs(expected - actual) <= accuracy,
            $"{label} expected {expected} ± {accuracy}, got {actual}");
}

public class SpectrumTests
{
    [Fact]
    public void SinePeak()
    {
        const int n = 256, k = 16;
        var values = Enumerable.Range(0, n).Select(i => (float)Math.Sin(2 * Math.PI * k * i / n)).ToArray();
        var s = SpectrumAnalyzer.Compute(values, 1, WindowFunction.None, removeMean: true);
        Assert.Equal(256, s.FftSize);
        Assert.Equal(k, s.PeakIndex);
        Helpers.Near(1, s.Amplitudes[k], 1e-3);
        Helpers.Near((double)k / n, s.CyclesPerPixel[k], 1e-9);
        Helpers.Near(0.5, s.CyclesPerPixel[^1], 1e-9);
    }

    [Fact]
    public void DCAmplitude()
    {
        var s = SpectrumAnalyzer.Compute(Enumerable.Repeat(0.3f, 100).ToArray(), 1, WindowFunction.None, removeMean: false);
        Assert.Equal(128, s.FftSize);
        Helpers.Near(0.3, s.Amplitudes[0], 1e-4);
    }

    [Fact]
    public void FftMatchesNaiveDft()
    {
        var rng = new Random(3);
        var x = Enumerable.Range(0, 64).Select(_ => new System.Numerics.Complex(rng.NextDouble(), 0)).ToArray();
        var fast = (System.Numerics.Complex[])x.Clone();
        Fft.Forward(fast);
        for (int k = 0; k < x.Length; k++)
        {
            System.Numerics.Complex sum = 0;
            for (int j = 0; j < x.Length; j++)
                sum += x[j] * System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * j * k / x.Length);
            Helpers.Near(0, (sum - fast[k]).Magnitude, 1e-9, $"bin {k}");
        }
    }
}

public class ResamplingTests
{
    [Fact]
    public void KernelShapes()
    {
        // Interpolating kernels are 1 at 0 and 0 at nonzero integers.
        foreach (var method in new[] { ResampleMethod.Tent, ResampleMethod.CatmullRom, ResampleMethod.Lanczos3, ResampleMethod.Sinc })
        {
            Helpers.Near(1, Resampler.Kernel(method, 0), 1e-12, method.ToString());
            for (int k = 1; k <= 3; k++)
                Helpers.Near(0, Resampler.Kernel(method, k), 1e-12, $"{method} at {k}");
        }
        // Mitchell-Netravali (B = C = 1/3) is approximating: k(0) = 8/9, k(1) = 1/18.
        Helpers.Near(8.0 / 9, Resampler.Kernel(ResampleMethod.Mitchell, 0), 1e-12);
        Helpers.Near(1.0 / 18, Resampler.Kernel(ResampleMethod.Mitchell, 1), 1e-12);
        Helpers.Near(0, Resampler.Kernel(ResampleMethod.Mitchell, 2), 1e-12);
        Helpers.Near(0.5625, Resampler.Kernel(ResampleMethod.CatmullRom, 0.5), 1e-12);
        // Sinc is truncated at its radius.
        Assert.Equal(0, Resampler.Kernel(ResampleMethod.Sinc, Resampler.SincRadius + 0.5));
    }

    [Fact]
    public void IdentityAtScaleOne()
    {
        var src = Helpers.Gradient(17, 9);
        // Mitchell is approximating (slightly smoothing), so it is not an identity at ×1.
        foreach (var method in ResampleMethodInfo.All.Where(m => m != ResampleMethod.Mitchell))
        {
            var output = Resampler.Resample(src, 1, method);
            Assert.Equal(17, output.Width);
            Assert.Equal(9, output.Height);
            for (int i = 0; i < src.Data.Length; i++)
                Helpers.Near(src.Data[i], output.Data[i], 1e-5, method.ToString());
        }
    }

    [Fact]
    public void AreaHalvesAverages()
    {
        var src = new PixelBuffer(4, 1);
        float[] values = [0, 1, 0.2f, 0.6f];
        for (int x = 0; x < values.Length; x++) src[x, 0] = new Rgba(values[x], values[x], values[x]);
        var output = Resampler.Resample(src, 0.5, ResampleMethod.Area);
        Assert.Equal(2, output.Width);
        Helpers.Near(0.5, output[0, 0].R, 1e-5);
        Helpers.Near(0.4, output[1, 0].R, 1e-5);
    }

    [Fact]
    public void ConstantImageStaysConstant()
    {
        var src = new PixelBuffer(13, 7, new Rgba(0.4f, 0.5f, 0.6f));
        foreach (var method in ResampleMethodInfo.All)
            foreach (double scale in new[] { 0.3, 0.5, 2.0, 3.7 })
            {
                var output = Resampler.Resample(src, scale, method);
                for (int i = 0; i < output.Data.Length; i += 4)
                    Helpers.Near(0.4, output.Data[i], 1e-4, $"{method} {scale}");
            }
    }
}

public class ColorTests
{
    [Fact]
    public void HslRoundTrip()
    {
        foreach (var c in new[] { new Rgba(0.2f, 0.7f, 0.4f), new Rgba(1, 0, 0), new Rgba(0.1f, 0.1f, 0.9f), new Rgba(0.5f, 0.5f, 0.5f) })
        {
            var hsl = ColorConversion.Hsl(c);
            var back = ColorConversion.FromHsl(hsl.H, hsl.S, hsl.L);
            Helpers.Near(c.R, back.R, 1e-5);
            Helpers.Near(c.G, back.G, 1e-5);
            Helpers.Near(c.B, back.B, 1e-5);
        }
        Helpers.Near(240, ColorConversion.Hsl(new Rgba(0, 0, 1)).H, 1e-4);
    }

    [Fact]
    public void CmykRoundTrip()
    {
        foreach (var c in new[] { new Rgba(0.2f, 0.7f, 0.4f), new Rgba(0, 0, 0), new Rgba(1, 1, 1) })
        {
            var v = ColorConversion.Cmyk(c);
            var back = ColorConversion.FromCmyk(v.C, v.M, v.Y, v.K);
            Helpers.Near(c.R, back.R, 1e-5);
            Helpers.Near(c.G, back.G, 1e-5);
            Helpers.Near(c.B, back.B, 1e-5);
        }
    }
}

public class LineSamplerTests
{
    [Fact]
    public void SampleCountScalesWithResample()
    {
        var src = Helpers.Gradient(200, 100);
        var half = Resampler.Resample(src, 0.5, ResampleMethod.Tent);
        var a = LineSampler.Sample(src, LineSegment.Initial);
        var b = LineSampler.Sample(half, LineSegment.Initial);
        Assert.Equal(201, a.Colors.Length);
        Assert.Equal(101, b.Colors.Length);
        Helpers.Near(1, a.Spacing, 1e-9);
    }

    [Fact]
    public void SamplesFollowContent()
    {
        var src = Helpers.Gradient(100, 100);
        var line = new LineSegment(new NormalizedPoint(0.505, 0.1), new NormalizedPoint(0.505, 0.9));
        var s = LineSampler.Sample(src, line);
        // Vertical line through pixel column 50: red is constant, green increases.
        Helpers.Near(0.5, s.Colors[0].R, 1e-4);
        Helpers.Near(0.5, s.Colors[^1].R, 1e-4);
        Assert.True(s.Colors[0].G < s.Colors[^1].G);
    }
}

public class BitmapBridgeTests
{
    [Fact]
    public void Bgra8RoundTrip()
    {
        var src = Helpers.Gradient(32, 16);
        var bytes = src.ToPremultipliedBgra8();
        var back = PixelBuffer.FromPremultipliedBgra8(32, 16, bytes);
        Assert.Equal(32, back.Width);
        Assert.Equal(16, back.Height);
        for (int i = 0; i < src.Data.Length; i++)
            Helpers.Near(src.Data[i], back.Data[i], 1.0 / 255 + 1e-4);
    }

    [Fact]
    public void Bgra8ChannelOrder()
    {
        var src = new PixelBuffer(1, 1, new Rgba(1, 0.5f, 0, 1));
        var bytes = src.ToPremultipliedBgra8();
        Assert.Equal(new byte[] { 0, 128, 255, 255 }, bytes);
    }
}

public class FilterTests
{
    private static readonly FilterKind[] AllFilters =
    [
        new FilterKind.GaussianBlur(2), new FilterKind.BoxBlur(2), new FilterKind.Median(),
        new FilterKind.UnsharpMask(2, 0.5), new FilterKind.NoiseReduction(0.02, 0.4), new FilterKind.Sobel(),
        new FilterKind.Laplacian(), new FilterKind.Emboss(), new FilterKind.Grayscale(), new FilterKind.Invert(),
    ];

    [Fact]
    public void FiltersKeepSize()
    {
        var src = Helpers.Gradient(24, 12);
        foreach (var f in AllFilters)
        {
            var output = Filters.Apply(f, src);
            Assert.Equal(24, output.Width);
            Assert.Equal(12, output.Height);
            Assert.All(output.Data, v => Assert.InRange(v, 0f, 1f));
        }
        var blurred = Filters.Apply(new FilterKind.GaussianBlur(1), src);
        // Horizontal gradient stays roughly monotone and in range after blur.
        Assert.True(blurred[2, 6].R < blurred[20, 6].R);
    }

    [Fact]
    public void SmoothingFiltersKeepConstantImages()
    {
        var src = new PixelBuffer(9, 7, new Rgba(0.3f, 0.6f, 0.9f));
        foreach (var f in AllFilters.Where(f => f is FilterKind.GaussianBlur or FilterKind.BoxBlur or FilterKind.Median
                     or FilterKind.UnsharpMask or FilterKind.NoiseReduction))
        {
            var output = Filters.Apply(f, src);
            for (int i = 0; i < output.Data.Length; i += 4)
            {
                Helpers.Near(0.3, output.Data[i], 1e-5, f.DisplayName);
                Helpers.Near(0.9, output.Data[i + 2], 1e-5, f.DisplayName);
            }
        }
    }

    [Fact]
    public void KernelsAreNormalized()
    {
        foreach (double r in new[] { 0.0, 0.4, 1, 2.5, 10 })
        {
            Helpers.Near(1, Filters.GaussianKernel(r).Sum(), 1e-5, $"gaussian {r}");
            Helpers.Near(1, Filters.BoxKernel(r).Sum(), 1e-5, $"box {r}");
        }
        Assert.Equal(new float[] { 1 }, Filters.BoxKernel(0));
        Assert.Equal(3, Filters.BoxKernel(1).Length);
    }

    [Fact]
    public void MedianRemovesAnImpulse()
    {
        var src = new PixelBuffer(5, 5, new Rgba(0.2f, 0.2f, 0.2f));
        src[2, 2] = new Rgba(1, 1, 1);
        var output = Filters.Apply(new FilterKind.Median(), src);
        Helpers.Near(0.2, output[2, 2].R, 1e-6);
    }

    [Fact]
    public void NoiseReductionSmoothsSmallVariations()
    {
        var src = new PixelBuffer(5, 5, new Rgba(0.5f, 0.5f, 0.5f));
        src[2, 2] = new Rgba(0.505f, 0.505f, 0.505f);
        var output = Filters.Apply(new FilterKind.NoiseReduction(0.02, 0.4), src);
        Assert.True(Math.Abs(output[2, 2].R - 0.5) < 0.002);
    }
}

public class TestPatternTests
{
    [Fact]
    public void AllPatternsGenerateRequestedSize()
    {
        foreach (var kind in TestPatternKindInfo.All)
        {
            var buf = TestPatternGenerator.Make(new TestPatternSpec(kind, 64, 48));
            Assert.Equal(64, buf.Width);
            Assert.Equal(48, buf.Height);
            Assert.All(buf.Data, v => Assert.InRange(v, 0f, 1f));
        }
    }

    [Fact]
    public void SineGratingPeaksAtItsFrequency()
    {
        var spec = new TestPatternSpec(TestPatternKind.SineGrating, 256, 8, 0.125);
        var samples = LineSampler.Sample(TestPatternGenerator.Make(spec), LineSegment.Initial);
        var values = samples.Colors.Select(c => c.R).ToArray();
        var s = SpectrumAnalyzer.Compute(values, samples.Spacing, WindowFunction.Hann, removeMean: true);
        int peak = Assert.NotNull(s.PeakIndex);
        Helpers.Near(0.125, s.CyclesPerPixel[peak], 1.0 / s.FftSize + 1e-9);
    }

    [Fact]
    public void CheckerboardAndNoiseDeterminism()
    {
        var board = TestPatternGenerator.Make(new TestPatternSpec(TestPatternKind.Checkerboard, 8, 8, 2));
        Assert.Equal(1, board[0, 0].R);
        Assert.Equal(0, board[2, 0].R);
        Assert.Equal(1, board[2, 2].R);
        var a = TestPatternGenerator.Make(new TestPatternSpec(TestPatternKind.WhiteNoise, 16, 16, 7));
        var b = TestPatternGenerator.Make(new TestPatternSpec(TestPatternKind.WhiteNoise, 16, 16, 7));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SplitMixMatchesReferenceSequence()
    {
        // Reference values of SplitMix64 seeded with 0 (Vigna's reference implementation).
        var rng = new SplitMix64(0);
        Assert.Equal(0xE220A8397B1DCDAFUL, rng.Next());
        Assert.Equal(0x6E789E6AA1B965F4UL, rng.Next());
    }
}
