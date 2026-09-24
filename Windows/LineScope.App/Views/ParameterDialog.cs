using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LineScope.App.Model;
using LineScope.Core;

namespace LineScope.App.Views;

/// <summary>Collects parameters for a filter or resample; the caller runs <see cref="Operation"/> on the focused variant.</summary>
public sealed class ParameterDialog : Window
{
    private const int MaxOutputSide = 16_384;

    private readonly OperationRequest _request;
    private readonly ImageVariant _source;
    private readonly Button _apply;
    private TextBlock? _result;

    private double _radius = 2.0;
    private double _intensity = 0.5;
    private double _noiseLevel = 0.02;
    private double _sharpness = 0.4;
    private double _scale = 0.5;

    public ImageOperation? Operation { get; private set; }

    public ParameterDialog(OperationRequest request, Session session)
    {
        _request = request;
        _source = session.FocusedVariant;
        Title = request.Title;
        SizeToContent = SizeToContent.Height;
        Width = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        UseLayoutRounding = true;

        var fields = new Grid { Margin = new Thickness(0, 10, 0, 16) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        BuildFields(fields);

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        _apply = new Button { Content = "Apply", IsDefault = true, MinWidth = 80 };
        _apply.Click += (_, _) =>
        {
            Operation = BuildOperation();
            DialogResult = true;
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = request.Title, FontWeight = FontWeights.SemiBold, FontSize = 14 },
                new TextBlock
                {
                    Text = $"Applies to: {_source.Name} ({_source.Buffer.Width}×{_source.Buffer.Height})",
                    FontSize = 11, Foreground = Ui.SecondaryText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                },
                fields,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _apply } },
            },
        };
        Validate();
    }

    private void BuildFields(Grid grid)
    {
        switch (_request)
        {
            case OperationRequest.GaussianBlur or OperationRequest.BoxBlur:
                Number(grid, "Radius (px)", _radius, 0, 100, v => _radius = v);
                break;
            case OperationRequest.UnsharpMask:
                Number(grid, "Radius (px)", _radius, 0, 100, v => _radius = v);
                Number(grid, "Intensity", _intensity, 0, 10, v => _intensity = v);
                break;
            case OperationRequest.NoiseReduction:
                Number(grid, "Noise level", _noiseLevel, 0, 0.1, v => _noiseLevel = v);
                Number(grid, "Sharpness", _sharpness, 0, 2, v => _sharpness = v);
                break;
            case OperationRequest.Resample:
                var box = new TextBox { Text = Fmt.Number(_scale, 4), Width = 80, VerticalContentAlignment = VerticalAlignment.Center };
                box.TextChanged += (_, _) =>
                {
                    bool ok = Ui.TryParse(box.Text, out double v) && v > 0;
                    if (ok) _scale = v; else _scale = double.NaN;
                    box.BorderBrush = ok ? SystemColors.ControlDarkBrush : Brushes.Red;
                    Validate();
                };
                var presets = Ui.PresetsButton(new[] { 0.25, 0.5, 0.75, 1.5, 2, 4 }
                    .Select(s => ($"×{Fmt.Number(s)}", (Action)(() => box.Text = Fmt.Number(s, 4)))));
                AddRow(grid, "Scale factor", new StackPanel { Orientation = Orientation.Horizontal, Children = { box, presets } });
                _result = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI") };
                Typography.SetNumeralAlignment(_result, FontNumeralAlignment.Tabular);
                AddRow(grid, "Result", _result);
                break;
        }
    }

    private void Number(Grid grid, string label, double initial, double min, double max, Action<double> set)
    {
        var slider = new Slider { Minimum = min, Maximum = max, Value = initial, VerticalAlignment = VerticalAlignment.Center };
        var box = new TextBox { Text = Fmt.Number(initial), Width = 64, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        bool syncing = false;
        slider.ValueChanged += (_, e) =>
        {
            if (syncing) return;
            syncing = true;
            set(e.NewValue);
            box.Text = Fmt.Number(e.NewValue);
            syncing = false;
        };
        box.TextChanged += (_, _) =>
        {
            if (syncing) return;
            bool ok = Ui.TryParse(box.Text, out double v) && v >= 0;
            box.BorderBrush = ok ? SystemColors.ControlDarkBrush : Brushes.Red;
            if (!ok) return;
            syncing = true;
            set(v);
            slider.Value = Math.Clamp(v, min, max);
            syncing = false;
        };
        var cell = new DockPanel();
        DockPanel.SetDock(box, Dock.Right);
        cell.Children.Add(box);
        cell.Children.Add(slider);
        AddRow(grid, label, cell);
    }

    private static void AddRow(Grid grid, string label, UIElement content)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 14, 5) };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        if (content is FrameworkElement fe) fe.Margin = new Thickness(0, 5, 0, 5);
        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    private void Validate()
    {
        if (_request is not OperationRequest.Resample)
        {
            _apply.IsEnabled = true;
            return;
        }
        // Check in floating point first: casting a huge size to int would overflow.
        double largest = Math.Max(_source.Buffer.Width, _source.Buffer.Height) * _scale;
        bool valid = _scale > 0 && largest < 1e9;
        if (valid)
        {
            int w = Resampler.OutputSize(_source.Buffer.Width, _scale);
            int h = Resampler.OutputSize(_source.Buffer.Height, _scale);
            valid = Math.Max(w, h) <= MaxOutputSide;
            if (_result is not null) _result.Text = $"{w} × {h} px";
        }
        else if (_result is not null)
        {
            _result.Text = "—";
        }
        _apply.IsEnabled = valid;
    }

    private ImageOperation BuildOperation() => _request switch
    {
        OperationRequest.GaussianBlur => new ImageOperation.Filter(new FilterKind.GaussianBlur(_radius)),
        OperationRequest.BoxBlur => new ImageOperation.Filter(new FilterKind.BoxBlur(_radius)),
        OperationRequest.UnsharpMask => new ImageOperation.Filter(new FilterKind.UnsharpMask(_radius, _intensity)),
        OperationRequest.NoiseReduction => new ImageOperation.Filter(new FilterKind.NoiseReduction(_noiseLevel, _sharpness)),
        OperationRequest.Resample r => new ImageOperation.Resample(r.Method, _scale),
        _ => throw new InvalidOperationException(),
    };
}
