using System.Windows.Media.Imaging;
using LineScope.Core;

namespace LineScope.App.Model;

/// <summary>The original image or one derived copy. Each copy records the chain of operations back to the original.</summary>
public sealed class ImageVariant(PixelBuffer buffer, BitmapSource bitmap, Guid? parentId, ImageOperation? operation,
    IReadOnlyList<ImageOperation> lineage)
{
    public Guid Id { get; } = Guid.NewGuid();
    public PixelBuffer Buffer { get; } = buffer;
    public BitmapSource Bitmap { get; } = bitmap;
    public Guid? ParentId { get; } = parentId;
    public ImageOperation? Operation { get; } = operation;
    /// <summary>Operations applied to the original, in order. Empty for the original.</summary>
    public IReadOnlyList<ImageOperation> Lineage { get; } = lineage;

    public bool IsOriginal => Operation is null;

    public string Name => Lineage.Count == 0 ? "Original" : string.Join(" → ", Lineage.Select(o => o.DisplayName));
}

/// <summary>Which kind of content a zone shows.</summary>
public enum Zone { Images, Profile, Spectrum }

public static class ZoneInfo
{
    public static readonly Zone[] All = [Zone.Images, Zone.Profile, Zone.Spectrum];

    public static string Title(this Zone z) => z switch
    {
        Zone.Images => "Images",
        Zone.Profile => "Line Profile",
        _ => "Spectrum",
    };
}

/// <summary>One side-by-side pane inside a zone; it shows one variant at a time.</summary>
public sealed class PaneState(Guid variantId)
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid VariantId { get; set; } = variantId;
}

public enum FrequencyAxis { CyclesPerPixel, CyclesPerLine }

public static class FrequencyAxisInfo
{
    public static readonly FrequencyAxis[] All = [FrequencyAxis.CyclesPerPixel, FrequencyAxis.CyclesPerLine];

    public static string DisplayName(this FrequencyAxis a) => a == FrequencyAxis.CyclesPerPixel ? "cycles/pixel" : "cycles/line";
}

/// <summary>Operations that need parameters before running; shown as a dialog.</summary>
public abstract record OperationRequest
{
    public sealed record GaussianBlur : OperationRequest;
    public sealed record BoxBlur : OperationRequest;
    public sealed record UnsharpMask : OperationRequest;
    public sealed record NoiseReduction : OperationRequest;
    public sealed record Resample(ResampleMethod Method) : OperationRequest;

    public string Title => this switch
    {
        GaussianBlur => "Gaussian Blur",
        BoxBlur => "Box Blur",
        UnsharpMask => "Sharpen (Unsharp Mask)",
        NoiseReduction => "Noise Reduction",
        Resample r => $"Resample: {r.Method.DisplayName()}",
        _ => "",
    };
}
