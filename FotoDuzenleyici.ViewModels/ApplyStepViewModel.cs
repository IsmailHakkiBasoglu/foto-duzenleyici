using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public enum ApplyMode { Run, Resume, Undo }

/// <summary>3a/3b: uygulanıyor (ilerleme, Durdur) ve bitti (sonuç, hatalar, Geri Al).</summary>
public sealed partial class ApplyStepViewModel : ObservableObject
{
    public const int EtaAfter = 20;

    readonly Services _s;
    CancellationTokenSource? _cts;

    public ApplyStepViewModel(Services s, string archive)
    {
        _s = s;
        Archive = archive;
    }

    public string Archive { get; }

    [ObservableProperty] private ApplyMode mode;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool stopped;
    [ObservableProperty] private int done;
    [ObservableProperty] private int total;
    [ObservableProperty] private string? current;
    [ObservableProperty] private TimeSpan? remaining;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoneCount), nameof(FailedCount), nameof(SkippedCount), nameof(Errors))]
    private ExecuteResult? result;

    // ExecuteResult sayıları alan (field); WPF yalnız özelliklere bağlanır.
    public int DoneCount => Result?.Done ?? 0;
    public int FailedCount => Result?.Failed ?? 0;
    public int SkippedCount => Result?.Skipped ?? 0;
    public IReadOnlyList<string> Errors => Result?.Errors ?? (IReadOnlyList<string>)Array.Empty<string>();
    [ObservableProperty] private string journal = "";
    /// <summary>Durdurulduysa günlükte bekleyen dosya sayısı (Onayla ekranında devam ettirilebilir).</summary>
    [ObservableProperty] private int pendingAfterStop;
    [ObservableProperty] private string? errorMessage;

    /// <summary>Test kancası: işlem, iptal edilmiş bir belirteçle başlar.</summary>
    internal bool CancelBeforeStartForTest { get; set; }

    public Task RunAsync(IReadOnlyList<PlanItem> items, string journal, bool recycle) =>
        Execute(ApplyMode.Run, journal, (prog, ct) => new Executor(journal, _s.Recycle).Run(items, recycle, prog, ct));

    public Task ResumeAsync(string journal) =>
        Execute(ApplyMode.Resume, journal, (prog, ct) => new Executor(journal, _s.Recycle).Resume(prog, ct));

    public Task UndoAsync(string journal) =>
        Execute(ApplyMode.Undo, journal, (prog, ct) => Executor.Undo(journal, prog, ct, _s.Recycle));

    async Task Execute(ApplyMode mode, string journal,
        Func<IProgress<(int, int, string)>, CancellationToken, ExecuteResult> work)
    {
        if (IsRunning) return;
        Mode = mode; Journal = journal;
        Stopped = false; Result = null; ErrorMessage = null; PendingAfterStop = 0;
        Done = 0; Total = 0; Current = null; Remaining = null;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        if (CancelBeforeStartForTest) _cts.Cancel();
        var ct = _cts.Token;
        var sw = Stopwatch.StartNew();
        var prog = new Progress<(int d, int t, string c)>(p =>
        {
            Done = p.d; Total = p.t; Current = p.c;
            Remaining = Eta(p.d, p.t, sw.Elapsed);
        });
        try
        {
            Result = await Task.Run(() => work(prog, ct));
            Done = Total;
        }
        catch (OperationCanceledException)
        {
            Stopped = true;
            if (mode != ApplyMode.Undo && File.Exists(journal))
            {
                Result = FromJournal(journal);
                PendingAfterStop = Executor.Pending(journal).Count;
            }
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally
        {
            Remaining = null;
            IsRunning = false;
            _cts = null;
        }
    }

    /// <summary>İptalde Executor kısmi sonucu döndürmez; günlükteki son durumlardan hesaplanır.</summary>
    static ExecuteResult FromJournal(string journal)
    {
        var r = new ExecuteResult();
        foreach (var e in Executor.FinalStates(journal))
            switch (e.State)
            {
                case "Copied" or "Recycled" or "Moved": r.Done++; break;
                case "Failed": r.Failed++; r.Errors.Add($"{e.Source}: {e.Error}"); break;
                case "Skipped": r.Skipped++; break;
            }
        return r;
    }

    /// <summary>İlk <see cref="EtaAfter"/> işlemden sonra: geçen süre / yapılan × kalan.</summary>
    public static TimeSpan? Eta(int done, int total, TimeSpan elapsed) =>
        done < EtaAfter || total <= done ? null : TimeSpan.FromTicks(elapsed.Ticks / done * (total - done));

    [RelayCommand]
    void Cancel() => _cts?.Cancel();

    /// <summary>Durdur istendi, iş henüz bitmedi.</summary>
    public bool IsCancellationRequested => _cts?.IsCancellationRequested == true;

    [RelayCommand]
    async Task Undo()
    {
        if (IsRunning || string.IsNullOrEmpty(Journal)) return;
        if (!_s.Dialogs.Confirm("Geri Al",
                "Bu işlemde taşınan dosyalar doğrulanarak eski yerlerine döner; arşivdeki kopyaları Geri Dönüşüm Kutusu'na gider. Devam edilsin mi?"))
            return;
        // Durdurulmuş bir işte kalanlar: geri alınan işin "yarım" sayılıp sonra Devam et diye önerilmemesi için
        // bırakılır (dosyalarına dokunulmaz).
        var journal = Journal;
        if (File.Exists(journal) && Executor.Pending(journal).Count > 0)
        {
            try { Executor.Abandon(journal); }
            catch (Exception ex) { ErrorMessage = ex.Message; return; }
        }
        await UndoAsync(journal);
    }

    [RelayCommand]
    void OpenArchive() => _s.Shell.OpenFolder(Archive);

    /// <summary>Durdurulan işi yeniden taramadan, aynı günlükten sürdürür.</summary>
    [RelayCommand]
    async Task Resume()
    {
        if (IsRunning || !Stopped || Mode == ApplyMode.Undo || string.IsNullOrEmpty(Journal)) return;
        await ResumeAsync(Journal);
    }
}
