using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public enum BackupStep { Select, Preview, Running, Finished }
public enum BackupActivity { Backup, Verify, Repair }

/// <summary>Yedekle sayfası: klasör seçimi → özet → kopyalama → sonuç. Hiçbir adım arşive yazmaz.</summary>
public sealed partial class BackupFlowViewModel : ObservableObject
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

    /// <summary>Sürmekte olan kopyalama / doğrulama / onarım; pencere kapanışı bunu bekler.</summary>
    public Task? RunningTask { get; private set; }

    /// <summary>Test kancası: her ilerleme bildiriminde, arka plan iş parçacığında.</summary>
    internal Action<int>? OnProgressForTest { get; set; }

    /// <summary>Test kancası: tarama iptal edilmiş belirteçle başlar.</summary>
    internal bool CancelBeforeStartForTest { get; set; }

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
        if (Step == BackupStep.Preview) Step = BackupStep.Select;
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
            var p = await Task.Run(() => Engine().Preview(arc, bak, opt, ct));
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
        Step = BackupStep.Select;
    }
}
