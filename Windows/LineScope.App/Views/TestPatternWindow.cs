using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LineScope.App.Model;
using LineScope.Core;

namespace LineScope.App.Views;

/// <summary>Window for choosing a pattern, its size and parameter before creating it.</summary>
public sealed class TestPatternWindow : Window
{
    private static readonly int[] Sizes = [128, 256, 512, 1024, 2048];
    private const int MaxSide = 8192;
    private const int MaxPreviewPixels = 2048 * 2048;

    private TestPatternSpec _spec = new(TestPatternKind.ZonePlate);
    private bool _sizeValid = true;

    private readonly ComboBox _kind;
    private readonly TextBlock _purpose;
    private readonly TextBox _width, _height;
    private readonly Grid _parameterRow = new();
    private readonly Button _create;
    private readonly Image _preview;
    private readonly TextBlock _previewCaption;
    private readonly DispatcherTimer _debounce;
    private int _previewGeneration;

    public TestPatternWindow()
    {
        Title = "New Test Pattern";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _kind = new ComboBox { ItemsSource = TestPatternKindInfo.All.Select(k => k.DisplayName()).ToList(), SelectedIndex = 0 };
        _kind.SelectionChanged += (_, _) =>
        {
            // Resets the parameter to the new pattern's default when the kind changes.
            _spec = new TestPatternSpec(TestPatternKindInfo.All[_kind.SelectedIndex], _spec.Width, _spec.Height);
            _purpose!.Text = _spec.Kind.Purpose();
            BuildParameterRow();
            SpecChanged();
        };
        _purpose = new TextBlock
        {
            Text = _spec.Kind.Purpose(), TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Ui.SecondaryText,
            Margin = new Thickness(0, 6, 0, 10),
        };

        _width = SizeBox(_spec.Width);
        _height = SizeBox(_spec.Height);
        var presets = Ui.PresetsButton(Sizes.Select(s => ($"{s} × {s}", (Action)(() =>
        {
            _width.Text = s.ToString();
            _height.Text = s.ToString();
        }))));
        var sizeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _width, new TextBlock { Text = "×", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }, _height, presets },
        };

        _create = new Button { Content = "Create", IsDefault = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        _create.Click += (_, _) => App.Current.OpenPattern(_spec.Clamped());

        var form = new Grid { Width = 380 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddRow(form, "Pattern", _kind);
        AddSpanning(form, _purpose);
        AddRow(form, "Size", sizeRow);
        AddSpanning(form, _parameterRow);
        AddSpanning(form, _create);
        BuildParameterRow();

        _preview = new Image { Width = 256, Height = 256, Stretch = Stretch.None };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _previewCaption = new TextBlock { FontSize = 11, Foreground = Ui.SecondaryText, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var previewPanel = new StackPanel
        {
            Margin = new Thickness(20, 0, 0, 0),
            Children =
            {
                new Border { Width = 256, Height = 256, Background = SystemColors.AppWorkspaceBrush, CornerRadius = new CornerRadius(4), Child = _preview, ClipToBounds = true },
                _previewCaption,
            },
        };

        Content = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16), Children = { form, previewPanel } };

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); UpdatePreview(); };
        SpecChanged();
    }

    private TextBox SizeBox(int value)
    {
        var box = new TextBox { Text = value.ToString(), Width = 64, VerticalContentAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => ReadSize();
        return box;
    }

    private void ReadSize()
    {
        if (_width is null || _height is null) return;
        bool okW = int.TryParse(_width.Text, out int w) && w is >= 1 and <= MaxSide;
        bool okH = int.TryParse(_height.Text, out int h) && h is >= 1 and <= MaxSide;
        _width.BorderBrush = okW ? SystemColors.ControlDarkBrush : Brushes.Red;
        _height.BorderBrush = okH ? SystemColors.ControlDarkBrush : Brushes.Red;
        _sizeValid = okW && okH;
        if (_sizeValid) _spec = _spec with { Width = w, Height = h };
        SpecChanged();
    }

    private void BuildParameterRow()
    {
        _parameterRow.Children.Clear();
        _parameterRow.ColumnDefinitions.Clear();
        var p = _spec.Kind.Parameter();
        if (p is null) return;
        _parameterRow.Margin = new Thickness(0, 10, 0, 0);
        _parameterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _parameterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _parameterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _parameterRow.Children.Add(label);
        var box = new TextBox { Text = Fmt.Number(_spec.Parameter, 4), Width = 70, Margin = new Thickness(8, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetColumn(box, 2);
        _parameterRow.Children.Add(box);
        bool syncing = false;
        Slider? slider = null;
        if (_spec.Kind != TestPatternKind.WhiteNoise)
        {
            slider = new Slider { Minimum = p.Min, Maximum = p.Max, Value = _spec.Parameter, VerticalAlignment = VerticalAlignment.Center };
            slider.ValueChanged += (_, e) =>
            {
                if (syncing) return;
                syncing = true;
                _spec = _spec with { Parameter = e.NewValue };
                box.Text = Fmt.Number(e.NewValue, 4);
                syncing = false;
                SpecChanged();
            };
            Grid.SetColumn(slider, 1);
            _parameterRow.Children.Add(slider);
        }
        box.TextChanged += (_, _) =>
        {
            if (syncing) return;
            bool ok = Ui.TryParse(box.Text, out double v);
            box.BorderBrush = ok ? SystemColors.ControlDarkBrush : Brushes.Red;
            if (!ok) return;
            syncing = true;
            _spec = _spec with { Parameter = v };
            if (slider is not null) slider.Value = p.Clamp(v);
            syncing = false;
            SpecChanged();
        };
    }

    private void SpecChanged()
    {
        if (_create is null) return;
        _create.IsEnabled = _sizeValid;
        _previewGeneration++;
        _debounce.Stop();
        bool previewable = _sizeValid && (long)_spec.Width * _spec.Height <= MaxPreviewPixels;
        _previewCaption.Text = previewable ? "Preview: center 256 × 256 px at 1:1" : "Too large to preview";
        if (!previewable)
        {
            _preview.Source = null;
            return;
        }
        _debounce.Start();
    }

    /// <summary>
    /// Generates the real pattern and shows its central pixels at 1:1, so aliasing looks as it will in the
    /// analysis window.
    /// </summary>
    private async void UpdatePreview()
    {
        int generation = _previewGeneration;
        var s = _spec.Clamped();
        var bitmap = await Task.Run(() =>
        {
            var full = TestPatternGenerator.Make(s);
            int w = Math.Min(s.Width, 256), h = Math.Min(s.Height, 256);
            var cropped = new CroppedBitmap(Bitmaps.FromBuffer(full), new Int32Rect((s.Width - w) / 2, (s.Height - h) / 2, w, h));
            cropped.Freeze();
            return (BitmapSource)cropped;
        });
        if (generation == _previewGeneration) _preview.Source = bitmap;
    }

    private static void AddRow(Grid grid, string label, UIElement content)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    private static void AddSpanning(Grid grid, UIElement content)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(content, row);
        Grid.SetColumnSpan(content, 2);
        grid.Children.Add(content);
    }
}
