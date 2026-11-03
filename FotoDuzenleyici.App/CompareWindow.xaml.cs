using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.App;

/// <summary>
/// Bir benzer grubunun karelerini tam ekran yan yana gösterir. Yakınlaştırma eşzamanlıdır: bütün kareler aynı göreli
/// noktaya aynı oranda büyür (netlik kıyası). Kararlar doğrudan grubun satırlarına yazılır; ana pencere aynı nesneleri
/// gösterdiği için şerit de güncellenir.
/// </summary>
public partial class CompareWindow : Window
{
    const int ScreenDecodeWidth = 1200;
    const double MinScale = 1, MaxScale = 8, WheelStep = 1.25;

    readonly IReadOnlyList<SimilarGroupState> _groups;
    readonly ReviewStore _store;
    readonly Action _onDecisionsChanged;
    readonly DispatcherTimer _warningTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    // Bütün karelerin paylaştığı dönüşüm: biri yakınlaşınca hepsi yakınlaşır.
    readonly ScaleTransform _scale = new(1, 1);
    readonly TranslateTransform _pan = new();
    readonly TransformGroup _transform = new();
    readonly List<Image> _images = new();
    // Tam çözünürlük: aynı anda en fazla 2; gruptan çıkılınca bekleyenler iptal edilir.
    readonly DecodeLimiter _fullDecodes = new(2);
    CancellationTokenSource _groupCts = new();

    UniformGrid? _grid;
    List<CompareTile> _tiles = new();
    int _index, _version;
    CompareTile? _hover, _lastClicked;
    Point? _dragStart;
    Vector _panAtDragStart;
    bool _dragged;

    public SimilarGroupState Current => _groups[_index];
    /// <summary>Son grupta Enter'a basılıp pencere kapandı.</summary>
    public bool FinishedAll { get; private set; }

    public CompareWindow(IReadOnlyList<SimilarGroupState> groups, int startIndex, ReviewStore store, Action onDecisionsChanged)
    {
        InitializeComponent();
        _groups = groups; _store = store; _onDecisionsChanged = onDecisionsChanged;
        _transform.Children.Add(_scale);
        _transform.Children.Add(_pan);
        _warningTimer.Tick += (_, _) => { Warning.Text = ""; _warningTimer.Stop(); };
        Closed += (_, _) => _groupCts.Cancel();
        Show(Math.Clamp(startIndex, 0, groups.Count - 1));
    }

    void Show(int index)
    {
        _index = index;
        var version = ++_version;
        _groupCts.Cancel();
        _groupCts = new CancellationTokenSource();
        ResetZoom();
        _images.Clear();
        _hover = _lastClicked = null;
        _tiles = Current.Rows.Select((r, i) => new CompareTile { Row = r, Number = i + 1 }).ToList();
        if (_grid is not null) _grid.Columns = Math.Min(4, _tiles.Count);
        Tiles.ItemsSource = _tiles;
        UpdateHeader();
        foreach (var t in _tiles) _ = LoadScreenAsync(t, version);
    }

    async Task LoadScreenAsync(CompareTile t, int version)
    {
        var f = t.Row.File;
        var img = await Task.Run(() => ImageHasher.Decode(f.Path, f.Orientation, ScreenDecodeWidth));
        if (version == _version) t.Offer(img, ScreenDecodeWidth); // başka gruba geçildiyse yok say
    }

    async Task LoadFullAsync(CompareTile t, int version, CancellationToken ct)
    {
        var f = t.Row.File;
        int width = CompareTile.FullDecodeWidth(_tiles.Count, f.Width);
        var img = await _fullDecodes.RunAsync(() => ImageHasher.Decode(f.Path, f.Orientation, width), ct);
        if (version == _version && img is not null) t.Offer(img, width == 0 ? CompareTile.FullWidth : width);
    }

    void UpdateHeader() =>
        Header.Text = $"Grup {Current.Number} / {_groups.Count} · {Current.KindText} · {Current.Rows.Count} kare" +
                      (Current.Reviewed ? " · İncelendi" : "");

    void Warn(string text) { Warning.Text = text; _warningTimer.Stop(); _warningTimer.Start(); }

    void TileGrid_Loaded(object s, RoutedEventArgs e)
    {
        _grid = (UniformGrid)s;
        _grid.Columns = Math.Min(4, _tiles.Count);
    }

    void TileImage_Loaded(object s, RoutedEventArgs e)
    {
        var img = (Image)s;
        img.RenderTransform = _transform;
        img.RenderTransformOrigin = _images.FirstOrDefault()?.RenderTransformOrigin ?? new Point(0.5, 0.5);
        _images.Add(img);
    }

    static CompareTile? TileOf(object sender) => (sender as FrameworkElement)?.DataContext as CompareTile;

    void Toggle(CompareTile t)
    {
        if (t.Row.Keep && !Current.CanRelease(t.Row)) { Warn("Her grupta en az bir kare kalmalı."); return; }
        t.Row.Keep = !t.Row.Keep;
        _lastClicked = t;
    }

    void Tile_MouseEnter(object s, MouseEventArgs e) => _hover = TileOf(s);

    void Tile_MouseDown(object s, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _panAtDragStart = new Vector(_pan.X, _pan.Y);
        _dragged = false;
        ((UIElement)s).CaptureMouse();
    }

    void Tile_MouseMove(object s, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || _scale.ScaleX <= 1) return;
        var d = e.GetPosition(this) - start;
        if (!_dragged && d.Length < 4) return;
        _dragged = true;
        _pan.X = _panAtDragStart.X + d.X;
        _pan.Y = _panAtDragStart.Y + d.Y;
    }

    void Tile_MouseUp(object s, MouseButtonEventArgs e)
    {
        ((UIElement)s).ReleaseMouseCapture();
        // Basması bu pencerede olmayan bırakma (ör. şeritteki çift tıklamanın ikinci bırakması) yok sayılır.
        if (_dragStart is null) return;
        bool wasDrag = _dragged;
        _dragStart = null; _dragged = false;
        if (!wasDrag && TileOf(s) is { } t) Toggle(t);
    }

    void Tile_MouseWheel(object s, MouseWheelEventArgs e)
    {
        if (s is not Border b || FindImage(b) is not { } img || img.ActualWidth <= 0) return;
        var p = e.GetPosition(img);
        ZoomAt(new Point(p.X / img.ActualWidth, p.Y / img.ActualHeight), e.Delta > 0 ? _scale.ScaleX * WheelStep : _scale.ScaleX / WheelStep);
        e.Handled = true;
    }

    static Image? FindImage(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Image img) return img;
            if (FindImage(c) is { } found) return found;
        }
        return null;
    }

    /// <summary>Bütün kareleri aynı göreli noktaya göre ölçekler; ilk yakınlaşmada tam çözünürlük yüklenir.</summary>
    void ZoomAt(Point relative, double scale)
    {
        scale = Math.Clamp(scale, MinScale, MaxScale);
        if (_scale.ScaleX <= 1 && scale > 1)
            foreach (var img in _images) img.RenderTransformOrigin = relative;
        _scale.ScaleX = _scale.ScaleY = scale;
        if (scale <= 1) { _pan.X = _pan.Y = 0; return; }

        var version = _version;
        foreach (var t in _tiles.Where(t => !t.FullRequested))
        {
            t.FullRequested = true;
            _ = LoadFullAsync(t, version, _groupCts.Token);
        }
    }

    void ResetZoom()
    {
        _scale.ScaleX = _scale.ScaleY = 1;
        _pan.X = _pan.Y = 0;
    }

    void Next()
    {
        if (_index + 1 < _groups.Count) Show(_index + 1);
    }

    void Window_PreviewKeyDown(object s, KeyEventArgs e)
    {
        int digit = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (digit > 0)
        {
            if (digit <= _tiles.Count) Toggle(_tiles[digit - 1]);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                if ((_hover ?? _lastClicked) is { } t) { Current.KeepOnly(t.Row); _lastClicked = t; }
                break;
            case Key.Enter:
                Current.SetReviewed(_store, true);
                _onDecisionsChanged();
                if (_index + 1 < _groups.Count) Next();
                else { FinishedAll = true; Close(); }
                break;
            case Key.Right: Next(); break;
            case Key.Left: if (_index > 0) Show(_index - 1); break;
            case Key.Z:
                if (_hover is not null && Mouse.DirectlyOver is DependencyObject over && FindAncestorImage(over) is { } img && img.ActualWidth > 0)
                {
                    var p = Mouse.GetPosition(img);
                    ZoomAt(new Point(p.X / img.ActualWidth, p.Y / img.ActualHeight), _scale.ScaleX * 2);
                }
                else ZoomAt(new Point(0.5, 0.5), _scale.ScaleX * 2);
                break;
            case Key.D0: case Key.NumPad0: ResetZoom(); break;
            case Key.Escape: Close(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Farenin altındaki öğeden yukarı çıkıp kutunun içindeki görüntüyü bulur.</summary>
    static Image? FindAncestorImage(DependencyObject d)
    {
        for (var x = d; x is not null; x = VisualTreeHelper.GetParent(x))
            if (x is Border b && b.DataContext is CompareTile) return FindImage(b);
        return null;
    }
}
