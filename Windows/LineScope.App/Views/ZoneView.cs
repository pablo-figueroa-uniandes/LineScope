using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LineScope.App.Model;
using LineScope.Core;
using LineSegment = LineScope.Core.LineSegment;

namespace LineScope.App.Views;

/// <summary>One horizontal zone: a header with its controls, and one or more side-by-side panes.</summary>
public sealed class ZoneView : DockPanel
{
    private readonly Zone _zone;
    private readonly Session _session;
    private readonly Grid _panesGrid = new();
    private readonly List<PaneView> _panes = [];
    private readonly StackPanel _controls = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock? _lineText;
    private Button _unsplit = null!;
    private ColorModel _builtFor;

    public ZoneView(Zone zone, Session session)
    {
        _zone = zone;
        _session = session;

        var header = new DockPanel { Margin = new Thickness(10, 4, 10, 4), LastChildFill = false };
        var title = new TextBlock
        {
            Text = zone.Title(), FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
        };
        SetDock(title, Dock.Left);
        header.Children.Add(title);
        var splitButtons = SplitButtons();
        SetDock(splitButtons, Dock.Right);
        header.Children.Add(splitButtons);
        var divider = new Border
        {
            Width = 1, Height = 16, Margin = new Thickness(10, 0, 10, 0), Background = SystemColors.ControlDarkBrush,
        };
        SetDock(divider, Dock.Right);
        header.Children.Add(divider);
        SetDock(_controls, Dock.Right);
        header.Children.Add(_controls);

        var bar = new Border
        {
            Child = header,
            Background = SystemColors.ControlBrush,
            BorderBrush = SystemColors.ControlDarkBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
        SetDock(bar, Dock.Top);
        Children.Add(bar);
        Children.Add(_panesGrid);

        BuildControls();
        RebuildPanes();
    }

    public void Update(SessionChange change)
    {
        if (change.HasFlag(SessionChange.Panes) && _session.Panes[(int)_zone].Count != _panes.Count)
            RebuildPanes();
        foreach (var pane in _panes) pane.Update(change);
        if (change.HasFlag(SessionChange.Line) && _lineText is not null) _lineText.Text = LineDescription();
        if (change.HasFlag(SessionChange.Settings) && _zone == Zone.Profile && _builtFor != _session.ColorModel)
            BuildControls();
        _unsplit.IsEnabled = _session.Panes[(int)_zone].Count > 1;
    }

    private void RebuildPanes()
    {
        _panesGrid.Children.Clear();
        _panesGrid.ColumnDefinitions.Clear();
        _panes.Clear();
        int count = _session.Panes[(int)_zone].Count;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                _panesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
                var splitter = new GridSplitter
                {
                    Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    Background = SystemColors.ControlBrush,
                };
                Grid.SetColumn(splitter, _panesGrid.ColumnDefinitions.Count - 1);
                _panesGrid.Children.Add(splitter);
            }
            _panesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
            var pane = new PaneView(_zone, i, _session);
            Grid.SetColumn(pane, _panesGrid.ColumnDefinitions.Count - 1);
            _panesGrid.Children.Add(pane);
            _panes.Add(pane);
        }
    }

    private void BuildControls()
    {
        _controls.Children.Clear();
        _lineText = null;
        _builtFor = _session.ColorModel;
        switch (_zone)
        {
            case Zone.Images:
                _lineText = new TextBlock
                {
                    Text = LineDescription(), FontFamily = new FontFamily("Consolas"), FontSize = 11.5,
                    Foreground = Ui.SecondaryText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
                };
                _controls.Children.Add(_lineText);
                var reset = new Button { Content = "Reset Line", Padding = new Thickness(8, 1, 8, 1) };
                reset.Click += (_, _) => _session.Line = LineSegment.Initial;
                _controls.Children.Add(reset);
                break;

            case Zone.Profile:
                var models = Ui.Segmented(ColorModelInfo.All.Select(m => m.DisplayName()).ToList(),
                    Array.IndexOf(ColorModelInfo.All, _session.ColorModel), i => _session.ColorModel = ColorModelInfo.All[i]);
                models.Margin = new Thickness(0, 0, 12, 0);
                models.ToolTip = "Color space";
                _controls.Children.Add(models);
                var channels = Ui.Segmented(_session.ColorModel.Channels(), _session.ChannelIndex, i => _session.ChannelIndex = i);
                channels.ToolTip = "Channel";
                _controls.Children.Add(channels);
                break;

            case Zone.Spectrum:
                _controls.Children.Add(Combo("Window", WindowFunctionInfo.All.Select(w => w.DisplayName()).ToList(),
                    Array.IndexOf(WindowFunctionInfo.All, _session.Window), i => _session.Window = WindowFunctionInfo.All[i]));
                _controls.Children.Add(Combo("Axis", FrequencyAxisInfo.All.Select(a => a.DisplayName()).ToList(),
                    Array.IndexOf(FrequencyAxisInfo.All, _session.FrequencyAxis), i => _session.FrequencyAxis = FrequencyAxisInfo.All[i]));
                _controls.Children.Add(Check("Remove mean", _session.RemoveMean, v => _session.RemoveMean = v));
                _controls.Children.Add(Check("dB", _session.LogMagnitude, v => _session.LogMagnitude = v));
                break;
        }
    }

    private static StackPanel Combo(string label, List<string> items, int selected, Action<int> onSelect)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = selected, MinWidth = 90, Padding = new Thickness(6, 1, 6, 1) };
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) onSelect(combo.SelectedIndex); };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 12, 0),
            Children =
            {
                new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) },
                combo,
            },
        };
    }

    private static CheckBox Check(string label, bool value, Action<bool> onChange)
    {
        var box = new CheckBox { Content = label, IsChecked = value, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        box.Checked += (_, _) => onChange(true);
        box.Unchecked += (_, _) => onChange(false);
        return box;
    }

    private StackPanel SplitButtons()
    {
        var split = new Button { Content = "Split", Padding = new Thickness(8, 1, 8, 1), ToolTip = $"Split: add a pane to {_zone.Title()}" };
        split.Click += (_, _) => _session.Split(_zone);
        _unsplit = new Button
        {
            Content = "Unsplit", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0),
            ToolTip = "Unsplit: remove the last pane", IsEnabled = _session.Panes[(int)_zone].Count > 1,
        };
        ToolTipService.SetShowOnDisabled(_unsplit, true);
        _unsplit.Click += (_, _) => _session.Unsplit(_zone);
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { split, _unsplit } };
    }

    private string LineDescription()
    {
        var l = _session.Line;
        static string P(double v) => Fmt.Fixed(v, 3);
        return $"({P(l.Start.X)}, {P(l.Start.Y)}) → ({P(l.End.X)}, {P(l.End.Y)})";
    }
}
