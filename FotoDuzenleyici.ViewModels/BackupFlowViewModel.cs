using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public enum BackupStep { Select, Preview, Running, Finished }
public enum BackupActivity { Backup, Verify, Repair }

/// <summary>Doğrulamada sorunlu dosya: göreli yol ve "eksik" / "bozuk".</summary>
public sealed record BackupProblemRow(string Path, string Reason);

/// <summary>Yedekle sayfası: klasör seçimi → özet → kopyalama → sonuç. Hiçbir adım arşive yazmaz.</summary>
public sealed partial class BackupFlowViewModel : ObservableObject, ILongRunningPage
{
    const long EtaAfterBytes = 16L << 20;

    readonly Services _s;
    readonly AppSettings _settings;
    CancellationTokenSource? _cts;

    /// <param name="archive">Dolu ise (Düzenle'nin Bitti ekranından) arşiv olarak bu kullanılır.</param>
    public BackupFlowViewModel(Services s, AppSettings settings, string? archive = null)
    {
        _s = s;
        _settings = settings;
        this.archive = archive ?? settings.LastBackup?.Archive ?? settings.Recent.FirstOrDefault()?.Archive;
        backup = settings.LastBackup?.Backup;
        includeDuplicates = settings.LastBackup?.IncludeDuplicates ?? false;
    }

    [ObservableProperty] private BackupStep step;
    [ObservableProperty] private BackupActivity activity;
    [ObservableProperty] private string? archive;
    [ObservableProperty] private string? backup;
    [ObservableProperty] private bool includeDuplicates;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private BackupPreview? preview;
    [ObservableProperty] private BackupResult? result;
    [ObservableProperty] private bool stopped;
    [ObservableProperty] private int done;
    [ObservableProperty] private int total;
    [ObservableProperty] private long bytesDone;
    [ObservableProperty] private long bytesTotal;
    [ObservableProperty] private string? current;
    [ObservableProperty] private TimeSpan? remaining;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private VerifyResult? verification;
    [ObservableProperty] private bool verifyPartial;
    [ObservableProperty] private RepairResult? repairs;
    [ObservableProperty] private IReadOnlyList<BackupProblemRow> problemRows = Array.Empty<BackupProblemRow>();

    partial void OnVerificationChanged(VerifyResult? value) => ProblemRows = value is null
        ? Array.Empty<BackupProblemRow>()
        : value.Problems.Select(p => new BackupProblemRow(p.Entry.Path, p.Status == VerifyStatus.Missing ? "eksik" : "bozuk")).ToList();

    public bool CanVerify => !IsScanning && Step != BackupStep.Running
        && !string.IsNullOrWhiteSpace(Backup) && Directory.Exists(Backup);

    public bool CanRepair => Step == BackupStep.Finished && Verification is { Blocked: null, Problems.Count: > 0 }
        && !string.IsNullOrWhiteSpace(Archive) && Directory.Exists(Archive);

    public string VerifySummary
    {
        get
        {
            if (Verification is not { } v) return "";
            if (v.Blocked is { } b) return b;
            if (!v.Cancelled && v.Ok == 0 && v.Problems.Count == 0)
                return "Bu klasörde bu uygulamayla alınmış bir yedek bulunamadı.";
            int missing = v.Problems.Count(p => p.Status == VerifyStatus.Missing);
            var text = $"{v.Ok:N0} sağlam · {missing:N0} eksik · {v.Problems.Count - missing:N0} bozuk";
            if (v.NotInManifest > 0) text += $" · {v.NotInManifest:N0} listede yok";
            return VerifyPartial ? text + " (yarım: doğrulama durduruldu)" : text;
        }
    }

    public string RepairSummary => Repairs is not { } r ? ""
        : $"{r.Repaired:N0} onarıldı · {r.NotRepaired.Count:N0} onarılamadı · {r.Errors.Count:N0} hata" + (r.Cancelled ? " (durduruldu)" : "");

    public IReadOnlyList<string> RepairIssues => Repairs is { } r ? r.NotRepaired.Concat(r.Errors).ToList() : Array.Empty<string>();

    /// <summary>Sürmekte olan kopyalama / doğrulama / onarım; pencere kapanışı bunu bekler.</summary>
    public Task? RunningTask { get; private set; }

    /// <summary>Test kancası: her ilerleme bildiriminde, arka plan iş parçacığında.</summary>
    internal Action<int>? OnProgressForTest { get; set; }

    /// <summary>Test kancası: tarama iptal edilmiş belirteçle başlar.</summary>
    internal bool CancelBeforeStartForTest { get; set; }

    /// <summary>Test kancası: tarama başlamadan önce, arka plan iş parçacığında.</summary>
    internal Action? BeforeScanForTest { get; set; }

    public bool CanScan => !IsScanning && Step != BackupStep.Running
        && !string.IsNullOrWhiteSpace(Backup) && !string.IsNullOrWhiteSpace(Archive) && Directory.Exists(Archive);

    public bool NothingToCopy => Preview is { Blocked: null, New: 0, Changed: 0 };

    public bool CanStart => Step == BackupStep.Preview && Preview is { Blocked: null } && !NothingToCopy;

    public bool CanResume => Step == BackupStep.Finished && Stopped && Activity == BackupActivity.Backup;

    public string NeededText => Sizes.Text(Preview?.BytesNeeded ?? 0);

    public string FreeText => Preview?.FreeBytes == long.MaxValue ? "ölçülemedi" : Sizes.Text(Preview?.FreeBytes ?? 0);

    public double Percent => BytesTotal > 0 ? BytesDone * 100.0 / BytesTotal : 0;

    public string ProgressText => $"{Done:N0} / {Total:N0} dosya";

    public string? RemainingText => Remaining is { } r
        ? r.TotalMinutes < 1 ? "Kalan: 1 dakikadan az"
        : r.TotalHours < 1 ? $"Kalan: yaklaşık {Math.Ceiling(r.TotalMinutes):N0} dakika"
        : $"Kalan: yaklaşık {(int)r.TotalHours} saat {r.Minutes} dakika"
        : null;

    public string FinishedSummary => Result switch
    {
        null => "",
        { Blocked: { } b } => b,
        { } r => $"{r.New:N0} yeni · {r.Changed:N0} değişmiş · {r.Unchanged:N0} aynı · {r.MissingInArchive:N0} arşivde yok · "
               + $"{r.Errors.Count:N0} hata · {Sizes.Text(r.BytesCopied)} kopyalandı",
    };

    public IReadOnlyList<string> FinishedErrors => Result?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();

    static readonly string[] Computed =
    {
        nameof(CanScan), nameof(NothingToCopy), nameof(CanStart), nameof(CanResume), nameof(NeededText), nameof(FreeText),
        nameof(Percent), nameof(ProgressText), nameof(RemainingText), nameof(FinishedSummary), nameof(FinishedErrors),
        nameof(CanVerify), nameof(CanRepair), nameof(VerifySummary), nameof(RepairSummary), nameof(RepairIssues),
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not null && !Computed.Contains(e.PropertyName) && e.PropertyName != nameof(Current))
            foreach (var c in Computed) base.OnPropertyChanged(new PropertyChangedEventArgs(c));
    }

    partial void OnArchiveChanged(string? value) => FoldersChanged();
    partial void OnBackupChanged(string? value) => FoldersChanged();
    partial void OnIncludeDuplicatesChanged(bool value) => FoldersChanged();

    /// <summary>Özet ve doğrulama seçilen klasörlere aittir; seçim değişince geçersizdir.</summary>
    void FoldersChanged()
    {
        Preview = null;
        Verification = null; VerifyPartial = false; Repairs = null;
        if (Step == BackupStep.Preview) Step = BackupStep.Select;
    }

    [RelayCommand]
    Task Verify()
    {
        if (!CanVerify) return Task.CompletedTask;
        var bak = Backup!;
        Result = null; Repairs = null; Verification = null; VerifyPartial = false;
        return Launch(BackupActivity.Verify, (prog, ct) => Engine().Verify(bak, prog, ct), r =>
        {
            VerifyPartial = r.Cancelled;
            Verification = r;
            Stopped = r.Cancelled;
        });
    }

    /// <summary>Yalnız son doğrulamanın sonucuyla; bitince doğrulama geçersizdir, istenirse yeniden yapılır.</summary>
    [RelayCommand]
    Task Repair()
    {
        if (!CanRepair) return Task.CompletedTask;
        string arc = Archive!, bak = Backup!;
        var problems = Verification!;
        Repairs = null;
        return Launch(BackupActivity.Repair, (prog, ct) => Engine().Repair(arc, bak, problems, prog, ct), r =>
        {
            Repairs = r;
            Verification = null; VerifyPartial = false;
            Stopped = r.Cancelled;
        });
    }

    [RelayCommand]
    void PickArchive()
    {
        if (_s.Folders.PickFolder("Arşiv klasörünü seç", Archive) is { } p) Archive = p;
    }

    [RelayCommand]
    void PickBackup()
    {
        if (_s.Folders.PickFolder("Yedek klasörünü seç", Backup) is { } p) Backup = p;
    }

    ArchiveBackup Engine() => new() { FreeSpace = _s.FreeSpace };

    BackupOptions Options() => new() { IncludeDuplicates = IncludeDuplicates };

    [RelayCommand]
    async Task Scan()
    {
        if (!CanScan) return;
        ErrorMessage = null;
        IsScanning = true;
        _cts = new CancellationTokenSource();
        if (CancelBeforeStartForTest) _cts.Cancel();
        var ct = _cts.Token;
        string arc = Archive!, bak = Backup!;
        var opt = Options();
        try
        {
            var before = BeforeScanForTest;
            var p = await Task.Run(() => { before?.Invoke(); return Engine().Preview(arc, bak, opt, ct); });
            if (p.Cancelled) { Preview = null; Step = BackupStep.Select; return; }
            Preview = p;
            Step = BackupStep.Preview;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; Preview = null; Step = BackupStep.Select; }
        finally { IsScanning = false; _cts = null; }
    }

    [RelayCommand]
    Task Start()
    {
        if (!CanStart) return Task.CompletedTask;
        _settings.LastBackup = new BackupPair(Archive!, Backup!, IncludeDuplicates, DateTime.Now);
        _s.Settings.TrySave(_settings);
        string arc = Archive!, bak = Backup!;
        var opt = Options();
        Result = null;
        return Launch(BackupActivity.Backup, (prog, ct) => Engine().Run(arc, bak, opt, prog, ct), r =>
        {
            Result = r;
            Stopped = r.Cancelled;
        });
    }

    /// <summary>Çalışan iş, ilk ilerleme bildiriminden önce görünür olsun diye önce görev, sonra iş.</summary>
    Task Launch<T>(BackupActivity activity, Func<IProgress<BackupProgress>, CancellationToken, T> work, Action<T> finished)
    {
        Activity = activity;
        Stopped = false; ErrorMessage = null;
        Done = 0; Total = 0; BytesDone = 0; BytesTotal = 0; Current = null; Remaining = null;
        Step = BackupStep.Running;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var tcs = new TaskCompletionSource();
        RunningTask = tcs.Task;
        _ = Go();
        return tcs.Task;

        async Task Go()
        {
            var sw = Stopwatch.StartNew();
            IProgress<BackupProgress> ui = new Progress<BackupProgress>(p =>
            {
                Done = p.Done; Total = p.Total; BytesDone = p.BytesDone; BytesTotal = p.BytesTotal; Current = p.Current;
                Remaining = Eta(p.BytesDone, p.BytesTotal, sw.Elapsed);
            });
            var hook = OnProgressForTest;
            var report = hook is null ? ui : new Tap(ui, hook);
            try
            {
                var r = await Task.Run(() => work(report, ct));
                finished(r);
                Step = BackupStep.Finished;
            }
            catch (Exception ex) { ErrorMessage = ex.Message; Step = BackupStep.Select; }
            finally
            {
                Remaining = null;
                _cts = null;
                tcs.TrySetResult();
            }
        }
    }

    sealed class Tap(IProgress<BackupProgress> inner, Action<int> hook) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress v) { hook(v.Done); inner.Report(v); }
    }

    /// <summary>İlk 16 MB'tan sonra: geçen süre / yapılan bayt × kalan bayt.</summary>
    public static TimeSpan? Eta(long done, long total, TimeSpan elapsed) =>
        done < EtaAfterBytes || total <= done ? null : TimeSpan.FromTicks((long)(elapsed.Ticks / (double)done * (total - done)));

    [RelayCommand]
    void Stop() => _cts?.Cancel();

    bool ILongRunningPage.IsLocked => Step == BackupStep.Running || IsScanning;
    bool ILongRunningPage.MustConfirmClose => Step == BackupStep.Running;
    void ILongRunningPage.RequestStop() => _cts?.Cancel();

    string ILongRunningPage.CloseQuestion => Activity switch
    {
        BackupActivity.Verify => "Doğrulama sürüyor. Durdurulsun mu?",
        BackupActivity.Repair => "Onarım sürüyor. Durdurulsun mu? Onarılanlar korunur, kalanlar sonra yeniden doğrulanıp onarılabilir.",
        _ => "Yedekleme sürüyor. Durdurulsun mu? O anki dosya yarıda kesilir, yedek tutarlı kalır; kalanlar sonra devam ettirilebilir.",
    };

    [RelayCommand]
    void Back()
    {
        if (Step != BackupStep.Preview) return;
        Preview = null;
        Step = BackupStep.Select;
    }

    /// <summary>Özeti atlamaz: arşiv arada değişmiş olabilir, yeniden taranır.</summary>
    [RelayCommand]
    Task Resume() => CanResume ? Scan() : Task.CompletedTask;

    [RelayCommand]
    void OpenBackup()
    {
        if (!string.IsNullOrWhiteSpace(Backup)) _s.Shell.OpenFolder(Backup);
    }

    [RelayCommand]
    void Reset()
    {
        if (Step == BackupStep.Running || IsScanning) return;
        Preview = null; Result = null; Stopped = false; ErrorMessage = null;
        Verification = null; VerifyPartial = false; Repairs = null;
        Step = BackupStep.Select;
    }
}
