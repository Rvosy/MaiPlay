using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MaiPlay.Rendering;

/// <summary>左键拖动定位，滚轮缩放，右键拖动平移可见区间。</summary>
public sealed class SeekTimeline : Control
{
    private double _start, _end = 1, _viewStart, _viewEnd = 1, _time, _dragTime;
    private bool _scrubbing, _panning;
    private double _panX, _panStart;
    private IReadOnlyList<double> _issues = [];
    public event Action? ScrubStarted;
    public event Action<double>? SeekRequested;
    public double ViewStart => _viewStart;
    public double ViewEnd => _viewEnd;
    private double Span => _viewEnd - _viewStart;
    private double TrackWidth => Math.Max(1, Bounds.Width - 16);
    private double X(double time) => 8 + (time - _viewStart) / Span * TrackWidth;
    private double At(double x) => Math.Clamp(_viewStart + (x - 8) / TrackWidth * Span, _start, _end);

    public SeekTimeline()
    {
        Focusable = true;
        ClipToBounds = true;
        ToolTip.SetTip(this, "拖动定位 · 滚轮缩放 · 右键拖动平移 · 红线为未解或误判位置");
    }

    public void SetRange(double start, double end)
    {
        _start = start;
        _end = Math.Max(end, start + .001);
        Fit();
    }

    public void SetTime(double time) { _time = time; InvalidateVisual(); }
    public void SetIssues(IReadOnlyList<double> issues) { _issues = issues; InvalidateVisual(); }
    public void Fit() { _viewStart = _start; _viewEnd = _end; InvalidateVisual(); }
    public void Zoom(double factor) => ZoomAt(factor, Math.Clamp(_time, _viewStart, _viewEnd));

    private void ZoomAt(double factor, double anchor)
    {
        var ratio = Math.Clamp((anchor - _viewStart) / Span, 0, 1);
        var span = Math.Clamp(Span * factor, Math.Min(2, _end - _start), _end - _start);
        SetView(anchor - ratio * span, span);
    }

    private void SetView(double start, double span)
    {
        _viewStart = Math.Clamp(start, _start, _end - span);
        _viewEnd = _viewStart + span;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0));
        context.DrawLine(new Pen(Brush.Parse("#C4D3DF"), 4), new(8, 18), new(Bounds.Width - 8, 18));
        foreach (var issue in _issues.Where(t => t >= _viewStart && t <= _viewEnd))
            context.DrawLine(new Pen(Brush.Parse("#C34747"), 2), new(X(issue), 8), new(X(issue), 26));
        var time = _scrubbing ? _dragTime : _time;
        if (time >= _viewStart && time <= _viewEnd)
        {
            var x = X(time);
            context.DrawLine(new Pen(Brush.Parse("#087D91"), 3), new(x, 4), new(x, 28));
            context.DrawEllipse(Brush.Parse("#087D91"), null, new(x, 18), 6, 6);
        }
        Label(context, $"{_viewStart:F1}s", 8, 30);
        var end = Text($"{_viewEnd:F1}s");
        context.DrawText(end, new(Math.Max(8, Bounds.Width - end.Width - 8), 30));
        if (_scrubbing) Label(context, $"{time:F3}s", Math.Clamp(X(time) - 28, 8, Math.Max(8, Bounds.Width - 70)), 30);
    }

    private static FormattedText Text(string text) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface("Segoe UI"), 10, Brush.Parse("#426078"));
    private static void Label(DrawingContext context, string text, double x, double y) => context.DrawText(Text(text), new(x, y));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            _scrubbing = true;
            ScrubStarted?.Invoke();
            _dragTime = At(point.Position.X);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            _panning = true;
            _panX = point.Position.X;
            _panStart = _viewStart;
        }
        else return;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_scrubbing) { _dragTime = At(e.GetPosition(this).X); InvalidateVisual(); }
        if (_panning) SetView(_panStart - (e.GetPosition(this).X - _panX) / TrackWidth * Span, Span);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var seek = _scrubbing;
        var time = At(e.GetPosition(this).X);
        _scrubbing = _panning = false;
        e.Pointer.Capture(null);
        if (seek) SeekRequested?.Invoke(time);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _scrubbing = _panning = false;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_scrubbing || _panning) return;
        ZoomAt(Math.Pow(.8, e.Delta.Y), At(e.GetPosition(this).X));
        e.Handled = true;
    }
}
