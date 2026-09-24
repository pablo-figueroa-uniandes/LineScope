using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LineScope.App.Model;
using LineScope.Core;
using LineSegment = LineScope.Core.LineSegment;

namespace LineScope.App.Views;

/// <summary>
/// Shows a variant at its true scale (1 image pixel = 1 DIP, or 1 device pixel) with scrolling,
/// and lets the user drag the shared selection line.
/// </summary>
public sealed class ImageCanvas : ScrollViewer
{
    private readonly Session _session;
    private readonly Grid _surface;
    private readonly Image _image;
    private readonly LineOverlay _overlay;
    private ImageVariant _variant;

    private enum DragMode { Start, End, Whole, Draw }
    private Point? _pressPoint;
    private DragMode? _dragMode;
    private LineSegment _dragInitial;

    public ImageCanvas(Session session, ImageVariant variant)
    {
        _session = session;
        _variant = variant;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Background = SystemColors.AppWorkspaceBrush;
        Focusable = false;

        _image = new Image { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _overlay = new LineOverlay { IsHitTestVisible = false };
        _surface = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = Cursors.Cross,
            Children = { _image, _overlay },
        };
        Content = _surface;

        _surface.MouseLeftButtonDown += OnDown;
        _surface.MouseMove += OnMove;
        _surface.MouseLeftButtonUp += OnUp;
        _surface.LostMouseCapture += (_, _) => { _pressPoint = null; _dragMode = null; };

        Loaded += (_, _) => UpdateSize();
        Refresh();
    }

    public ImageVariant Variant
    {
        get => _variant;
        set { _variant = value; Refresh(); }
    }

    public void Refresh()
    {
        _image.Source = _variant.Bitmap;
        UpdateSize();
        _overlay.Line = _session.Line;
    }

    public void RefreshLine() => _overlay.Line = _session.Line;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateSize();
    }

    private Size DisplaySize
    {
        get
        {
            double scale = _session.UseDevicePixels ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1;
            return new Size(_variant.Buffer.Width / scale, _variant.Buffer.Height / scale);
        }
    }

    public void UpdateSize()
    {
        var size = DisplaySize;
        _surface.Width = size.Width;
        _surface.Height = size.Height;
    }

    // Mouse handling

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(_surface);
        _dragMode = null;
        _surface.CaptureMouse();
        _session.FocusedVariantId = _variant.Id;
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressPoint is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_surface);
        if (_dragMode is null)
        {
            if ((p - start).Length < 2) return;
            _dragMode = HitTest(start);
            _dragInitial = _session.Line;
        }
        _session.Line = UpdatedLine(_dragMode.Value, start, p);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _pressPoint = null;
        _dragMode = null;
        _surface.ReleaseMouseCapture();
    }

    private DragMode HitTest(Point p)
    {
        var size = DisplaySize;
        var a = ToPoint(_session.Line.Start, size);
        var b = ToPoint(_session.Line.End, size);
        if ((p - a).Length <= 9) return DragMode.Start;
        if ((p - b).Length <= 9) return DragMode.End;
        if (DistanceToSegment(p, a, b) <= 6) return DragMode.Whole;
        return DragMode.Draw;
    }

    private LineSegment UpdatedLine(DragMode mode, Point start, Point location)
    {
        var size = DisplaySize;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var current = Normalized(location, size);
        var line = _dragInitial;
        switch (mode)
        {
            case DragMode.Start:
                line = line with { Start = Snapped(current, line.End, size, shift) };
                break;
            case DragMode.End:
                line = line with { End = Snapped(current, line.Start, size, shift) };
                break;
            case DragMode.Draw:
                var s = Normalized(start, size);
                line = new LineSegment(s, Snapped(current, s, size, shift));
                break;
            case DragMode.Whole:
                // Move both endpoints, limiting the offset so neither leaves the image.
                var i = _dragInitial;
                double dx = (location.X - start.X) / size.Width;
                double dy = (location.Y - start.Y) / size.Height;
                dx = Math.Min(Math.Max(dx, -Math.Min(i.Start.X, i.End.X)), 1 - Math.Max(i.Start.X, i.End.X));
                dy = Math.Min(Math.Max(dy, -Math.Min(i.Start.Y, i.End.Y)), 1 - Math.Max(i.Start.Y, i.End.Y));
                line = new LineSegment(new NormalizedPoint(i.Start.X + dx, i.Start.Y + dy),
                    new NormalizedPoint(i.End.X + dx, i.End.Y + dy));
                break;
        }
        return line;
    }

    /// <summary>With Shift held, constrains the segment to horizontal or vertical (in pixel space).</summary>
    private static NormalizedPoint Snapped(NormalizedPoint p, NormalizedPoint anchor, Size size, bool enabled)
    {
        if (!enabled) return p;
        double dx = Math.Abs(p.X - anchor.X) * size.Width;
        double dy = Math.Abs(p.Y - anchor.Y) * size.Height;
        return dx >= dy ? new NormalizedPoint(p.X, anchor.Y) : new NormalizedPoint(anchor.X, p.Y);
    }

    private static NormalizedPoint Normalized(Point p, Size size) =>
        new NormalizedPoint(p.X / size.Width, p.Y / size.Height).Clamped();

    private static Point ToPoint(NormalizedPoint n, Size size) => new(n.X * size.Width, n.Y * size.Height);

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 <= 0) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }
}

/// <summary>Draws the selection line: a filled handle at the start (position 0) and a hollow one at the end.</summary>
internal sealed class LineOverlay : FrameworkElement
{
    private static readonly Pen Shadow = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(179, 0, 0, 0)), 3));
    private static readonly Pen Yellow = Frozen(new Pen(Brushes.Yellow, 1.25));
    private static readonly Pen DotOutline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(179, 0, 0, 0)), 1));
    private static readonly Pen EndOutline = Frozen(new Pen(Brushes.Yellow, 1.5));
    private static readonly Brush EndFill = Frozen(new SolidColorBrush(Color.FromArgb(89, 0, 0, 0)));

    private LineSegment _line = LineSegment.Initial;

    public LineSegment Line
    {
        get => _line;
        set { _line = value; InvalidateVisual(); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var a = new Point(_line.Start.X * w, _line.Start.Y * h);
        var b = new Point(_line.End.X * w, _line.End.Y * h);
        dc.DrawLine(Shadow, a, b);
        dc.DrawLine(Yellow, a, b);
        const double r = 5;
        dc.DrawEllipse(Brushes.Yellow, DotOutline, a, r, r);
        dc.DrawEllipse(EndFill, EndOutline, b, r, r);
    }

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
