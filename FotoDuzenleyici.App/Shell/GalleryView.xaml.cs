using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>
/// Sürekli galeri. Mantık <see cref="GalleryViewModel"/> ve <see cref="GalleryLayout"/>'ta; burada yalnız görünüm
/// işleri: genişliğe göre satırlara bölmek, zaman çubuğunu kaydırmayla eşlemek, seçim ve tuşlar. Galeri yeniden
/// kurulunca (süzgeç, Yeniden tara, ay görünümünde atma) görünen ay korunur.
/// </summary>
public partial class GalleryView : UserControl
{
    public const double HeaderHeight = 40;
    public const double RowHeight = MonthView.TileHeight + 4;
    const double TileStride = MonthView.TileWidth + 4;

    GalleryViewModel? _vm;
    GalleryLayout? _layout;
    ScrollViewer? _sv;
    int _columns;
    MonthPhoto? _selected;
    /// <summary>Yeniden kurulumda geri dönülecek yer: ay ve ayın başlığından uzaklık.</summary>
    (int Year, int Month, double Delta)? _anchor;

    public GalleryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as GalleryViewModel);
        Scrubber.Scrubbed += o => _sv?.ScrollToVerticalOffset(o);
        Scrubber.DragStarted += () => ThumbTracker.Pause(true);
        Scrubber.DragEnded += () => ThumbTracker.Pause(false);
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(() => Focus()); };
    }

    void Attach(GalleryViewModel? vm)
    {
        _anchor ??= CurrentAnchor();
        _vm = vm;
        Select(null);
        Rebuild();
    }

    int Columns() => Math.Max(1, (int)((List.ActualWidth - 12) / TileStride));

    void Rebuild()
    {
        _columns = Columns();
        _layout = _vm is null ? null : GalleryLayout.Build(_vm.Months, _columns, HeaderHeight, RowHeight);
        List.ItemsSource = _layout?.Rows;
        var anchor = _anchor;
        _anchor = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (anchor is { } a && _layout is not null && _sv is not null)
            {
                int row = -1;
                for (int i = 0; i < _layout.Rows.Count; i++)
                    if (_layout.Rows[i] is GalleryHeaderRow h && h.Month.Year == a.Year && h.Month.Month == a.Month) { row = i; break; }
                if (row >= 0) _sv.ScrollToVerticalOffset(_layout.OffsetOfRow(row) + a.Delta);
            }
            SyncScrubber();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    (int, int, double)? CurrentAnchor()
    {
        if (_layout is null || _sv is null || _layout.MonthAtOffset(_sv.VerticalOffset) is not { } m) return null;
        for (int i = 0; i < _layout.Rows.Count; i++)
            if (_layout.Rows[i] is GalleryHeaderRow h && h.Month == m) return (m.Year, m.Month, _sv.VerticalOffset - _layout.OffsetOfRow(i));
        return null;
    }

    void SyncScrubber() => Scrubber.SetLayout(_layout, _sv?.ViewportHeight ?? List.ActualHeight, _sv?.VerticalOffset ?? 0);

    void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _sv ??= FindScrollViewer(List);
        if (e.WidthChanged && Columns() != _columns)
        {
            _anchor = CurrentAnchor();
            Rebuild();
        }
        else SyncScrubber();
    }

    void List_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        _sv ??= e.OriginalSource as ScrollViewer;
        if (e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0) SyncScrubber();
        else if (!Scrubber.IsDragging) Scrubber.SetOffset(e.VerticalOffset);
    }

    static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    // ── Seçim, açma ─────────────────────────────────────────────

    void Select(MonthPhoto? p)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = p;
        if (p is not null) p.IsSelected = true;
    }

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MonthPhoto p) return;
        Select(p);
        Focus();
        if (e.ClickCount == 2) _vm?.OpenFileCommand.Execute(p);
        e.Handled = true;
    }

    static MonthPhoto? PhotoOf(object sender) =>
        ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is FrameworkElement { DataContext: MonthPhoto p } ? p : null;

    void OpenMenu_Click(object sender, RoutedEventArgs e) => _vm?.OpenFileCommand.Execute(PhotoOf(sender));

    void MonthMenu_Click(object sender, RoutedEventArgs e)
    {
        if (PhotoOf(sender) is not { } p || _vm is null) return;
        _vm.OpenMonthCommand.Execute(_vm.Months.FirstOrDefault(m => m.Photos.Contains(p)));
    }

    void Thumb_Loaded(object sender, RoutedEventArgs e) => ThumbTracker.Attach((FrameworkElement)sender);

    void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_sv is null) return;
        switch (e.Key)
        {
            case Key.Home: _sv.ScrollToTop(); break;
            case Key.End: _sv.ScrollToBottom(); break;
            case Key.PageDown: _sv.PageDown(); break;
            case Key.PageUp: _sv.PageUp(); break;
            case Key.Enter when _selected is not null: _vm?.OpenFileCommand.Execute(_selected); break;
            default: return;
        }
        e.Handled = true;
    }
}
