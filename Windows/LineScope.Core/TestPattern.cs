namespace LineScope.Core;

/// <summary>Synthetic images commonly used to evaluate resampling and filtering.</summary>
public enum TestPatternKind
{
    ZonePlate,
    LinearChirp,
    SineGrating,
    SquareGrating,
    Checkerboard,
    StepEdge,
    SlantedEdge,
    Impulses,
    SiemensStar,
    WhiteNoise,
    HueRamp,
}

/// <summary>The single tunable parameter of a pattern.</summary>
public sealed record PatternParameter(string Name, double DefaultValue, double Min, double Max)
{
    public double Clamp(double v) => Math.Clamp(v, Min, Max);
}

public static class TestPatternKindInfo
{
    public static readonly TestPatternKind[] All = Enum.GetValues<TestPatternKind>();

    public static string DisplayName(this TestPatternKind k) => k switch
    {
        TestPatternKind.ZonePlate => "Zone Plate",
        TestPatternKind.LinearChirp => "Linear Chirp",
        TestPatternKind.SineGrating => "Sine Grating",
        TestPatternKind.SquareGrating => "Square-Wave Bars",
        TestPatternKind.Checkerboard => "Checkerboard",
        TestPatternKind.StepEdge => "Step Edge",
        TestPatternKind.SlantedEdge => "Slanted Edge",
        TestPatternKind.Impulses => "Impulse Grid",
        TestPatternKind.SiemensStar => "Siemens Star",
        TestPatternKind.WhiteNoise => "White Noise",
        _ => "Hue / Lightness Ramp",
    };

    /// <summary>What the pattern is good for, shown in the generator window.</summary>
    public static string Purpose(this TestPatternKind k) => k switch
    {
        TestPatternKind.ZonePlate => "Circular chirp: frequency grows with radius up to the chosen maximum. Aliasing appears as false ring centers.",
        TestPatternKind.LinearChirp => "Horizontal sweep from 0 to the chosen frequency. Put the line across it to read a filter's frequency response.",
        TestPatternKind.SineGrating => "Single pure frequency along x. The spectrum should show one peak.",
        TestPatternKind.SquareGrating => "Bars with odd harmonics. Shows how filters treat harmonics near Nyquist.",
        TestPatternKind.Checkerboard => "Two-dimensional square wave. Small cells alias badly under nearest-neighbor.",
        TestPatternKind.StepEdge => "One hard vertical edge. Reveals ringing (sinc, Lanczos) versus blur (tent, Mitchell).",
        TestPatternKind.SlantedEdge => "Edge tilted a few degrees, the standard target for measuring sharpness (MTF).",
        TestPatternKind.Impulses => "Isolated single pixels. Each dot shows the filter's point-spread function.",
        TestPatternKind.SiemensStar => "Radial spokes: spatial frequency rises toward the center, in every orientation.",
        TestPatternKind.WhiteNoise => "Uniform random values with a flat spectrum. The profile's spectrum shows the filter's transfer curve.",
        _ => "Hue across x, lightness down y. Use it with the HSL and CMYK channels.",
    };

    /// <summary>The single tunable parameter, or null if the pattern has none.</summary>
    public static PatternParameter? Parameter(this TestPatternKind k) => k switch
    {
        TestPatternKind.ZonePlate or TestPatternKind.LinearChirp => new("Max frequency (cycles/px)", 0.5, 0.01, 1),
        TestPatternKind.SineGrating => new("Frequency (cycles/px)", 0.1, 0.001, 1),
        TestPatternKind.SquareGrating => new("Period (px)", 8, 2, 512),
        TestPatternKind.Checkerboard => new("Cell size (px)", 8, 1, 512),
        TestPatternKind.SlantedEdge => new("Angle (degrees)", 5, -45, 45),
        TestPatternKind.Impulses => new("Spacing (px)", 32, 2, 1024),
        TestPatternKind.SiemensStar => new("Spokes", 36, 4, 360),
        TestPatternKind.WhiteNoise => new("Seed", 1, 0, 1_000_000),
        _ => null,
    };
}

public readonly record struct TestPatternSpec(TestPatternKind Kind, int Width, int Height, double Parameter)
{
    public TestPatternSpec(TestPatternKind kind, int width = 512, int height = 512, double? parameter = null)
        : this(kind, width, height, parameter ?? kind.Parameter()?.DefaultValue ?? 0) { }

    public string Title
    {
        get
        {
            var t = $"{Kind.DisplayName()} {Width}×{Height}";
            var p = Kind.Parameter();
            if (p is not null && (Kind != TestPatternKind.WhiteNoise || Parameter != p.DefaultValue))
                t += $" ({Fmt.Number(Parameter)})";
            return t;
        }
    }

    /// <summary>The spec with its parameter clamped to the kind's range.</summary>
    public TestPatternSpec Clamped()
    {
        var p = Kind.Parameter();
        return p is null ? this : this with { Parameter = p.Clamp(Parameter) };
    }
}

public static class TestPatternGenerator
{
    public static PixelBuffer Make(TestPatternSpec spec)
    {
        int w = Math.Max(1, spec.Width);
        int h = Math.Max(1, spec.Height);
        double p = spec.Parameter;
        var buf = new PixelBuffer(w, h);
        double cx = w / 2.0;
        double cy = h / 2.0;

        void Gray(Func<double, double, double> f)
        {
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    // Evaluate at pixel centers.
                    float v = (float)Math.Clamp(f(x + 0.5, y + 0.5), 0, 1);
                    buf[x, y] = new Rgba(v, v, v);
                }
            });
        }

        switch (spec.Kind)
        {
            case TestPatternKind.ZonePlate:
            {
                // Local frequency k·r reaches p at r = max(w,h)/2 (the left/right edges of the midline).
                double k = p / (Math.Max(w, h) / 2.0);
                Gray((x, y) =>
                {
                    double r2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    return 0.5 + 0.5 * Math.Cos(Math.PI * k * r2);
                });
                break;
            }
            case TestPatternKind.LinearChirp:
                // Phase 2π·p·x²/(2W): instantaneous frequency p·x/W.
                Gray((x, _) => 0.5 + 0.5 * Math.Cos(Math.PI * p * x * x / w));
                break;
            case TestPatternKind.SineGrating:
                Gray((x, _) => 0.5 + 0.5 * Math.Sin(2 * Math.PI * p * x));
                break;
            case TestPatternKind.SquareGrating:
            {
                double period = Math.Max(p, 1);
                Gray((x, _) => (x - 0.5) % period < period / 2 ? 1 : 0);
                break;
            }
            case TestPatternKind.Checkerboard:
            {
                int cell = Math.Max(RoundInt(p), 1);
                Gray((x, y) => (((int)x / cell) + ((int)y / cell)) % 2 == 0 ? 1 : 0);
                break;
            }
            case TestPatternKind.StepEdge:
                Gray((x, _) => x < cx ? 0.1 : 0.9);
                break;
            case TestPatternKind.SlantedEdge:
            {
                // Dark/light halves split by a line through the center tilted from vertical.
                double a = p * Math.PI / 180;
                Gray((x, y) => ((x - cx) * Math.Cos(a) + (y - cy) * Math.Sin(a)) < 0 ? 0.1 : 0.9);
                break;
            }
            case TestPatternKind.Impulses:
            {
                int s = Math.Max(RoundInt(p), 1);
                int off = s / 2;
                Gray((x, y) => ((int)x % s == off && (int)y % s == off) ? 1 : 0);
                break;
            }
            case TestPatternKind.SiemensStar:
            {
                double spokes = Math.Max(Math.Round(p, MidpointRounding.AwayFromZero), 2);
                double radius = Math.Min(w, h) / 2.0;
                Gray((x, y) =>
                {
                    double dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > radius * radius) return 0.5;
                    double theta = Math.Atan2(dy, dx);
                    return Math.Sin(spokes * theta) >= 0 ? 1 : 0;
                });
                break;
            }
            case TestPatternKind.WhiteNoise:
            {
                var rng = new SplitMix64((ulong)Math.Max(p, 0));
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float v = (float)((rng.Next() >> 11) / (double)(1UL << 53));
                        buf[x, y] = new Rgba(v, v, v);
                    }
                break;
            }
            case TestPatternKind.HueRamp:
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float hue = (float)((x + 0.5) / w * 360);
                        float light = (float)(1 - (y + 0.5) / h);
                        var c = ColorConversion.FromHsl(Math.Min(hue, 359.999f), 1, light);
                        buf[x, y] = new Rgba(Math.Clamp(c.R, 0, 1), Math.Clamp(c.G, 0, 1), Math.Clamp(c.B, 0, 1));
                    }
                break;
        }
        return buf;
    }

    private static int RoundInt(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);
}

/// <summary>Small deterministic generator so noise patterns are reproducible from their seed.</summary>
internal struct SplitMix64(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E37_79B9_7F4A_7C15;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EB;
            return z ^ (z >> 31);
        }
    }
}
