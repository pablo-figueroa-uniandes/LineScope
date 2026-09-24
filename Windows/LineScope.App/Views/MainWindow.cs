using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LineScope.App.Model;
using LineScope.Core;
using LineSegment = LineScope.Core.LineSegment;

namespace LineScope.App.Views;

/// <summary>
/// One document window: the menu bar, the three zones (images, line profile, spectrum) stacked with splitters,
/// and a status bar. Without a document it shows a start screen.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly ContentControl _body = new();
    private readonly StatusBarItem _status = new();
    private readonly ProgressBar _progress = new() { Width = 90, Height = 12, IsIndeterminate = true, Visibility = Visibility.Collapsed };
    private readonly List<ZoneView> _zones = [];
    private MenuItem _devicePixels = null!;
    private Session? _session;

    public bool HasDocument => _session is not null;

    public MainWindow()
    {
        Title = "LineScope";
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1100, work.Width);
        Height = Math.Min(900, work.Height);
        MinWidth = 720;
        MinHeight = 560;
        UseLayoutRounding = true;
        AllowDrop = true;
        Background = SystemColors.WindowBrush;

        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        var statusBar = new StatusBar { Items = { _status, new StatusBarItem { Content = _progress, HorizontalAlignment = HorizontalAlignment.Right } } };
        DockPanel.SetDock(statusBar, Dock.Bottom);
        root.Children.Add(statusBar);
        root.Children.Add(_body);
        Content = root;

        Activated += (_, _) => { if (_session is not null) ActiveSession.Current = _session; };
        Closed += (_, _) => { if (ActiveSession.Current == _session && _session is not null) ActiveSession.Current = null; };
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        ShowEmpty();
    }

    // Content states

    public void ShowEmpty()
    {
        var open = new Button { Content = "Open Image…", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        open.Click += (_, _) => App.Current.ShowOpenDialog(this);
        var pattern = new Button { Content = "New Test Pattern…", Padding = new Thickness(14, 4, 14, 4) };
        pattern.Click += (_, _) => App.Current.ShowPatternGenerator(this);
        _body.Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "LineScope", FontSize = 26, FontWeight = FontWeights.Light, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = "Open an image (or drop one here) or create a test pattern.", Foreground = Ui.SecondaryText,
                    Margin = new Thickness(0, 6, 0, 16), HorizontalAlignment = HorizontalAlignment.Center,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { open, pattern } },
            },
        };
        _status.Content = "";
    }

    public void ShowLoading(string message)
    {
        _body.Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new ProgressBar { Width = 160, Height = 6, IsIndeterminate = true, Margin = new Thickness(0, 0, 0, 10) },
                new TextBlock { Text = message, Foreground = Ui.SecondaryText, HorizontalAlignment = HorizontalAlignment.Center },
            },
        };
    }

    public void LoadDocument(ImageDocument document, string fileName)
    {
        var session = new Session(document, fileName);
        _session = session;
        Title = $"{fileName} — LineScope";
        session.Changed += OnSessionChanged;
        session.Error += message =>
            MessageBox.Show(this, message, "Operation failed", MessageBoxButton.OK, MessageBoxImage.Warning);

        var grid = new Grid();
        _zones.Clear();
        double[] ideal = [420, 220, 220];
        double[] min = [180, 140, 140];
        foreach (var zone in ZoneInfo.All)
        {
            int i = (int)zone;
            if (i > 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
                var splitter = new GridSplitter
                {
                    Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext, ResizeDirection = GridResizeDirection.Rows,
                    Background = SystemColors.ControlDarkBrush,
                };
                Grid.SetRow(splitter, grid.RowDefinitions.Count - 1);
                grid.Children.Add(splitter);
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ideal[i], GridUnitType.Star), MinHeight = min[i] });
            var view = new ZoneView(zone, session);
            Grid.SetRow(view, grid.RowDefinitions.Count - 1);
            grid.Children.Add(view);
            _zones.Add(view);
        }
        _body.Content = grid;
        UpdateStatus();
        if (IsActive) ActiveSession.Current = session;
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnSessionChanged(SessionChange change)
    {
        foreach (var zone in _zones) zone.Update(change);
        if (change.HasFlag(SessionChange.Settings)) _devicePixels.IsChecked = _session?.UseDevicePixels == true;
        if ((change & (SessionChange.Running | SessionChange.Focus | SessionChange.Variants)) != 0) UpdateStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStatus()
    {
        if (_session is null) return;
        var v = _session.FocusedVariant;
        _status.Content = $"Focused: {v.Name} · {v.Buffer.Width}×{v.Buffer.Height} px";
        _progress.Visibility = _session.RunningOperations > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        MainWindow? reuse = this;
        foreach (var path in files)
        {
            App.Current.OpenFile(path, reuse);
            reuse = null;
        }
    }

    // Menus

    private Menu BuildMenu()
    {
        bool Has() => _session is not null;
        var menu = new Menu();

        var file = TopMenu("_File");
        Add(file, "_Open…", () => App.Current.ShowOpenDialog(this), key: new KeyGesture(Key.O, ModifierKeys.Control));
        var patterns = new MenuItem { Header = "New _Test Pattern" };
        foreach (var kind in TestPatternKindInfo.All)
            Add(patterns, kind.DisplayName(), () => App.Current.OpenPattern(new TestPatternSpec(kind)));
        patterns.Items.Add(new Separator());
        Add(patterns, "_Custom…", () => App.Current.ShowPatternGenerator(this),
            key: new KeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Alt));
        file.Items.Add(patterns);
        file.Items.Add(new Separator());
        Add(file, "_Export Image…", () => _session!.ExportFocused(this), Has,
            new KeyGesture(Key.E, ModifierKeys.Control | ModifierKeys.Shift));
        Add(file, "Close T_ab", () => _session!.CloseVariant(_session.FocusedVariantId),
            () => _session is { FocusedVariant.IsOriginal: false }, new KeyGesture(Key.Delete, ModifierKeys.Control));
        file.Items.Add(new Separator());
        Add(file, "_Close Window", Close, key: new KeyGesture(Key.W, ModifierKeys.Control));
        Add(file, "E_xit", () => Application.Current.Shutdown(), gestureText: "Alt+F4");
        menu.Items.Add(file);

        var filter = TopMenu("F_ilter");
        Add(filter, "_Gaussian Blur…", () => Request(new OperationRequest.GaussianBlur()), Has);
        Add(filter, "_Box Blur…", () => Request(new OperationRequest.BoxBlur()), Has);
        Add(filter, "_Median 3×3", () => Perform(new FilterKind.Median()), Has);
        Add(filter, "_Noise Reduction…", () => Request(new OperationRequest.NoiseReduction()), Has);
        filter.Items.Add(new Separator());
        Add(filter, "_Sharpen (Unsharp Mask)…", () => Request(new OperationRequest.UnsharpMask()), Has);
        Add(filter, "_Edge Detect (Sobel)", () => Perform(new FilterKind.Sobel()), Has);
        Add(filter, "_Laplacian", () => Perform(new FilterKind.Laplacian()), Has);
        Add(filter, "Em_boss", () => Perform(new FilterKind.Emboss()), Has);
        filter.Items.Add(new Separator());
        Add(filter, "G_rayscale", () => Perform(new FilterKind.Grayscale()), Has);
        Add(filter, "_Invert", () => Perform(new FilterKind.Invert()), Has);
        menu.Items.Add(filter);

        var resample = TopMenu("_Resample");
        foreach (var method in ResampleMethodInfo.All)
            Add(resample, method.DisplayName() + "…", () => Request(new OperationRequest.Resample(method)), Has);
        menu.Items.Add(resample);

        var view = TopMenu("_View");
        foreach (var zone in ZoneInfo.All)
            Add(view, $"Split {zone.Title()}", () => _session!.Split(zone), Has);
        foreach (var zone in ZoneInfo.All)
            Add(view, $"Unsplit {zone.Title()}", () => _session!.Unsplit(zone), () => _session?.Panes[(int)zone].Count > 1);
        view.Items.Add(new Separator());
        _devicePixels = new MenuItem { Header = "Actual _Device Pixels", IsCheckable = true };
        _devicePixels.Checked += (_, _) => { if (_session is not null) _session.UseDevicePixels = true; };
        _devicePixels.Unchecked += (_, _) => { if (_session is not null) _session.UseDevicePixels = false; };
        view.SubmenuOpened += (_, _) =>
        {
            _devicePixels.IsEnabled = Has();
            _devicePixels.IsChecked = _session?.UseDevicePixels == true;
        };
        view.Items.Add(_devicePixels);
        Add(view, "Reset _Line", () => _session!.Line = LineSegment.Initial, Has);
        view.Items.Add(new Separator());
        Add(view, "Show _Inspector", () => App.Current.ShowInspector(), key: new KeyGesture(Key.I, ModifierKeys.Control | ModifierKeys.Alt));
        menu.Items.Add(view);

        return menu;
    }

    private static MenuItem TopMenu(string header) => new() { Header = header };

    private void Add(MenuItem parent, string header, Action action, Func<bool>? canExecute = null, KeyGesture? key = null,
        string? gestureText = null)
    {
        var command = new RelayCommand(action, canExecute);
        var item = new MenuItem { Header = header, Command = command };
        if (key is not null)
        {
            InputBindings.Add(new KeyBinding(command, key));
            item.InputGestureText = key.GetDisplayStringForCulture(System.Globalization.CultureInfo.CurrentUICulture);
        }
        else if (gestureText is not null)
        {
            item.InputGestureText = gestureText;
        }
        parent.Items.Add(item);
    }

    private void Perform(FilterKind filter) => _session?.Perform(new ImageOperation.Filter(filter));

    private void Request(OperationRequest request)
    {
        if (_session is null) return;
        var dialog = new ParameterDialog(request, _session) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Operation is { } op) _session.Perform(op);
    }
}
