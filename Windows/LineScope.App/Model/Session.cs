using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using LineScope.Core;
using Microsoft.Win32;

namespace LineScope.App.Model;

public sealed record Profile(double[] X, float[] Values, LineSamples Samples);

/// <summary>What changed in a <see cref="Session"/>, so views redraw only what they show.</summary>
[Flags]
public enum SessionChange
{
    None = 0,
    Line = 1,
    Settings = 2,
    Variants = 4,
    Panes = 8,
    Focus = 16,
    Running = 32,
    All = Line | Settings | Variants | Panes | Focus | Running,
}

/// <summary>All state for one document window: the variants, the shared line, and view settings.</summary>
public sealed class Session
{
    public string FileName { get; }
    public SourceInfo SourceInfo { get; }

    private readonly List<ImageVariant> _variants;
    public IReadOnlyList<ImageVariant> Variants => _variants;

    /// <summary>Raised on the UI thread after any state change.</summary>
    public event Action<SessionChange>? Changed;
    public event Action<string>? Error;

    public Session(ImageDocument document, string fileName)
    {
        var original = new ImageVariant(document.Buffer, document.DisplayImage, null, null, []);
        FileName = fileName;
        SourceInfo = document.Info;
        _variants = [original];
        _focusedVariantId = original.Id;
        Panes = ZoneInfo.All.Select(_ => new List<PaneState> { new(original.Id) }).ToArray();
    }

    private void Raise(SessionChange change) => Changed?.Invoke(change);

    private LineSegment _line = LineSegment.Initial;
    public LineSegment Line
    {
        get => _line;
        set { if (_line != value) { _line = value; Raise(SessionChange.Line); } }
    }

    private ColorModel _colorModel = ColorModel.Rgba;
    public ColorModel ColorModel
    {
        get => _colorModel;
        set
        {
            if (_colorModel == value) return;
            _colorModel = value;
            if (_channelIndex >= value.Channels().Length) _channelIndex = 0;
            Raise(SessionChange.Settings);
        }
    }

    private int _channelIndex;
    public int ChannelIndex
    {
        get => _channelIndex;
        set { if (_channelIndex != value) { _channelIndex = value; Raise(SessionChange.Settings); } }
    }

    private WindowFunction _window = WindowFunction.Hann;
    public WindowFunction Window
    {
        get => _window;
        set { if (_window != value) { _window = value; Raise(SessionChange.Settings); } }
    }

    private bool _removeMean = true;
    public bool RemoveMean
    {
        get => _removeMean;
        set { if (_removeMean != value) { _removeMean = value; Raise(SessionChange.Settings); } }
    }

    private bool _logMagnitude = true;
    public bool LogMagnitude
    {
        get => _logMagnitude;
        set { if (_logMagnitude != value) { _logMagnitude = value; Raise(SessionChange.Settings); } }
    }

    private FrequencyAxis _frequencyAxis = FrequencyAxis.CyclesPerPixel;
    public FrequencyAxis FrequencyAxis
    {
        get => _frequencyAxis;
        set { if (_frequencyAxis != value) { _frequencyAxis = value; Raise(SessionChange.Settings); } }
    }

    private bool _useDevicePixels;
    public bool UseDevicePixels
    {
        get => _useDevicePixels;
        set { if (_useDevicePixels != value) { _useDevicePixels = value; Raise(SessionChange.Settings); } }
    }

    /// <summary>Panes per zone, indexed by <see cref="Zone"/>.</summary>
    public List<PaneState>[] Panes { get; }

    private Guid _focusedVariantId;
    /// <summary>The variant operations apply to and the inspector describes.</summary>
    public Guid FocusedVariantId
    {
        get => _focusedVariantId;
        set { if (_focusedVariantId != value) { _focusedVariantId = value; Raise(SessionChange.Focus); } }
    }

    public int RunningOperations { get; private set; }

    public ImageVariant Original => _variants[0];
    public ImageVariant FocusedVariant => Variant(FocusedVariantId) ?? Original;
    public ImageVariant? Variant(Guid id) => _variants.FirstOrDefault(v => v.Id == id);
    public string ChannelName => ColorModel.Channels()[ChannelIndex];

    // Panes

    public void Select(Guid variantId, Zone zone, int pane)
    {
        Panes[(int)zone][pane].VariantId = variantId;
        _focusedVariantId = variantId;
        Raise(SessionChange.Panes | SessionChange.Focus);
    }

    public void Split(Zone zone)
    {
        var shown = Panes[(int)zone].Select(p => p.VariantId).ToHashSet();
        var next = _variants.FirstOrDefault(v => !shown.Contains(v.Id)) ?? FocusedVariant;
        Panes[(int)zone].Add(new PaneState(next.Id));
        Raise(SessionChange.Panes);
    }

    public void Unsplit(Zone zone)
    {
        var panes = Panes[(int)zone];
        if (panes.Count <= 1) return;
        panes.RemoveAt(panes.Count - 1);
        Raise(SessionChange.Panes);
    }

    // Variants

    public void CloseVariant(Guid id)
    {
        int index = _variants.FindIndex(v => v.Id == id);
        if (index <= 0) return;
        _variants.RemoveAt(index);
        foreach (var zone in Panes)
            foreach (var pane in zone.Where(p => p.VariantId == id))
                pane.VariantId = Original.Id;
        if (_focusedVariantId == id) _focusedVariantId = Original.Id;
        Raise(SessionChange.Variants | SessionChange.Panes | SessionChange.Focus);
    }

    /// <summary>Applies an operation to the focused variant in the background and adds the result as a new tab.</summary>
    public async void Perform(ImageOperation operation)
    {
        var source = FocusedVariant;
        var input = source.Buffer;
        RunningOperations++;
        Raise(SessionChange.Running);
        try
        {
            var (output, bitmap) = await Task.Run(() =>
            {
                var result = operation.Apply(input);
                return (result, Bitmaps.FromBuffer(result));
            });
            var variant = new ImageVariant(output, bitmap, source.Id, operation, [.. source.Lineage, operation]);
            _variants.Add(variant);
            Show(variant.Id);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Could not compute {operation.DisplayName}: {ex.Message}");
        }
        finally
        {
            RunningOperations--;
            Raise(SessionChange.Running);
        }
    }

    /// <summary>
    /// Shows a new variant in every zone: in the last pane when split (so the left pane keeps the
    /// comparison baseline), otherwise in the only pane.
    /// </summary>
    private void Show(Guid id)
    {
        foreach (var zone in Panes) zone[^1].VariantId = id;
        _focusedVariantId = id;
        Raise(SessionChange.Variants | SessionChange.Panes | SessionChange.Focus);
    }

    // Analysis

    public Profile Profile(ImageVariant variant)
    {
        var samples = LineSampler.Sample(variant.Buffer, Line);
        var values = samples.Colors.Select(c => ColorConversion.Value(c, ColorModel, ChannelIndex)).ToArray();
        return new Profile(samples.Positions, values, samples);
    }

    public SpectrumResult Spectrum(Profile profile) =>
        SpectrumAnalyzer.Compute(profile.Values, Math.Max(profile.Samples.Spacing, 1e-9), Window, RemoveMean);

    /// <summary>Frequencies on the chosen axis and magnitudes (in dB when enabled).</summary>
    public (double[] Frequencies, double[] Magnitudes) SpectrumPoints(SpectrumResult s)
    {
        var freqs = FrequencyAxis == FrequencyAxis.CyclesPerPixel ? s.CyclesPerPixel : s.CyclesPerLine;
        var mags = s.Amplitudes.Select(a => LogMagnitude ? Math.Max(20 * Math.Log10(Math.Max(a, 1e-12)), DbFloor) : a).ToArray();
        return (freqs, mags);
    }

    public const double DbFloor = -100.0;

    // Export

    public void ExportFocused(Window? owner)
    {
        var variant = FocusedVariant;
        string baseName = Path.GetFileNameWithoutExtension(FileName);
        string suffix = variant.IsOriginal ? "original" : variant.Name.Replace(" → ", "_");
        foreach (char c in Path.GetInvalidFileNameChars()) suffix = suffix.Replace(c, '-');
        var dialog = new SaveFileDialog
        {
            Filter = "PNG image|*.png",
            DefaultExt = ".png",
            FileName = $"{baseName}-{suffix}.png",
            Title = "Export Image",
        };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(variant.Bitmap));
            using var stream = File.Create(dialog.FileName);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Could not write {Path.GetFileName(dialog.FileName)}: {ex.Message}");
        }
    }
}

/// <summary>
/// The session of the most recently activated document window, for the inspector window (which is itself
/// the active window while being used).
/// </summary>
public static class ActiveSession
{
    private static Session? _session;

    public static event Action? Changed;

    public static Session? Current
    {
        get => _session;
        set { if (_session != value) { _session = value; Changed?.Invoke(); } }
    }
}
