using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using LineScope.App.Model;

namespace LineScope.App.Views;

/// <summary>A tab strip listing every variant, plus the zone's content for the selected one.</summary>
public sealed class PaneView : DockPanel
{
    private readonly Zone _zone;
    private readonly int _paneIndex;
    private readonly Session _session;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 3, 6, 3) };
    private readonly FrameworkElement _content;

    public PaneView(Zone zone, int paneIndex, Session session)
    {
        _zone = zone;
        _paneIndex = paneIndex;
        _session = session;
        LastChildFill = true;

        var strip = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tabs,
            Focusable = false,
        };
        strip.PreviewMouseWheel += (_, e) =>
        {
            strip.ScrollToHorizontalOffset(strip.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };
        SetDock(strip, Dock.Top);
        Children.Add(strip);
        var divider = new Border { Height = 1, Background = SystemColors.ControlDarkBrush, Opacity = 0.5 };
        SetDock(divider, Dock.Top);
        Children.Add(divider);

        var variant = SelectedVariant;
        _content = zone switch
        {
            Zone.Images => new ImageCanvas(session, variant),
            Zone.Profile => new ProfileChart(session, variant),
            _ => new SpectrumChart(session, variant),
        };
        Children.Add(_content);
        RebuildTabs();
    }

    private ImageVariant SelectedVariant
    {
        get
        {
            var panes = _session.Panes[(int)_zone];
            var id = _paneIndex < panes.Count ? panes[_paneIndex].VariantId : _session.Original.Id;
            return _session.Variant(id) ?? _session.Original;
        }
    }

    public void Update(SessionChange change)
    {
        if ((change & (SessionChange.Variants | SessionChange.Panes | SessionChange.Focus)) != 0)
        {
            RebuildTabs();
            var variant = SelectedVariant;
            switch (_content)
            {
                case ImageCanvas canvas when canvas.Variant != variant: canvas.Variant = variant; break;
                case ProfileChart chart: chart.Variant = variant; break;
                case SpectrumChart chart: chart.Variant = variant; break;
            }
        }
        switch (_content)
        {
            case ImageCanvas canvas:
                if (change.HasFlag(SessionChange.Line)) canvas.RefreshLine();
                if (change.HasFlag(SessionChange.Settings)) canvas.UpdateSize();
                break;
            case ChartView chart:
                if ((change & (SessionChange.Line | SessionChange.Settings | SessionChange.Variants | SessionChange.Panes)) != 0)
                    chart.InvalidateVisual();
                break;
        }
    }

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        var selectedId = SelectedVariant.Id;
        foreach (var v in _session.Variants)
            _tabs.Children.Add(Tab(v, v.Id == selectedId));
    }

    private Border Tab(ImageVariant v, bool selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (v.Id == _session.FocusedVariantId)
        {
            row.Children.Add(new Ellipse
            {
                Width = 5, Height = 5, Fill = SystemColors.HighlightBrush,
                Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
            });
        }
        row.Children.Add(new TextBlock { Text = v.Name, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = $"{v.Buffer.Width}×{v.Buffer.Height}", FontSize = 11.5, Foreground = Ui.SecondaryText,
            Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });

        var highlight = SystemColors.HighlightColor;
        var tab = new Border
        {
            Child = row,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 2, 0),
            CornerRadius = new CornerRadius(5),
            Background = selected
                ? new SolidColorBrush(Color.FromArgb(56, highlight.R, highlight.G, highlight.B))
                : Brushes.Transparent,
            ToolTip = v.Name,
            Cursor = Cursors.Hand,
        };
        tab.MouseLeftButtonUp += (_, _) => _session.Select(v.Id, _zone, _paneIndex);

        var menu = new ContextMenu();
        var export = new MenuItem { Header = "Export Image…" };
        export.Click += (_, _) =>
        {
            _session.Select(v.Id, _zone, _paneIndex);
            _session.ExportFocused(Window.GetWindow(this));
        };
        var close = new MenuItem { Header = "Close Tab", IsEnabled = !v.IsOriginal };
        close.Click += (_, _) => _session.CloseVariant(v.Id);
        menu.Items.Add(export);
        menu.Items.Add(close);
        tab.ContextMenu = menu;
        return tab;
    }
}
