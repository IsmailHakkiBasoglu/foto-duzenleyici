using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Izgarada bir gün başlığı.</summary>
public sealed record MonthHeaderRow(string Header, DateOnly? Day);

/// <summary>Izgarada bir sıra karo (pencere genişliğine göre kaç karo sığıyorsa).</summary>
public sealed record MonthTileRow(IReadOnlyList<MonthPhoto> Photos);

/// <summary>
/// Ay görünümü: Izgara, Takvim, Ayıkla ve Gözden geçir. Mantık <see cref="MonthViewModel"/>'de; burada yalnız
/// görünüm işleri: ızgarayı genişliğe göre sıralara bölmek (yalnız görünen sıralar kurulur), tuşlar, sürükleme,
/// büyük resmin yüklenmesi.
/// </summary>
public partial class MonthView : UserControl
{
    public const double TileWidth = 132;
    public const double TileHeight = 100;
    const double TileStride = TileWidth + 4;
    /// <summary>Sürükleme, resmin bu oranından uzun olursa karar sayılır.</summary>
    const double DragThreshold = 0.25;

    readonly LargeImage _large = new();
    MonthViewModel? _vm;
    int _columns;
    List<object> _rows = new();
    MonthPhoto? _selected;
    Window? _window;

    public MonthView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Attach(e.NewValue as MonthViewModel);
        Loaded += (_, _) =>
        {
            Focus();
            _window = Window.GetWindow(this);
            if (_window is not null) _window.PreviewKeyDown += Window_PreviewKeyDown;
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null) _window.PreviewKeyDown -= Window_PreviewKeyDown;
            _window = null;
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(() => Focus()); };
    }

    void Attach(MonthViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = vm;
        _selected = null;
        _large.Clear();
        if (vm is not null) vm.PropertyChanged += Vm_PropertyChanged;
        BuildRows();
        ShowCurrent();
    }

    void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MonthViewModel.Days):
                if (_selected is not null && !_vm!.Photos.Contains(_selected)) Select(null);
                BuildRows();
                break;
            case nameof(MonthViewModel.ScrollTarget):
                Dispatcher.BeginInvoke(ScrollToTarget, System.Windows.Threading.DispatcherPriority.Loaded);
                break;
            case nameof(MonthViewModel.Current):
                ShowCurrent();
                if (_vm!.Current is { } c) Strip.ScrollIntoView(c);
                break;
            case nameof(MonthViewModel.IsTriage):
                ResetCard();
                if (_vm!.IsTriage) { ShowCurrent(); if (_vm.Current is { } cur) Strip.ScrollIntoView(cur); }
                else if (_vm.Current is { } last) { Select(last); ScrollToPhoto(last); }
                Focus();
                break;
            case nameof(MonthViewModel.IsReview):
                Focus();
                break;
        }
    }

    // ── Izgara ───────────────────────────────────────────────────

    void GridList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged && Columns() != _columns) BuildRows();
    }

    int Columns() => Math.Max(1, (int)((GridList.ActualWidth - 24 - SystemParameters.VerticalScrollBarWidth) / TileStride));

    void BuildRows()
    {
        _columns = Columns();
        var rows = new List<object>();
        foreach (var d in _vm?.Days ?? Array.Empty<DayGroup>())
        {
            rows.Add(new MonthHeaderRow(d.Header, d.Day));
            for (int i = 0; i < d.Photos.Count; i += _columns)
                rows.Add(new MonthTileRow(d.Photos.Skip(i).Take(_columns).ToList()));
        }
        _rows = rows;
        GridList.ItemsSource = rows;
    }

    void ScrollToTarget()
    {
        if (_vm?.ScrollTarget is not { } day) return;
        int i = _rows.FindIndex(r => r is MonthHeaderRow h && h.Day == day);
        if (i >= 0) Scroller(GridList)?.ScrollToVerticalOffset(i); // sıra sıra kaydırma: ofset = sıra numarası
    }

    void ScrollToPhoto(MonthPhoto p)
    {
        int i = _rows.FindIndex(r => r is MonthTileRow t && t.Photos.Contains(p));
        if (i >= 0) GridList.ScrollIntoView(_rows[i]);
    }

    static ScrollViewer? Scroller(DependencyObject d)
    {
        if (d is ScrollViewer s) return s;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Scroller(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }

    void Select(MonthPhoto? p)
    {
        if (_selected is not null) _selected.IsSelected = false;
        _selected = p;
        if (p is not null) p.IsSelected = true;
    }

    void Tile_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MonthPhoto p } || _vm is null) return;
        Select(p);
        Focus();
        if (e.ClickCount == 2) _vm.StartTriageCommand.Execute(p);
        e.Handled = true;
    }

    static MonthPhoto? MenuPhoto(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu { PlacementTarget: FrameworkElement { DataContext: MonthPhoto p } } ? p : null;

    void MarkMenu_Click(object sender, RoutedEventArgs e) { if (MenuPhoto(sender) is { } p) _vm?.ToggleMarkCommand.Execute(p); }
    void TriageMenu_Click(object sender, RoutedEventArgs e) { if (MenuPhoto(sender) is { } p) _vm?.StartTriageCommand.Execute(p); }
    void OpenMenu_Click(object sender, RoutedEventArgs e) { if (MenuPhoto(sender) is { } p) _vm?.OpenFileCommand.Execute(p); }

    void Thumb_Loaded(object sender, RoutedEventArgs e) => ThumbTracker.Attach((FrameworkElement)sender);

    /// <summary>⋯ menüsü: "Gezgin'de aç"; ay birden çok klasördeyse alt menüde klasörler.</summary>
    void More_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        var open = new MenuItem { Header = "Gezgin'de aç" };
        if (_vm.Folders.Count == 1)
            open.Click += (_, _) => _vm.OpenFolderCommand.Execute(_vm.Folders[0]);
        else
            foreach (var f in _vm.Folders)
            {
                var item = new MenuItem { Header = ShortPath(f), ToolTip = f };
                item.Click += (_, _) => _vm.OpenFolderCommand.Execute(f);
                open.Items.Add(item);
            }
        menu.Items.Add(open);
        menu.IsOpen = true;
    }

    string ShortPath(string f)
    {
        var rel = System.IO.Path.GetRelativePath(_vm!.Root, f);
        return rel.StartsWith("..", StringComparison.Ordinal) ? f : rel;
    }

    // ── Tuşlar ───────────────────────────────────────────────────

    /// <summary>
    /// Odak ay görünümünün dışındaysa (ör. üst çubuktaki Ana ekran'a basılıp "Hayır" denince) tuşlar yine buraya gelsin:
    /// yoksa Ayıkla'da ← → Delete Esc çalışmaz.
    /// </summary>
    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible || IsKeyboardFocusWithin || e.Handled || Keyboard.FocusedElement is TextBox) return;
        if (e.Key is Key.Left or Key.Right or Key.Delete or Key.Escape) Focus();
        Root_PreviewKeyDown(this, e);
    }

    void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null || _vm.IsDiscarding) return;
        if (_vm.IsReview)
        {
            if (e.Key == Key.Escape) { _vm.CancelReviewCommand.Execute(null); e.Handled = true; }
            return;
        }
        if (_vm.IsTriage)
        {
            switch (e.Key)
            {
                case Key.Left: _vm.PreviousCommand.Execute(null); break;
                case Key.Right: _vm.NextCommand.Execute(null); break;
                case Key.Delete: _vm.MarkAndNextCommand.Execute(null); break;
                case Key.Escape: _vm.CloseTriageCommand.Execute(null); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete && _selected is not null) { _vm.ToggleMarkCommand.Execute(_selected); e.Handled = true; }
        else if (e.Key == Key.Enter && _selected is not null) { _vm.StartTriageCommand.Execute(_selected); e.Handled = true; }
        else if (e.Key == Key.Escape) { _vm.BackCommand.Execute(null); e.Handled = true; }
    }

    // ── Ayıkla: büyük resim ve sürükleme ─────────────────────────

    int StageWidth()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return (int)(Math.Max(Stage.ActualWidth, 800) * dpi);
    }

    async void ShowCurrent()
    {
        if (_vm is not { IsTriage: true, Current: { } c }) return;
        var shown = c;
        Big.Source = c.Thumb; // önce küçük resim: anında bir şey görünsün
        BigMissing.Visibility = Visibility.Collapsed;
        int w = StageWidth();
        var img = await _large.Get(c.File, w);
        if (!ReferenceEquals(_vm?.Current, shown)) return;
        Big.Source = img ?? c.Thumb;
        BigMissing.Visibility = img is null && c.Thumb is null && !c.IsVideo ? Visibility.Visible : Visibility.Collapsed;
        if (_vm.Next1 is { } next) _ = _large.Get(next.File, w); // sıradakini önceden çöz
    }

    Point? _dragStart;

    void Stage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.Current is null) return;
        Focus();
        _dragStart = e.GetPosition(Stage);
        Stage.CaptureMouse();
        e.Handled = true;
    }

    void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        double dx = e.GetPosition(Stage).X - start.X;
        CardMove.X = dx;
        CardRotate.Angle = dx / Math.Max(Stage.ActualWidth, 1) * 8;
        double t = Math.Min(1, Math.Abs(dx) / Math.Max(Stage.ActualWidth * DragThreshold, 1));
        HintLeft.Opacity = dx < 0 ? t : 0;
        HintRight.Opacity = dx > 0 ? t : 0;
    }

    void Stage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start || _vm is null) return;
        double dx = e.GetPosition(Stage).X - start.X;
        _dragStart = null;
        Stage.ReleaseMouseCapture();
        if (Math.Abs(dx) >= Stage.ActualWidth * DragThreshold)
        {
            ResetCard();
            if (dx < 0) _vm.DiscardAndNextCommand.Execute(null);
            else _vm.KeepAndNextCommand.Execute(null);
        }
        else SnapBack();
    }

    void Stage_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;
        _dragStart = null;
        SnapBack();
    }

    void SnapBack()
    {
        var d = new Duration(TimeSpan.FromMilliseconds(160));
        CardMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, d) { FillBehavior = FillBehavior.Stop });
        CardRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, d) { FillBehavior = FillBehavior.Stop });
        CardMove.X = 0; CardRotate.Angle = 0;
        HintLeft.Opacity = 0; HintRight.Opacity = 0;
    }

    void ResetCard()
    {
        CardMove.BeginAnimation(TranslateTransform.XProperty, null);
        CardRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        CardMove.X = 0; CardRotate.Angle = 0;
        HintLeft.Opacity = 0; HintRight.Opacity = 0;
    }
}
