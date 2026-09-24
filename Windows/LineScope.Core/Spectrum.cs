using System.Numerics;

namespace LineScope.Core;

public enum WindowFunction { None, Hann }

public static class WindowFunctionInfo
{
    public static readonly WindowFunction[] All = [WindowFunction.None, WindowFunction.Hann];

    public static string DisplayName(this WindowFunction w) => w == WindowFunction.None ? "Rectangular" : "Hann";
}

/// <param name="CyclesPerPixel">Bin frequencies in cycles per pixel of the sampled image (Nyquist ≈ 0.5).</param>
/// <param name="CyclesPerLine">Bin frequencies in cycles over the whole line; comparable across resampled copies.</param>
/// <param name="Amplitudes">Single-sided amplitude: a sine of amplitude A peaks at ≈ A.</param>
public sealed record SpectrumResult(
    double[] CyclesPerPixel, double[] CyclesPerLine, double[] Amplitudes, int FftSize, int SampleCount)
{
    public static readonly SpectrumResult Empty = new([], [], [], 0, 0);

    /// <summary>Index of the largest non-DC bin.</summary>
    public int? PeakIndex
    {
        get
        {
            if (Amplitudes.Length <= 1) return null;
            int best = 1;
            for (int i = 2; i < Amplitudes.Length; i++)
                if (Amplitudes[best] < Amplitudes[i]) best = i;
            return best;
        }
    }
}

public static class SpectrumAnalyzer
{
    /// <summary>Real FFT of a profile, zero-padded to the next power of two.</summary>
    /// <param name="spacing">Distance between samples, in pixels.</param>
    public static SpectrumResult Compute(float[] values, double spacing, WindowFunction window, bool removeMean)
    {
        int n = values.Length;
        if (n < 2) return SpectrumResult.Empty;
        int log2n = (int)Math.Ceiling(Math.Log2(n));
        int fftSize = 1 << log2n;
        int half = fftSize / 2;

        var signal = (float[])values.Clone();
        if (removeMean)
        {
            float mean = signal.Sum() / n;
            for (int i = 0; i < n; i++) signal[i] -= mean;
        }
        float windowSum = n;
        if (window == WindowFunction.Hann)
        {
            // Symmetric Hann over the n real samples.
            windowSum = 0;
            for (int i = 0; i < n; i++)
            {
                float w = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (n - 1));
                signal[i] *= w;
                windowSum += w;
            }
        }

        var buffer = new Complex[fftSize];
        for (int i = 0; i < n; i++) buffer[i] = signal[i];
        Fft.Forward(buffer);

        var amplitudes = new double[half + 1];
        double norm = Math.Max(windowSum, 1e-12);
        amplitudes[0] = buffer[0].Magnitude / norm;
        amplitudes[half] = buffer[half].Magnitude / norm;
        for (int k = 1; k < Math.Max(half, 1); k++)
            amplitudes[k] = 2 * buffer[k].Magnitude / norm;

        double lineLength = spacing * (n - 1);
        var cpp = new double[half + 1];
        var cpl = new double[half + 1];
        for (int k = 0; k <= half; k++)
        {
            cpp[k] = k / (fftSize * spacing);
            cpl[k] = cpp[k] * lineLength;
        }
        return new SpectrumResult(cpp, cpl, amplitudes, fftSize, n);
    }
}

/// <summary>In-place iterative radix-2 Cooley-Tukey FFT (replaces vDSP on macOS).</summary>
internal static class Fft
{
    /// <summary>Forward DFT, X[k] = Σ x[j]·e^(−2πi·jk/N), unscaled. The length must be a power of two.</summary>
    public static void Forward(Complex[] a)
    {
        int n = a.Length;
        if (n <= 1) return;
        if ((n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.");

        // Bit-reversal permutation.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            var wLen = new Complex(Math.Cos(angle), Math.Sin(angle));
            int halfLen = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                Complex w = Complex.One;
                for (int k = 0; k < halfLen; k++)
                {
                    Complex u = a[i + k];
                    Complex v = a[i + k + halfLen] * w;
                    a[i + k] = u + v;
                    a[i + k + halfLen] = u - v;
                    w *= wLen;
                }
            }
        }
    }
}
