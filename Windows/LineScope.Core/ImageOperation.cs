namespace LineScope.Core;

/// <summary>One step that derives a new image from an existing one.</summary>
public abstract record ImageOperation
{
    public sealed record Filter(FilterKind Kind) : ImageOperation;
    public sealed record Resample(ResampleMethod Method, double Scale) : ImageOperation;

    public string DisplayName => this switch
    {
        Filter f => f.Kind.DisplayName,
        Resample r => $"{r.Method.ShortName()} ×{Fmt.Number(r.Scale)}",
        _ => "",
    };

    public PixelBuffer Apply(PixelBuffer buffer) => this switch
    {
        Filter f => Filters.Apply(f.Kind, buffer),
        Resample r => Resampler.Resample(buffer, r.Scale, r.Method),
        _ => buffer,
    };
}
