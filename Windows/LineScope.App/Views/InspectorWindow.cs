using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LineScope.App.Model;
using LineScope.Core;

namespace LineScope.App.Views;

/// <summary>Separate window describing the focused variant of the most recently active document window.</summary>
public sealed class InspectorWindow : Window
{
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(14, 4, 14, 14) };
    private Session? _observed;
    private bool _refreshQueued;

    public InspectorWindow()
    {
        Title = "Inspector";
        Width = 340;
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
        MinWidth = 300;
        MinHeight = 320;
        UseLayoutRounding = true;
        ShowInTaskbar = false;
        var work = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = work.Right - Width - 20;
        Top = work.Top + 20;
        Content = _scroll;
        Background = SystemColors.WindowBrush;

        ActiveSession.Changed += OnActiveChanged;
        Closed += (_, _) =>
        {
            ActiveSession.Changed -= OnActiveChanged;
            Observe(null);
        };
        OnActiveChanged();
    }

    private void OnActiveChanged()
    {
        Observe(ActiveSession.Current);
        Rebuild();
    }

    private void Observe(Session? session)
    {
        if (_observed is not null) _observed.Changed -= OnSessionChanged;
        _observed = session;
        if (_observed is not null) _observed.Changed += OnSessionChanged;
    }

    /// <summary>Coalesces bursts of changes (such as a line drag) into one rebuild.</summary>
    private void OnSessionChanged(SessionChange change)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Rebuild();
        });
    }

    private void Rebuild()
    {
        var session = _observed;
        if (session is null)
        {
            _scroll.Content = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 120, 0, 0),
                Children =
                {
                    new TextBlock { Text = "No Image", FontSize = 18, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Open an image to inspect it.", Foreground = Ui.SecondaryText, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) },
                },
            };
            return;
        }

        var variant = session.FocusedVariant;
        var profile = session.Profile(variant);
        var spectrum = session.Spectrum(profile);
        var info = session.SourceInfo;
        var original = session.Original.Buffer;
        var panel = new StackPanel();

        Section(panel, "File");
        Row(panel, "Name", session.FileName);
        Row(panel, "Type", info.FileType);
        Row(panel, "Stored size", $"{info.PixelWidth} × {info.PixelHeight} px");
        Row(panel, "Bit depth", $"{info.BitsPerComponent} bpc, {info.BitsPerPixel} bpp");
        Row(panel, "Color space", info.ColorSpaceName);
        Row(panel, "Alpha", info.HasAlpha ? "Yes" : "No");
        if (info.Dpi is double dpi) Row(panel, "Resolution", $"{Fmt.Fixed(dpi, 0)} dpi");

        Section(panel, "Focused Image");
        Row(panel, "Tab", variant.Name);
        Row(panel, "Size", $"{variant.Buffer.Width} × {variant.Buffer.Height} px");
        Row(panel, "Scale vs. original",
            $"×{Fmt.Fixed((double)variant.Buffer.Width / original.Width, 4)} · ×{Fmt.Fixed((double)variant.Buffer.Height / original.Height, 4)}");
        Row(panel, "Working format", "RGBA Float32, sRGB-encoded");

        Section(panel, "Operation Chain");
        panel.Children.Add(new TextBlock { Text = "Original", Foreground = Ui.SecondaryText, Margin = new Thickness(0, 2, 0, 2) });
        for (int i = 0; i < variant.Lineage.Count; i++)
            panel.Children.Add(new TextBlock { Text = $"{i + 1}. {variant.Lineage[i].DisplayName}", Margin = new Thickness(0, 2, 0, 2), TextWrapping = TextWrapping.Wrap });

        Section(panel, "Line");
        double w = variant.Buffer.Width, h = variant.Buffer.Height;
        var l = session.Line;
        Row(panel, "Start", $"({Fmt.Fixed(l.Start.X * w, 1)}, {Fmt.Fixed(l.Start.Y * h, 1)}) px");
        Row(panel, "End", $"({Fmt.Fixed(l.End.X * w, 1)}, {Fmt.Fixed(l.End.Y * h, 1)}) px");
        Row(panel, "Length", $"{Fmt.Fixed(profile.Samples.PixelLength, 1)} px");
        Row(panel, "Samples", $"{profile.Values.Length}, spacing {Fmt.Fixed(profile.Samples.Spacing, 3)} px");

        Section(panel, $"{session.ColorModel.DisplayName()} · {session.ChannelName}");
        var v = profile.Values;
        Row(panel, "Min", Fmt.Fixed(v.Length > 0 ? v.Min() : 0, 4));
        Row(panel, "Max", Fmt.Fixed(v.Length > 0 ? v.Max() : 0, 4));
        Row(panel, "Mean", Fmt.Fixed(v.Sum() / Math.Max(v.Length, 1), 4));
        if (spectrum.PeakIndex is int k)
        {
            Row(panel, "Peak frequency", $"{Fmt.Fixed(spectrum.CyclesPerPixel[k], 4)} c/px · {Fmt.Fixed(spectrum.CyclesPerLine[k], 2)} c/line");
            Row(panel, "Peak amplitude", Fmt.Fixed(spectrum.Amplitudes[k], 4));
        }

        double offset = _scroll.VerticalOffset;
        _scroll.Content = panel;
        _scroll.ScrollToVerticalOffset(offset);
    }

    private static void Section(Panel panel, string title) =>
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });

    private static void Row(Panel panel, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 12, 0) });
        // A read-only, borderless TextBox so values can be selected and copied.
        var text = new TextBox
        {
            Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = null, Padding = new Thickness(0),
            TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        panel.Children.Add(grid);
    }
}
