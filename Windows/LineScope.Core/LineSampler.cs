namespace LineScope.Core;

/// <summary>A point relative to the image extent: (0,0) is the top-left corner, (1,1) the bottom-right corner.</summary>
public readonly record struct NormalizedPoint(double X, double Y)
{
    public NormalizedPoint Clamped() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, 0, 1));
}

/// <summary>The selection line, in normalized coordinates so it lands on the same content in every resampled copy.</summary>
public readonly record struct LineSegment(NormalizedPoint Start, NormalizedPoint End)
{
    /// <summary>Full-width horizontal line at mid-height.</summary>
    public static readonly LineSegment Initial = new(new NormalizedPoint(0, 0.5), new NormalizedPoint(1, 0.5));

    public double PixelLength(int width, int height)
    {
        double dx = (End.X - Start.X) * width;
        double dy = (End.Y - Start.Y) * height;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <param name="Positions">Position along the line, 0 at Start and 1 at End.</param>
/// <param name="Colors">Sampled colors.</param>
/// <param name="PixelLength">Line length in the sampled image's pixels.</param>
/// <param name="Spacing">Distance between consecutive samples, in pixels (≈ 1).</param>
public sealed record LineSamples(double[] Positions, Rgba[] Colors, double PixelLength, double Spacing);

public static class LineSampler
{
    /// <summary>
    /// Samples the line in the buffer's own pixel grid: <c>ceil(length) + 1</c> bilinear samples,
    /// so a half-size copy yields about half as many samples.
    /// </summary>
    public static LineSamples Sample(PixelBuffer buffer, LineSegment line)
    {
        double w = buffer.Width, h = buffer.Height;
        double length = line.PixelLength(buffer.Width, buffer.Height);
        int count = Math.Max(2, (int)Math.Ceiling(length) + 1);
        double x0 = line.Start.X * w, y0 = line.Start.Y * h;
        double x1 = line.End.X * w, y1 = line.End.Y * h;

        var positions = new double[count];
        var colors = new Rgba[count];
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / (count - 1);
            positions[i] = t;
            colors[i] = Bilinear(buffer, x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
        }
        return new LineSamples(positions, colors, length, length / (count - 1));
    }

    /// <summary>Bilinear interpolation at a continuous position (pixel centers at +0.5), edges clamped.</summary>
    public static Rgba Bilinear(PixelBuffer buffer, double x, double y)
    {
        double fx = x - 0.5;
        double fy = y - 0.5;
        int ix = (int)Math.Floor(fx);
        int iy = (int)Math.Floor(fy);
        float tx = (float)(fx - ix);
        float ty = (float)(fy - iy);
        int x0 = Math.Clamp(ix, 0, buffer.Width - 1);
        int x1 = Math.Clamp(ix + 1, 0, buffer.Width - 1);
        int y0 = Math.Clamp(iy, 0, buffer.Height - 1);
        int y1 = Math.Clamp(iy + 1, 0, buffer.Height - 1);

        var d = buffer.Data;
        int w = buffer.Width;
        float Mix(int c)
        {
            float a = d[(y0 * w + x0) * 4 + c];
            float b = d[(y0 * w + x1) * 4 + c];
            float e = d[(y1 * w + x0) * 4 + c];
            float f = d[(y1 * w + x1) * 4 + c];
            float top = a + (b - a) * tx;
            float bottom = e + (f - e) * tx;
            return top + (bottom - top) * ty;
        }
        return new Rgba(Mix(0), Mix(1), Mix(2), Mix(3));
    }
}
