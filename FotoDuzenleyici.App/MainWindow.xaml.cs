using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
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

public sealed record PlanRow(string Action, string Source, string Target, string Date, string DateSource, string Camera);
public sealed record DupRow(string Keeper, string Copy, string SizeMb);

public partial class MainWindow : Window
{
    static readonly Regex SoftwareLike = new("getintopc|autocad|setup|install|program files|\\bsdk\\b", RegexOptions.IgnoreCase);

    List<MediaFile> _files = new();
    List<DuplicateGroup> _dups = new();
    Plan? _plan;
    FolderNode? _rootNode;
    CancellationTokenSource? _cts;

    public MainWindow() => InitializeComponent();

    void SetBusy(bool busy)
    {
        ScanBtn.IsEnabled = !busy;
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

        _plan = null; PlanGrid.ItemsSource = null; DupGrid.ItemsSource = null;
        SetBusy(true);
        var opts = BuildScanOptions();
        var prog = new Progress<(int d, int t)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"Metadata okunuyor… {p.d}/{p.t}"; });
        try
        {
            _files = await Task.Run(() => new Scanner(opts).Scan(prog));
            StatusText.Text = "Aynı içerikli dosyalar aranıyor (SHA-256)…";
            var hprog = new Progress<(int d, int t)>(p => { Progress.Maximum = p.t; Progress.Value = p.d; StatusText.Text = $"Hash hesaplanıyor… {p.d}/{p.t}"; });
            _dups = await Task.Run(() => DuplicateFinder.Find(_files, 4, hprog));
            ShowDuplicates();
            SummaryBox.Text = BuildSummary(null);
            StatusText.Text = $"Tarama bitti: {_files.Count} dosya, {_dups.Count} yinelenen grup. Şimdi plan oluşturabilirsin.";
        }
        catch (Exception ex) { StatusText.Text = "Hata: " + ex.Message; }
        SetBusy(false);
    }

    void ShowDuplicates() =>
        DupGrid.ItemsSource = _dups.SelectMany(g => g.Copies.Select(c =>
            new DupRow(g.Keeper.Path, c.Path, (c.Size / 1048576.0).ToString("N1")))).ToList();

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
        _plan = Planner.Build(_files, _dups, BuildPlanOptions());
        PlanGrid.ItemsSource = _plan.Items.Select(i => new PlanRow(
            i.Action.ToString(), i.File.Path, i.TargetPath,
            i.File.Taken?.ToString("yyyy-MM-dd HH:mm") ?? "-", i.File.DateSource.ToString(), i.File.Source ?? "")).ToList();
        SummaryBox.Text = BuildSummary(dest);
        ApplyBtn.IsEnabled = true; CsvBtn.IsEnabled = true;
        StatusText.Text = "Plan hazır. Hiçbir dosya henüz değiştirilmedi. Özet ve Plan sekmelerine bak, sonra Uygula.";
    }

    string BuildSummary(string? dest)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Toplam dosya: {_files.Count}   ({_files.Sum(f => f.Size) / 1e9:N2} GB)");
        foreach (var g in _files.GroupBy(f => f.Kind)) sb.AppendLine($"  {g.Key,-10} {g.Count(),6}   {g.Sum(f => f.Size) / 1e9,8:N2} GB");
        sb.AppendLine("\nTarih kaynağı:");
        foreach (var g in _files.GroupBy(f => f.DateSource)) sb.AppendLine($"  {g.Key,-14} {g.Count(),6}");
        sb.AppendLine("\nKaynak (kamera):");
        foreach (var g in _files.GroupBy(f => f.Source ?? "(bilinmiyor)")) sb.AppendLine($"  {g.Key,-14} {g.Count(),6}");
        sb.AppendLine($"\nYinelenen: {_dups.Sum(g => g.Copies.Count)} dosya, kazanılacak {_dups.Sum(g => g.Copies.Sum(c => c.Size)) / 1e9:N2} GB");
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
            if (ans == MessageBoxResult.No) Executor.Abandon(journal);
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
