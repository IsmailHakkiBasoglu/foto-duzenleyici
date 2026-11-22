using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Zaman çizelgesi: seçilen klasördeki dosyalar yıl × ay tablosunda. Salt okunur; gezinmeyi kilitlemez,
/// sayfadan çıkışta ve pencere kapanışında sayım sessizce durur.
/// </summary>
public sealed partial class TimelineViewModel : ObservableObject, ILongRunningPage, ILeaveGuard
{
    readonly Services _s;
    readonly AppSettings _settings;
    CancellationTokenSource? _cts;
    int _generation;
    bool _quietMethod;
    TimelineMethod? _defaultMethod;

    /// <param name="startFolder">Sağ tıkla açılmış klasör; yoksa son zaman çizelgesi klasörü, o da yoksa son arşiv.</param>
    /// <param name="catalogs">Oturum kataloğu deposu (kabuktan); null → bu sayfaya özel.</param>
    public TimelineViewModel(Services s, AppSettings settings, string? startFolder = null, CatalogStore? catalogs = null)
    {
        _s = s;
        _settings = settings;
        Catalogs = catalogs ?? new CatalogStore();
        folder = new[] { startFolder, settings.LastTimelineFolder, settings.Recent.FirstOrDefault()?.Archive }
            .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f) && Directory.Exists(f));
        if (folder is not null)
        {
            _defaultMethod = Timeline.DefaultMethod(folder);
            method = _defaultMethod.Value;
        }
    }

    public CatalogStore Catalogs { get; }

    [ObservableProperty] private string? folder;
    [ObservableProperty] private TimelineMethod method;
    [ObservableProperty] private KindFilter filter = KindFilter.Photo;
    [ObservableProperty] private bool isCounting;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int done;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int total;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterLines), nameof(CanUseCaptureDate))]
    private TimelineResult? result;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FooterLines))]
    private TimelineTable? table;
    [ObservableProperty] private bool stopped;
    /// <summary>Açık ay görünümü (dolu bir aya tıklanınca); null → tablo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonth))]
    private MonthViewModel? month;

    public bool HasMonth => Month is not null;

    /// <summary>Test kancası: sayım başlamadan, arka plan iş parçacığında; sayılan klasörle çağrılır.</summary>
    internal Action<string>? BeforeCountForTest { get; set; }

    public bool HasFolder => Folder is not null;

    public string MethodReason => _defaultMethod switch
    {
        TimelineMethod.FolderNames => "Bu klasörde \"Yıl-Ay\" klasörü bulundu, bu yüzden Klasör adlarından seçildi.",
        TimelineMethod.CaptureDate => "Bu klasörde \"Yıl-Ay\" klasörü bulunamadı, bu yüzden Çekim tarihinden seçildi.",
        _ => "",
    };

    public string ProgressText => Total > 0 ? $"{Done:N0} / {Total:N0} dosya okundu" : "Sayılıyor…";

    /// <summary>Klasör adlarından sayımda Yıl-Ay dışında dosya varsa: çekim tarihine geçme önerisi.</summary>
    public bool CanUseCaptureDate => Result is { Method: TimelineMethod.FolderNames, Outside: > 0 };

    public IReadOnlyList<string> FooterLines
    {
        get
        {
            if (Result is not { } r) return Array.Empty<string>();
            var lines = new List<string>();
            if (r.Method == TimelineMethod.FolderNames && r.Outside > 0)
                lines.Add($"{r.Outside:N0} dosya Yıl-Ay klasörleri dışında");
            foreach (var (name, n) in r.Special) lines.Add($"{name}: {n:N0} dosya");
            if (r.Method == TimelineMethod.CaptureDate)
            {
                int guessed = Table?.Rows.SelectMany(row => row.Months).Sum(c => c.NotFromMetadata) ?? 0;
                if (guessed > 0) lines.Add($"{guessed:N0} dosyanın tarihi dosya adından / dosya zamanından");
                lines.Add("Kopyalar ayıklanmaz; aynı fotoğrafın kopyaları ayrı sayılır.");
            }
            if (r.Errors.Count > 0) lines.Add($"{r.Errors.Count:N0} dosya ya da klasör okunamadı");
            return lines;
        }
    }

    /// <summary>Seçilen köke göre kısa yol; kökün kendisi ".".</summary>
    public string ShortPath(string path) => Folder is null ? path : Path.GetRelativePath(Folder, path);

    partial void OnFolderChanged(string? value)
    {
        OnPropertyChanged(nameof(HasFolder));
        _defaultMethod = value is null ? null : Timeline.DefaultMethod(value);
        OnPropertyChanged(nameof(MethodReason));
        if (value is null) return;
        _settings.LastTimelineFolder = value;
        _s.Settings.TrySave(_settings);
        _quietMethod = true;
        try { Method = _defaultMethod!.Value; }
        finally { _quietMethod = false; }
        _ = CountAsync();
    }

    partial void OnMethodChanged(TimelineMethod value)
    {
        if (!_quietMethod) _ = CountAsync();
    }

    partial void OnFilterChanged(KindFilter value) => Rebuild();

    void Rebuild()
    {
        Table = Result is null ? null : TimelineTable.Build(Result.Entries, Filter);
    }

    Task? _countTask;

    /// <summary>Ay görünümünde atma sürüyorsa o iş (pencere bekler), yoksa sayım (beklenmez).</summary>
    public Task? RunningTask => Month is { IsDiscarding: true } m ? m.DiscardTask : _countTask;

    /// <summary>Önceki sayımı iptal edip baştan sayar. Klasör yoksa hiçbir şey yapmaz.</summary>
    public Task CountAsync()
    {
        if (Folder is not { } f) return Task.CompletedTask;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        var m = Method;
        Stopped = false;
        Result = null;
        Table = null;
        CloseMonth();
        Done = 0;
        Total = 0;
        IsCounting = true;
        var task = Run(f, m, gen, _cts.Token);
        _countTask = task;
        return task;
    }

    async Task Run(string f, TimelineMethod m, int gen, CancellationToken ct)
    {
        // Bildirimler sırasız gelebilir (Progress arka plandan gönderir): sayaç geri gitmesin.
        IProgress<(int done, int total)> ui = new Progress<(int done, int total)>(v =>
        {
            if (gen != _generation || v.done < Done) return;
            Done = v.done;
            Total = v.total;
        });
        var hook = BeforeCountForTest;
        try
        {
            var r = await Task.Run(() =>
            {
                hook?.Invoke(f);
                return m == TimelineMethod.FolderNames ? Timeline.ByFolders(f, ct) : Timeline.ByCaptureDate(f, ui, ct);
            });
            if (gen != _generation || ct.IsCancellationRequested || r.Cancelled) return;
            if (m == TimelineMethod.CaptureDate) { Total = Math.Max(Total, r.Entries.Count); Done = Total; }
            Result = r;
            Rebuild();
        }
        catch (Exception ex) when (gen == _generation)
        {
            Result = new TimelineResult { Root = f, Method = m };
            Result.Errors.Add($"{f}: {ex.Message}");
            Rebuild();
        }
        finally
        {
            if (gen == _generation) IsCounting = false;
        }
    }

    [RelayCommand]
    void ChangeFolder()
    {
        var picked = _s.Folders.PickFolder("Zaman çizelgesi için klasör seç", Folder);
        if (picked is null) return;
        if (string.Equals(picked, Folder, StringComparison.OrdinalIgnoreCase)) { _ = CountAsync(); return; }
        Folder = picked;
    }

    [RelayCommand]
    void Recount() => _ = CountAsync();

    [RelayCommand]
    void Stop()
    {
        _cts?.Cancel();
        _generation++;
        IsCounting = false;
        Result = null;
        Table = null;
        Stopped = true;
    }

    [RelayCommand]
    void UseCaptureDate() => Method = TimelineMethod.CaptureDate;

    [RelayCommand]
    void OpenCell(TimelineCell? cell)
    {
        if (cell is not { IsEmpty: false } || cell.Folders.Count == 0 || Result is null || Month is not null) return;
        var m = new MonthViewModel(_s, _settings, Result.Root, Result.Method, cell.Year, cell.Month,
            cell.Folders.Select(f => f.Folder).ToList(), Filter);
        m.BackRequested += () => { if (ReferenceEquals(Month, m)) { Patch(m); CloseMonth(); } };
        Month = m;
        _ = m.LoadAsync();
    }

    void CloseMonth()
    {
        if (Month is null) return;
        Month.StopCommand.Execute(null);
        Month = null;
    }

    /// <summary>
    /// Atılan dosyaları tablodan düşer (yeniden sayım yok): ayın her atılan dosyası için aynı tür ve boyutta, aynı
    /// klasördeki (klasör adlarından: ay klasörünün altındaki) bir kayıt silinir; taşınanlar _Ayiklananlar'a eklenir.
    /// </summary>
    void Patch(MonthViewModel m)
    {
        if (Result is not { } r || m.Discarded.Count == 0) return;
        foreach (var (file, moved) in m.Discarded)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(file.Path)) ?? "";
            int i = r.Entries.FindIndex(e => e.Year == m.Year && e.Month == m.MonthNumber && e.Kind == file.Kind
                && e.Size == file.Size && InFolder(dir, e.Folder, r.Method));
            if (i >= 0) r.Entries.RemoveAt(i);
            if (moved) r.Special[CleanSession.RejectsFolder] = r.Special.GetValueOrDefault(CleanSession.RejectsFolder) + 1;
        }
        Rebuild();
        OnPropertyChanged(nameof(FooterLines));
    }

    static bool InFolder(string fileDir, string folder, TimelineMethod method)
    {
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return string.Equals(fileDir, f, StringComparison.OrdinalIgnoreCase)
            || (method == TimelineMethod.FolderNames && fileDir.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase));
    }

    public string? LeaveQuestion => Month?.LeaveQuestion;

    bool ILongRunningPage.IsLocked => Month is { IsDiscarding: true };
    bool ILongRunningPage.MustConfirmClose => Month is { IsDiscarding: true };
    string ILongRunningPage.CloseQuestion => "Dosyalar atılıyor. Durdurulup pencere kapatılsın mı? Sıradaki dosyalar yerinde kalır.";

    public void RequestStop()
    {
        if (IsCounting) Stop();
        Month?.RequestStop();
    }
}
