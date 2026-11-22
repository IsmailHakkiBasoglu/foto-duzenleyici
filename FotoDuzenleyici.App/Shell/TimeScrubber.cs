using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>
/// Galerinin sağındaki zaman çubuğu: yıl etiketleri, ay çentikleri, o anki konumun tutamacı. Tutup sürükleyince
/// (ya da tıklayınca) galeri o konuma gider; sürüklerken yanda "Ekim 2026" balonu. Konum ↔ piksel eşlemesi
/// kaydırılabilir yükseklik üzerinden: en üst en yeni, en alt en eski.
/// </summary>
public sealed class TimeScrubber : FrameworkElement
{
    const double MinLabelGap = 18;

    readonly Popup _bubble;
    readonly TextBlock _bubbleText;
    GalleryLayout? _layout;
    double _viewport, _offset;
    bool _dragging;

    public TimeScrubber()
    {
        Cursor = Cursors.Hand;
        _bubbleText = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 4, 10, 4) };
        _bubbleText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var border = new Border { CornerRadius = new CornerRadius(6), Child = _bubbleText, BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorTertiaryBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        _bubble = new Popup
        {
            Child = border, PlacementTarget = this, Placement = PlacementMode.Relative,
            AllowsTransparency = true, IsHitTestVisible = false,
        };
    }

    /// <summary>Kullanıcı bir konum seçti (piksel cinsinden galeri kaydırma konumu).</summary>
    public event Action<double>? Scrubbed;
    public event Action? DragStarted;
    public event Action? DragEnded;

    public bool IsDragging => _dragging;

    public void SetLayout(GalleryLayout? layout, double viewport, double offset)
    {
        _layout = layout;
        _viewport = viewport;
        _offset = offset;
        Visibility = layout is { TotalHeight: > 0 } l && l.TotalHeight > viewport ? Visibility.Visible : Visibility.Hidden;
        InvalidateVisual();
    }

    public void SetOffset(double offset)
    {
        _offset = offset;
        InvalidateVisual();
    }

    double Scrollable => Math.Max(1, (_layout?.TotalHeight ?? 0) - _viewport);
    double Track => Math.Max(1, ActualHeight - 8);
    double PixelOf(double offset) => 4 + Math.Clamp(offset / Scrollable, 0, 1) * Track;
    double OffsetOf(double y) => Math.Clamp((y - 4) / Track, 0, 1) * Scrollable;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize)); // her yere tıklanabilsin
        if (_layout is null) return;
        var fg = (Brush?)TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray;
        var tick = (Brush?)TryFindResource("ControlStrokeColorDefaultBrush") ?? Brushes.Gray;
        var accent = (Brush?)TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.DodgerBlue;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tickPen = new Pen(tick, 1);

        // Ay çentikleri
        for (int i = 0; i < _layout.Rows.Count; i++)
            if (_layout.Rows[i] is GalleryHeaderRow)
            {
                double y = PixelOf(_layout.OffsetOfRow(i));
                dc.DrawLine(tickPen, new Point(ActualWidth - 8, y), new Point(ActualWidth - 3, y));
            }

        // Yıl etiketleri (üst üste binenler atlanır)
        double last = double.NegativeInfinity;
        foreach (var (year, off) in _layout.YearMarks)
        {
            double y = PixelOf(off);
            if (y - last < MinLabelGap) continue;
            var text = new FormattedText(year.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, fg, dpi);
            double ty = Math.Min(y, ActualHeight - text.Height);
            dc.DrawText(text, new Point(ActualWidth - 10 - text.Width, ty));
            last = y;
        }

        // Tutamaç: o anki konum
        double h = PixelOf(_offset);
        dc.DrawRoundedRectangle(accent, null, new Rect(ActualWidth - 10, h - 2, 9, 4), 2, 2);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_layout is null) return;
        _dragging = true;
        CaptureMouse();
        DragStarted?.Invoke();
        ScrubTo(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging) ScrubTo(e.GetPosition(this).Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    void EndDrag()
    {
        _dragging = false;
        _bubble.IsOpen = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        DragEnded?.Invoke();
    }

    void ScrubTo(double y)
    {
        double offset = OffsetOf(y);
        _offset = offset;
        Scrubbed?.Invoke(offset);
        if (_layout?.MonthAtOffset(offset) is { } m)
        {
            _bubbleText.Text = m.ShortTitle;
            _bubble.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = _bubble.Child.DesiredSize;
            _bubble.HorizontalOffset = -size.Width - 6;
            _bubble.VerticalOffset = Math.Clamp(y - size.Height / 2, 0, Math.Max(0, ActualHeight - size.Height));
            _bubble.IsOpen = true;
        }
        InvalidateVisual();
    }
}
