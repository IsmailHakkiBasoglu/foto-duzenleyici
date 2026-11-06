using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using FotoDuzenleyici.Core;
using Microsoft.Win32;

namespace FotoDuzenleyici.App;

public sealed class FolderNode : INotifyPropertyChanged
{
    bool? _checked = true;
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public FolderNode? Parent { get; init; }
    public ObservableCollection<FolderNode> Children { get; } = new();

    public bool? IsChecked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Raise(nameof(IsChecked));
            foreach (var c in Children) c.SetFromParent(value);
        }
    }
    void SetFromParent(bool? v) { _checked = v; Raise(nameof(IsChecked)); foreach (var c in Children) c.SetFromParent(v); }

    public void CollectExcluded(HashSet<string> ex)
    {
        if (_checked == false) { ex.Add(FullPath); return; }
        foreach (var c in Children) c.CollectExcluded(ex);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed record PlanRow(string Action, string Source, string Target, string Date, string DateSource, string Camera, PlanItem Item);
public sealed record DupRow(string Keeper, string Copy, string SizeMb);

public partial class MainWindow : Window
{
    static readonly Regex SoftwareLike = new("getintopc|autocad|setup|install|program files|\\bsdk\\b", RegexOptions.IgnoreCase);

    List<MediaFile> _files = new();
    List<DuplicateGroup> _dups = new();
    List<SimilarGroupState> _groups = new();
    IEnumerable<SimilarRow> AllSimilarRows => _groups.SelectMany(g => g.Rows);
    List<string> _simErrors = new();
    List<string> _qualityErrors = new();
    /// <summary>Netlik / karanlık ölçümleri; ölçüm durdurulduysa boş ve <see cref="_qualityDone"/> false.</summary>
    Dictionary<MediaFile, QualityScore> _scores = new();
    bool _qualityDone;
    /// <summary>Kusurlu sekmesi: tek kareli gruplar (bkz. <see cref="FlawedList"/>).</summary>
    List<SimilarGroupState> _flawed = new();
    Plan? _plan;
    /// <summary>Plan önizlemesi için RAW+JPG eşleri; plan kurulurken doldurulur.</summary>
    Dictionary<MediaFile, List<MediaFile>> _planPairs = new();
    // Hızlı gezinmede yalnızca son seçilen satırın görüntüsü gösterilir.
    int _previewSeq;
    FolderNode? _rootNode;
    CancellationTokenSource? _cts;
    readonly ReviewStore _reviews = new(ReviewStore.DefaultPath);
    readonly ThumbnailLoader _thumbs;
    // Art arda tıklamalar tek yazma: son değişiklikten 1 sn sonra kaydedilir.
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        _thumbs = new ThumbnailLoader(ThumbnailLoader.DefaultDecode); // arayüz iş parçacığında: sonuçlar buraya gelir
        _saveTimer.Tick += (_, _) => SaveReviews();
        Closing += (_, _) => { if (_saveTimer.IsEnabled) SaveReviews(); };
        if (_reviews.LoadFailed) StatusText.Text = "Önceki benzer kararları okunamadı; boş başlandı.";
    }

    void SaveReviews()
    {
        _saveTimer.Stop();
        try { _reviews.Save(); }
        catch (Exception) { StatusText.Text = "Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli."; }
    }

    void ReviewsChanged() { _saveTimer.Stop(); _saveTimer.Start(); UpdateReviewCounter(); }

    void SetBusy(bool busy)
    {
        ScanBtn.IsEnabled = !busy;
        StopBtn.IsEnabled = busy;
        PlanBtn.IsEnabled = !busy && _files.Count > 0;
        ApplyBtn.IsEnabled = !busy && _plan != null;
        UndoBtn.IsEnabled = !busy;
    }

    async void PickRoot_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Taranacak ana klasörü seç" };
        if (dlg.ShowDialog() != true) return;
        RootBox.Text = dlg.FolderName;
        if (string.IsNullOrWhiteSpace(DestBox.Text)) DestBox.Text = dlg.FolderName + "_DUZENLI";
        BuildTree(dlg.FolderName);
        await OfferResumeAsync(DestBox.Text.Trim());
    }

    async void PickDest_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Düzenli yapının oluşturulacağı hedef klasör" };
        if (dlg.ShowDialog() != true) return;
        DestBox.Text = dlg.FolderName;
        await OfferResumeAsync(dlg.FolderName);
    }

    void BuildTree(string root)
    {
        _rootNode = MakeNode(root, null, 0);
        Tree.ItemsSource = new[] { _rootNode };
        StatusText.Text = "Klasör ağacı hazır. Dahil etmek istemediklerinin işaretini kaldır, sonra Tara'ya bas.";
    }

    static FolderNode MakeNode(string path, FolderNode? parent, int depth)
    {
        var node = new FolderNode { Name = Path.GetFileName(path) is { Length: > 0 } n ? n : path, FullPath = path, Parent = parent };
        if (depth > 0 && SoftwareLike.IsMatch(node.Name)) node.IsChecked = false; // yazılım kurulumu gibi görünenleri baştan hariç tut
        try
        {
            foreach (var d in Directory.GetDirectories(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                node.Children.Add(MakeNode(d, node, depth + 1));
        }
        catch { }
        return node;
    }

    ScanOptions BuildScanOptions()
    {
        var ex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _rootNode?.CollectExcluded(ex);
        var kinds = new HashSet<FileKind> { FileKind.Photo, FileKind.Raw, FileKind.Video };
        if (OptPdf.IsChecked == true) kinds.Add(FileKind.Pdf);
        return new ScanOptions { Roots = { RootBox.Text }, ExcludedFolders = ex, IncludedKinds = kinds };
    }

    async void Scan_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RootBox.Text) || !Directory.Exists(RootBox.Text))
        { MessageBox.Show("Önce geçerli bir ana klasör seç."); return; }

        _plan = null; PlanGrid.ItemsSource = null; DupGrid.ItemsSource = null; SimilarList.ItemsSource = null;
        _groups = new(); _simErrors = new(); _qualityErrors = new(); _thumbs.Clear(); _files = new(); _dups = new();
        _scores = new(); _qualityDone = false; _flawed = new(); FlawedListBox.ItemsSource = null; FlawedCounter.Text = "";
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        SetBusy(true);
        var opts = BuildScanOptions();
        var dest = DestBox.Text.Trim();
        // Önceki turlarda ayrılanlar (_Benzerler, _Kusurlu) ne benzerlik aramasına ne ölçüme girer.
        var ignoreDirs = dest.Length == 0 ? Array.Empty<string>()
            : new[] { Path.Combine(Path.GetFullPath(dest), "_Benzerler"), Path.Combine(Path.GetFullPath(dest), "_Kusurlu") };
        var prog = new Progress<(int d, int t)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"Metadata okunuyor… {p.d}/{p.t}"; });
        try
        {
            var files = await Task.Run(() => new Scanner(opts).Scan(prog, ct));
            StatusText.Text = "Aynı içerikli dosyalar aranıyor (SHA-256)…";
            var hprog = new Progress<(int d, int t)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"Hash hesaplanıyor… {p.d}/{p.t}"; });
            var dups = await Task.Run(() => DuplicateFinder.Find(files, 4, hprog, ct));
            (_files, _dups) = (files, dups);
            ShowDuplicates();

            var hashes = new Dictionary<MediaFile, ImagePrint>();
            string? note = null;
            if (OptSimilar.IsChecked == true)
            {
                var sprog = new Progress<(int d, int t, int c)>(p =>
                {
                    Progress.Maximum = p.t; Progress.Value = p.d;
                    StatusText.Text = $"Benzerlik izi… {p.d}/{p.t} ({p.c}'i önbellekten)";
                });
                try
                {
                    hashes = await Task.Run(() => SimilarityScan.Compute(_files, _dups, ignoreDirs,
                        new HashCache(HashCache.DefaultPath), _simErrors, sprog, ct));
                }
                catch (OperationCanceledException)
                {
                    hashes = new();
                    note = "Benzerlik araması durduruldu; hesaplanan izler kaydedildi, bir sonraki taramada kaldığı yerden sürer.";
                }
            }
            // Netlik / karanlık: durdurulursa yarım sonuçla liste gösterilmez, ölçülenler önbellekte kalır.
            if (OptQuality.IsChecked == true && !ct.IsCancellationRequested)
            {
                var qprog = new Progress<(int d, int t, int c)>(p =>
                {
                    Progress.Maximum = p.t; Progress.Value = p.d;
                    StatusText.Text = $"Netlik ölçülüyor… {p.d}/{p.t} ({p.c}'i önbellekten)";
                });
                try
                {
                    _scores = await Task.Run(() => QualityScan.Compute(_files, _dups, ignoreDirs,
                        new QualityCache(QualityCache.DefaultPath), _qualityErrors, qprog, ct));
                    _qualityDone = true;
                }
                catch (OperationCanceledException)
                {
                    note = "Netlik ölçümü durduruldu; ölçülenler kaydedildi, bir sonraki taramada kaldığı yerden sürer.";
                }
            }
            await ShowSimilar(hashes, ignoreDirs);
            await ShowFlawed();
            SummaryBox.Text = BuildSummary(null);
            StatusText.Text = note ?? $"Tarama bitti: {_files.Count} dosya, {_dups.Count} yinelenen grup, " +
                $"{_groups.Count} benzer grup. Şimdi plan oluşturabilirsin.";
        }
        catch (OperationCanceledException) { _files = new(); _dups = new(); StatusText.Text = "Tarama durduruldu."; }
        catch (Exception ex) { StatusText.Text = "Hata: " + ex.Message; }
        SetBusy(false);
    }

    void Stop_Click(object s, RoutedEventArgs e) => _cts?.Cancel();

    void ShowDuplicates() =>
        DupGrid.ItemsSource = _dups.SelectMany(g => g.Copies.Select(c =>
            new DupRow(g.Keeper.Path, c.Path, (c.Size / 1048576.0).ToString("N1")))).ToList();

    async Task ShowSimilar(Dictionary<MediaFile, ImagePrint> hashes, IReadOnlyList<string> ignoreDirs)
    {
        var groups = SimilarFinder.Find(_files, hashes, SameFrameFinder.Find(_files, _dups, ignoreDirs));
        var states = SimilarGroupState.FromGroups(groups);

        // Önceki kararlar: dosya kimliği yoldan bağımsız; okunamayan dosyanın kararı saklanmaz.
        StatusText.Text = "Önceki kararlar yükleniyor…";
        var rows = states.SelectMany(g => g.Rows).ToList();
        var pairs = RawJpgPairs.Index(_files);
        foreach (var r in rows) r.HasRaw = r.File.Kind == FileKind.Photo && pairs.ContainsKey(r.File);
        await Task.Run(() =>
        {
            foreach (var r in rows)
                try { r.StoreKey = HashCache.Key(r.Path); } catch (Exception) { r.StoreKey = null; }
        });
        foreach (var g in states)
        {
            g.ApplyBurstRanks(_scores);
            g.Restore(_reviews);
            g.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SimilarGroupState.Reviewed)) ReviewsChanged(); };
            foreach (var r in g.Rows)
            {
                r.CanRelease = row =>
                {
                    bool ok = g.CanRelease(row);
                    if (!ok) StatusText.Text = "Her grupta en az bir dosya kalmalı.";
                    return ok;
                };
                r.Changed = row =>
                {
                    if (row.StoreKey is { } k) _reviews.SetKeep(k, row.Keep);
                    ReviewsChanged();
                    SimilarChanged();
                };
            }
        }
        _groups = states;
        SimilarList.ItemsSource = _groups;
        ApplyReviewFilter();
    }

    /// <summary>Kusurlu sekmesini doldurur; ölçüm yapılmadıysa ya da durdurulduysa boş kalır.</summary>
    async Task ShowFlawed()
    {
        if (!_qualityDone) { FlawedCounter.Text = "Ölçüm yapılmadı."; return; }
        var list = FlawedList.Build(_scores, _groups, _groups.Count + 1);
        var rows = list.Select(g => g.Rows[0]).ToList();
        var pairs = RawJpgPairs.Index(_files);
        foreach (var r in rows) r.HasRaw = pairs.ContainsKey(r.File);
        await Task.Run(() =>
        {
            foreach (var r in rows)
                try { r.StoreKey = HashCache.Key(r.Path); } catch (Exception) { r.StoreKey = null; }
        });
        foreach (var g in list)
        {
            g.Restore(_reviews);
            g.Rows[0].Changed = row =>
            {
                if (row.StoreKey is { } k) _reviews.SetKeep(k, row.Keep);
                ReviewsChanged();
                UpdateFlawedCounter();
                SimilarChanged();
            };
        }
        _flawed = list;
        FlawedListBox.ItemsSource = _flawed;
        ApplyFlawFilter();
    }

    void ApplyFlawFilter()
    {
        if (FlawedListBox.ItemsSource is null) return;
        bool blurry = ShowBlurry.IsChecked == true, dark = ShowDark.IsChecked == true;
        CollectionViewSource.GetDefaultView(FlawedListBox.ItemsSource).Filter = o =>
            o is SimilarGroupState g && g.Rows[0].Flaw is { } f &&
            (blurry && f.Contains("BULANIK") || dark && f.Contains("KARANLIK"));
        UpdateFlawedCounter();
    }

    void FlawFilter_Click(object s, RoutedEventArgs e) => ApplyFlawFilter();

    void UpdateFlawedCounter()
    {
        var rows = _flawed.Select(g => g.Rows[0]).ToList();
        FlawedCounter.Text = rows.Count == 0 ? "Kusurlu kare yok."
            : $"Bulanık: {rows.Count(r => r.Flaw!.Contains("BULANIK"))}  ·  Karanlık: {rows.Count(r => r.Flaw!.Contains("KARANLIK"))}" +
              $"  ·  Ayıklanacak: {rows.Count(r => !r.Keep)} / {rows.Count}";
    }

    void UpdateReviewCounter() =>
        ReviewCounter.Text = _groups.Count == 0 ? "" : $"İncelenen: {_groups.Count(g => g.Reviewed)} / {_groups.Count}";

    void ApplyReviewFilter()
    {
        if (SimilarList.ItemsSource is null) return;
        bool only = OnlyUnreviewed.IsChecked == true;
        CollectionViewSource.GetDefaultView(SimilarList.ItemsSource).Filter =
            only ? o => o is SimilarGroupState g && !g.Reviewed : null;
        UpdateReviewCounter();
    }

    void OnlyUnreviewed_Click(object s, RoutedEventArgs e) => ApplyReviewFilter();

    static SimilarRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as SimilarRow;
    SimilarGroupState? GroupOf(SimilarRow row) =>
        _groups.FirstOrDefault(g => g.Rows.Contains(row)) ?? _flawed.FirstOrDefault(g => g.Rows.Contains(row));

    // Kartın satırı Tag'de tutulur: kart kaldırılırken DataContext çoktan "bağlantısız" olabilir, iptal yine
    // doğru satıra gitsin. Geri dönüştürülen kartın satırı değişince eskisi iptal, yenisi istenir.
    void Card_Loaded(object s, RoutedEventArgs e)
    {
        var card = (FrameworkElement)s;
        if (card.Tag is null) card.DataContextChanged += Card_DataContextChanged;
        Track(card, RowOf(s));
    }

    void Card_Unloaded(object s, RoutedEventArgs e) => Track((FrameworkElement)s, null);

    void Card_DataContextChanged(object s, DependencyPropertyChangedEventArgs e)
    {
        var card = (FrameworkElement)s;
        if (card.IsLoaded) Track(card, e.NewValue as SimilarRow);
    }

    void Track(FrameworkElement card, SimilarRow? row)
    {
        if (card.Tag is SimilarRow old && old != row) _thumbs.Cancel(old);
        card.Tag = row ?? (object)false; // false: olay bağlandı ama satır yok
        if (row is not null) _thumbs.Request(row);
    }

    (SimilarRow Row, bool Before)? _lastToggle;

    // Çift tıklama yalnız karşılaştırmayı açar: ilk tıklamanın değiştirdiği işaret geri alınır.
    void Card_MouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || RowOf(s) is not { } r || GroupOf(r) is not { } g) return;
        if (_lastToggle is { } t && t.Row == r) r.Keep = t.Before;
        _lastToggle = null;
        e.Handled = true;
        OpenCompare(g);
    }

    void Card_MouseUp(object s, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || RowOf(s) is not { } r) return;
        _lastToggle = (r, r.Keep);
        r.Keep = !r.Keep;
    }

    void SimilarList_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SimilarList.SelectedItem is SimilarGroupState g) { OpenCompare(g); e.Handled = true; }
    }

    /// <summary>Süzgeçten geçen (görünen) gruplar arasında gezinen karşılaştırma penceresi.</summary>
    void OpenCompare(SimilarGroupState g)
    {
        // Kusurlu kare: tek kare tam ekran; ← → süzgeçten geçen sonraki/önceki kusurlu kare.
        bool flawed = g.Kind == SimilarKind.Flawed;
        var box = flawed ? FlawedListBox : SimilarList;
        var all = flawed ? _flawed : _groups;
        var visible = CollectionViewSource.GetDefaultView(box.ItemsSource).Cast<SimilarGroupState>().ToList();
        int start = visible.IndexOf(g);
        if (start < 0) { visible = all; start = all.IndexOf(g); }
        var w = new CompareWindow(visible, start, _reviews, ReviewsChanged) { Owner = this };
        w.ShowDialog();
        // Süzgeç açıksa incelenen gruplar listeden düşsün.
        if (!flawed) ApplyReviewFilter();
        box.SelectedItem = w.Current;
        box.ScrollIntoView(w.Current);
        if (w.FinishedAll) StatusText.Text = flawed ? "Bütün kusurlu kareler gözden geçirildi." : "Bütün gruplar gözden geçirildi.";
    }

    /// <summary>İşaretler değişince eski plan geçersiz: yeniden oluşturulmadan uygulanamaz.</summary>
    void SimilarChanged()
    {
        if (_plan is null) return;
        _plan = null; ApplyBtn.IsEnabled = false;
        StatusText.Text = "Seçim değişti; Uygula'dan önce planı yeniden oluştur.";
    }

    /// <summary>Kalsın işareti kaldırılan dosya → grubunda kalan dosya (öneri işaretliyse o).</summary>
    Dictionary<MediaFile, MediaFile> BuildSimilarMap()
    {
        var map = new Dictionary<MediaFile, MediaFile>();
        foreach (var g in _groups)
        {
            var keeper = g.Rows.FirstOrDefault(r => r.Keep && r.Suggested) ?? g.Rows.First(r => r.Keep);
            foreach (var r in g.Rows.Where(r => !r.Keep)) map[r.File] = keeper.File;
        }
        return map;
    }

    void SuggestSimilar_Click(object s, RoutedEventArgs e)
    {
        // Önerisi olmayan gruplara (seri çekim) dokunulmaz.
        foreach (var g in _groups)
            if (g.Rows.FirstOrDefault(r => r.Suggested) is { } star) g.KeepOnly(star);
    }

    void ReleaseSoft_Click(object s, RoutedEventArgs e)
    {
        int n = _groups.Sum(g => g.ReleaseSoft());
        StatusText.Text = n == 0 ? "Ayıklanacak bulanık kare yok." : $"{n} bulanık kare ayıklandı.";
    }

    void KeepAllSimilar_Click(object s, RoutedEventArgs e)
    {
        foreach (var r in AllSimilarRows) r.Keep = true;
    }

    PlanOptions BuildPlanOptions() => new()
    {
        DestinationRoot = DestBox.Text.Trim(),
        SourceFolder = OptSourceFolder.IsChecked == true ? SourceFolderMode.SubFolder : SourceFolderMode.None,
        RenameWithDate = OptRenameDate.IsChecked == true,
        AppendSourceToName = OptAppendSource.IsChecked == true,
        SeparateRawFolder = OptRawSeparate.IsChecked == true,
    };

    async void Plan_Click(object s, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DestBox.Text)) { MessageBox.Show("Hedef klasörü seç."); return; }
        var dest = Path.GetFullPath(DestBox.Text.Trim());
        if (await OfferResumeAsync(dest)) { StatusText.Text += "  Dosyalar değişti; plan için yeniden Tara."; return; }
        var similarOf = BuildSimilarMap();
        var flawedSet = _flawed.SelectMany(g => g.Rows).Where(r => !r.Keep).Select(r => r.File).ToHashSet();
        // Ayıklanan karelerin (Benzerler + Kusurlu) RAW eşleri: her Plan'da tek soruda sorulur (kullanıcı kararı).
        var released = similarOf.Keys.Concat(flawedSet).ToHashSet();
        var raws = RawJpgPairs.UnreleasedRaws(_files, released);
        if (raws.Count > 0)
        {
            var ans = MessageBox.Show(
                $"Benzerler'de ve Kusurlu'da ayıkladığın {raws.Count} karenin RAW eşi var.\n\n" +
                "Evet: RAW'lar da JPG'leriyle birlikte taşınsın (_Benzerler / _Kusurlu).\n" +
                "Hayır: RAW'lar normal klasörlerine gitsin, yalnız JPG'ler ayıklansın.\n" +
                "İptal: plan oluşturulmasın.",
                "RAW eşleri", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ans == MessageBoxResult.Cancel) return;
            if (ans == MessageBoxResult.Yes)
                foreach (var (raw, photo) in raws)
                    if (similarOf.TryGetValue(photo, out var keeper)) similarOf[raw] = keeper;
                    else flawedSet.Add(raw);
        }
        _plan = Planner.Build(_files, _dups, BuildPlanOptions(), similarOf, flawedSet);
        _planPairs = RawJpgPairs.Index(_files);
        PlanGrid.ItemsSource = _plan.Items.Select(i => new PlanRow(
            PlanPreview.ActionName(i.Action), i.File.Path, i.TargetPath,
            i.File.Taken?.ToString("yyyy-MM-dd HH:mm") ?? "-", i.File.DateSource.ToString(), i.File.Source ?? "", i)).ToList();
        SummaryBox.Text = BuildSummary(dest);
        ApplyBtn.IsEnabled = true; CsvBtn.IsEnabled = true;
        StatusText.Text = "Plan hazır. Hiçbir dosya henüz değiştirilmedi. Özet ve Plan sekmelerine bak, sonra Uygula.";
    }

    const int PreviewDecodeWidth = 640;

    async void PlanGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var seq = ++_previewSeq;
        PreviewImage.Source = null;
        if (PlanGrid.SelectedItem is not PlanRow row)
        {
            PreviewNote.Text = "";
            PreviewInfo.Text = "Önizleme için tablodan bir satır seç.";
            return;
        }
        var file = row.Item.File;
        MediaFile? pair = _planPairs.TryGetValue(file, out var g)
            ? g.FirstOrDefault(m => m != file && m.Kind != file.Kind) : null;
        PreviewInfo.Text = PlanPreview.Describe(row.Item, _scores.TryGetValue(file, out var s) ? s : null, pair);
        PreviewNote.Text = file.Kind is FileKind.Photo or FileKind.Raw ? "Yükleniyor…" : "";

        try
        {
            var (img, note) = await Task.Run(() =>
                PlanPreview.Load(file, pair, f => ImageHasher.Decode(f.Path, f.Orientation, PreviewDecodeWidth)));
            if (seq != _previewSeq) return; // bu arada başka satır seçildi
            PreviewImage.Source = img;
            PreviewNote.Text = img is null ? note ?? "" : "";
            if (img is not null && note is not null) PreviewInfo.Text += "\n" + note;
        }
        catch (Exception ex)
        {
            if (seq == _previewSeq) PreviewNote.Text = "Görüntü açılamadı: " + ex.Message;
        }
    }

    /// <summary>Dosyayı Windows'un varsayılan uygulamasında açar; dosyaya dokunmaz.</summary>
    void PlanGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlanGrid.SelectedItem is not PlanRow row) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.Item.File.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Dosya açılamadı: " + ex.Message); }
    }

    string BuildSummary(string? dest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Toplam dosya: {_files.Count}   ({_files.Sum(f => f.Size) / 1e9:N2} GB)");
        foreach (var g in _files.GroupBy(f => f.Kind)) sb.AppendLine($"  {g.Key,-10} {g.Count(),6}   {g.Sum(f => f.Size) / 1e9,8:N2} GB");
        var pairCount = RawJpgPairs.Groups(_files).Count;
        if (pairCount > 0) sb.AppendLine($"RAW+JPG eşi: {pairCount} (adları ve klasör ayları eş tutulur)");
        sb.AppendLine("\nTarih kaynağı:");
        foreach (var g in _files.GroupBy(f => f.DateSource)) sb.AppendLine($"  {g.Key,-14} {g.Count(),6}");
        sb.AppendLine("\nKaynak (kamera):");
        foreach (var g in _files.GroupBy(f => f.Source ?? "(bilinmiyor)")) sb.AppendLine($"  {g.Key,-14} {g.Count(),6}");
        sb.AppendLine($"\nYinelenen: {_dups.Sum(g => g.Copies.Count)} dosya, kazanılacak {_dups.Sum(g => g.Copies.Sum(c => c.Size)) / 1e9:N2} GB");
        var byKind = _groups.GroupBy(g => g.KindText).Select(g => $"{g.Key}: {g.Count()}");
        sb.AppendLine($"Benzerler: {string.Join(", ", byKind.DefaultIfEmpty("yok"))} grup; " +
                      $"_Benzerler'e ayrılacak: {AllSimilarRows.Count(r => !r.Keep)}");
        if (_qualityDone)
        {
            var fr = _flawed.Select(g => g.Rows[0]).ToList();
            sb.AppendLine($"Kusurlu (Benzerler dışında): bulanık {fr.Count(r => r.Flaw!.Contains("BULANIK"))}, " +
                          $"karanlık {fr.Count(r => r.Flaw!.Contains("KARANLIK"))}; _Kusurlu'ya ayrılacak: {fr.Count(r => !r.Keep)}");
            sb.AppendLine($"Seri çekimde en net önerisi: {_groups.Count(g => g.Rows.Any(r => r.Sharpest))} grup " +
                          $"(BULANIK kare: {AllSimilarRows.Count(r => r.Flaw == "BULANIK")})");
        }
        if (_simErrors.Count > 0)
        {
            sb.AppendLine($"Benzerlik izi çıkarılamayan: {_simErrors.Count}");
            foreach (var err in _simErrors.Take(3)) sb.AppendLine("  " + err);
        }
        if (_qualityErrors.Count > 0)
        {
            var (small, failed) = QualityScan.CountErrors(_qualityErrors);
            sb.AppendLine($"Netliği ölçülemeyen: {failed}   (ölçülmeyecek kadar küçük: {small})");
            foreach (var err in _qualityErrors.Where(e => !e.EndsWith(": çok küçük", StringComparison.Ordinal)).Take(3))
                sb.AppendLine("  " + err);
        }
        if (_plan != null)
        {
            sb.AppendLine($"\nPlan → {dest}");
            foreach (var g in _plan.Items.GroupBy(i => i.Action)) sb.AppendLine($"  {g.Key,-16} {g.Count(),6}");
            sb.AppendLine($"  İsim çakışması (tarih eklendi): {_plan.Collisions}");
            sb.AppendLine("\nAylara göre:");
            foreach (var g in _plan.Items.Where(i => i.Action == PlanAction.Move)
                         .GroupBy(i => Path.GetRelativePath(dest!, i.TargetDir).Split('\\')[0]).OrderBy(g => g.Key))
                sb.AppendLine($"  {g.Key,-14} {g.Count(),6}");
        }
        return sb.ToString();
    }

    async void Apply_Click(object s, RoutedEventArgs e)
    {
        if (_plan == null) return;
        var todo = _plan.Items.Where(i => i.Action != PlanAction.AlreadyInPlace).ToList();
        bool recycle = OptRecycle.IsChecked == true;
        var gb = todo.Sum(i => i.File.Size) / 1e9;
        var msg = $"{todo.Count} dosya ({gb:N1} GB) hedefe KOPYALANACAK ve her biri doğrulanacak.\n" +
                  (recycle ? "Doğrulanan her dosyanın eskisi Geri Dönüşüm Kutusu'na alınacak (silinmeyecek).\n"
                           : "Eski dosyalara dokunulmayacak.\n") +
                  "Her adım günlüğe yazılır ve Geri Al ile geri dönülebilir.\n\nDevam edilsin mi?";
        if (MessageBox.Show(msg, "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var dest = Path.GetFullPath(DestBox.Text.Trim());
        var journal = Path.Combine(dest, "_Gunluk", $"islem-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        await ExecuteAsync(journal, (ex, prog, ct) => ex.Run(todo, recycle, prog, ct));
    }

    /// <summary>Uygula ve devam etme için ortak: ilerlemeyi gösterir, sonucu bildirir, planı geçersiz kılar.</summary>
    async Task ExecuteAsync(string journal,
        Func<Executor, IProgress<(int, int, string)>, CancellationToken, ExecuteResult> work)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        var prog = new Progress<(int d, int t, string c)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"{p.d}/{p.t}  {p.c}"; });
        try
        {
            var r = await Task.Run(() => work(new Executor(journal), prog, _cts.Token));
            StatusText.Text = $"Bitti. Başarılı: {r.Done}, Hatalı: {r.Failed}. Günlük: {journal}";
            if (r.Failed > 0)
                MessageBox.Show(string.Join("\n", r.Errors.Take(15)), $"{r.Failed} dosya işlenemedi (bu dosyalara dokunulmadı)");
            _plan = null;
        }
        catch (Exception ex) { StatusText.Text = "Hata: " + ex.Message; }
        SetBusy(false);
    }

    /// <summary>
    /// Hedefte yarıda kalmış işlem varsa sorar. Evet → kaldığı yerden sürer, Hayır → vazgeçilir (bir daha sorulmaz),
    /// İptal → şimdilik geçilir. Devam edildiyse true döner (dosyalar değişti, tarama eskidi).
    /// </summary>
    async Task<bool> OfferResumeAsync(string dest)
    {
        bool resumed = false;
        if (!Directory.Exists(dest)) return false;
        foreach (var (journal, remaining) in Executor.FindUnfinished(dest))
        {
            var ans = MessageBox.Show(
                $"Yarım kalan bir işlem var ({Path.GetFileName(journal)}): {remaining} dosya henüz işlenmedi.\n\n" +
                "Evet: kaldığı yerden devam et (yeniden tarama gerekmez)\n" +
                "Hayır: vazgeç; kalan dosyalara dokunulmaz, bir daha sorulmaz\n" +
                "İptal: şimdilik geç, sonra yine sor",
                "Yarım kalan işlem", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ans == MessageBoxResult.No)
            {
                Executor.Abandon(journal);
                StatusText.Text = $"Yarım kalan işlemden vazgeçildi ({remaining} dosyaya dokunulmadı).";
            }
            if (ans != MessageBoxResult.Yes) continue;
            await ExecuteAsync(journal, (ex, prog, ct) => ex.Resume(prog, ct));
            resumed = true;
        }
        return resumed;
    }

    async void Undo_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Geri alınacak işlem günlüğünü seç", Filter = "Günlük (*.jsonl)|*.jsonl",
            InitialDirectory = Directory.Exists(DestBox.Text) ? Path.Combine(DestBox.Text, "_Gunluk") : null };
        if (dlg.ShowDialog() != true) return;
        if (MessageBox.Show("Bu günlükteki dosyalar doğrulanarak ESKİ yerlerine kopyalanacak, sonra yeni konumdakiler Geri Dönüşüm Kutusu'na alınacak. Devam?",
                "Geri Al", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetBusy(true);
        var prog = new Progress<(int d, int t, string c)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"Geri alınıyor {p.d}/{p.t}  {p.c}"; });
        var r = await Task.Run(() => Executor.Undo(dlg.FileName, prog));
        StatusText.Text = $"Geri alma bitti. Geri dönen: {r.Done}, atlanan: {r.Skipped}, hatalı: {r.Failed}.";
        SetBusy(false);
    }

    void Csv_Click(object s, RoutedEventArgs e)
    {
        if (_plan == null) return;
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "plan.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder("Eylem;Kaynak;Hedef;Tarih;TarihKaynagi;Kamera;Not\n");
        foreach (var i in _plan.Items)
            sb.AppendLine($"{i.Action};{i.File.Path};{i.TargetPath};{i.File.Taken:yyyy-MM-dd HH:mm:ss};{i.File.DateSource};{i.File.Source};{i.Note}");
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
    }
}
