namespace LineScope.Core;

public enum ColorModel { Rgba, Hsl, Cmyk }

public static class ColorModelInfo
{
    public static readonly ColorModel[] All = [ColorModel.Rgba, ColorModel.Hsl, ColorModel.Cmyk];

    public static string DisplayName(this ColorModel m) => m switch
    {
        ColorModel.Rgba => "RGBA",
        ColorModel.Hsl => "HSL",
        _ => "CMYK",
    };

    public static string[] Channels(this ColorModel m) => m switch
    {
        ColorModel.Rgba => ["Red", "Green", "Blue", "Alpha"],
        ColorModel.Hsl => ["Hue", "Saturation", "Lightness"],
        _ => ["Cyan", "Magenta", "Yellow", "Black"],
    };

    /// <summary>Plot range for a channel; hue is in degrees, everything else in 0…1.</summary>
    public static (double Min, double Max) RangeOfChannel(this ColorModel m, int channel) =>
        m == ColorModel.Hsl && channel == 0 ? (0, 360) : (0, 1);
}

public static class ColorConversion
{
    // HSL (hue in degrees)

    public static (float H, float S, float L) Hsl(Rgba c)
    {
        float maxV = Math.Max(c.R, Math.Max(c.G, c.B));
        float minV = Math.Min(c.R, Math.Min(c.G, c.B));
        float l = (maxV + minV) / 2;
        float d = maxV - minV;
        if (!(d > 1e-7f)) return (0, 0, l);
        float s = d / (1 - MathF.Abs(2 * l - 1));
        float h;
        if (maxV == c.R) h = ((c.G - c.B) / d) % 6;
        else if (maxV == c.G) h = (c.B - c.R) / d + 2;
        else h = (c.R - c.G) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, Math.Clamp(s, 0, 1), l);
    }

    public static Rgba FromHsl(float h, float s, float l, float alpha = 1)
    {
        float c = (1 - MathF.Abs(2 * l - 1)) * s;
        float hp = h / 60;
        float x = c * (1 - MathF.Abs(hp % 2 - 1));
        (float r, float g, float b) = hp switch
        {
            < 1 => (c, x, 0f),
            < 2 => (x, c, 0f),
            < 3 => (0f, c, x),
            < 4 => (0f, x, c),
            < 5 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = l - c / 2;
        return new Rgba(r + m, g + m, b + m, alpha);
    }

    // CMYK (naive device-independent conversion, K = 1 - max(R,G,B))

    public static (float C, float M, float Y, float K) Cmyk(Rgba c)
    {
        float k = 1 - Math.Max(c.R, Math.Max(c.G, c.B));
        if (!(k < 1 - 1e-7f)) return (0, 0, 0, 1);
        float d = 1 - k;
        return ((1 - c.R - k) / d, (1 - c.G - k) / d, (1 - c.B - k) / d, k);
    }

    public static Rgba FromCmyk(float c, float m, float y, float k, float alpha = 1) =>
        new((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - y) * (1 - k), alpha);

    // Channel extraction

    public static float Value(Rgba color, ColorModel model, int channel)
    {
        switch (model)
        {
            case ColorModel.Rgba:
                return channel switch { 0 => color.R, 1 => color.G, 2 => color.B, _ => color.A };
            case ColorModel.Hsl:
                var v = Hsl(color);
                return channel switch { 0 => v.H, 1 => v.S, _ => v.L };
            default:
                var k = Cmyk(color);
                return channel switch { 0 => k.C, 1 => k.M, 2 => k.Y, _ => k.K };
        }
    }
}
