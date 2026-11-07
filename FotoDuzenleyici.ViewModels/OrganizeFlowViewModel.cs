using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public enum FlowStep { Folders, Review, Confirm, Apply }

/// <summary>Düzenle sihirbazı: Klasörler → Tara ve incele → Onayla → Uygulanıyor/Bitti.</summary>
public sealed partial class OrganizeFlowViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;
    CancellationTokenSource? _cts;

    public OrganizeFlowViewModel(Services s, AppSettings settings, FlowKind kind = FlowKind.Organize)
    {
        _s = s; _settings = settings; Kind = kind;
        Folders = new FoldersStepViewModel(s, settings, kind);
    }

    /// <summary>Düzenle ya da Temizle.</summary>
    public FlowKind Kind { get; }

    public FoldersStepViewModel Folders { get; }

    [ObservableProperty] private FlowStep step;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? progressText;
    /// <summary>0..1</summary>
    [ObservableProperty] private double progress;
    [ObservableProperty] private ReviewStepViewModel? review;

    /// <summary>Test kancası: tarama, iptal edilmiş bir belirteçle başlar.</summary>
    internal bool CancelBeforeStartForTest { get; set; }

    [RelayCommand]
    async Task Scan()
    {
        if (IsBusy || !Folders.CanScan) return;
        ErrorMessage = null;
        Review?.Detach();
        Review = null;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        if (CancelBeforeStartForTest) _cts.Cancel();
        var ct = _cts.Token;
        try
        {
            Folders.RememberCurrent(DateTime.Now);
            var job = CreateJob();
            var scanProg = new Progress<(int d, int t)>(p => Report($"Metadata okunuyor {p.d:N0} / {p.t:N0}", p.d, p.t));
            var hashProg = new Progress<(int d, int t)>(p => Report($"Hash hesaplanıyor {p.d:N0} / {p.t:N0}", p.d, p.t));
            var simProg = new Progress<(int d, int t, int c)>(p => Report($"Benzerlik izi… {p.d:N0} / {p.t:N0} ({p.c:N0}'i önbellekten)", p.d, p.t));
            var qualProg = new Progress<(int d, int t, int c)>(p => Report($"Netlik ölçülüyor… {p.d:N0} / {p.t:N0} ({p.c:N0}'i önbellekten)", p.d, p.t));
            bool findSimilar = _settings.FindSimilar, markFlawed = _settings.MarkFlawed;
            var (plan, data, note) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                OnPhaseForTest?.Invoke("metadata");
                job.Scan(scanProg, hashProg, ct);
                ct.ThrowIfCancellationRequested();
                var r = ScanReview(job, findSimilar, markFlawed, simProg, qualProg, ct);
                return (job.BuildPlan(null, null, null), r.Data, r.Note);
            }, ct);
            Review = new ReviewStepViewModel(job, plan, _s, data, m => ErrorMessage = m);
            ProgressText = null;
            ErrorMessage = note;
            Step = FlowStep.Review;
        }
        catch (OperationCanceledException) { ProgressText = "Durduruldu"; Step = FlowStep.Folders; }
        catch (Exception ex) { ErrorMessage = ex.Message; ProgressText = null; }
        finally { IsBusy = false; _cts = null; }
    }

    /// <summary>Test kancası: her aşamanın başında ("metadata", "similar", "quality"); arka plan iş parçacığında.</summary>
    internal Action<string>? OnPhaseForTest { get; set; }

    void Report(string text, int done, int total)
    {
        ProgressText = text;
        Progress = total == 0 ? 0 : (double)done / total;
    }

    /// <summary>
    /// Aşama 3 (benzerlik) ve 4 (netlik). Durdurulursa ya da hata olursa o aşamanın sonucu boş kalır, inceleme yine
    /// açılır; hesaplananlar önbellekte kalır. Not: hata şeridine gidecek metin.
    /// </summary>
    /// <summary>Taranacak iş: klasör adımındaki seçimlerden.</summary>
    IScanJob CreateJob() => Kind == FlowKind.Clean
        ? new CleanJob(new CleanSession(Folders.ExistingSources, Folders.ExistingPriority))
        : new OrganizeJob(new OrganizeSession(Folders.ExistingSources, Folders.BuildPlanOptions()));

    /// <summary>Test kancası: uygulamada her ilerleme bildiriminde (yapılan sayı), arka plan iş parçacığında.</summary>
    internal Action<int>? OnApplyProgressForTest { get; set; }

    (ReviewData Data, string? Note) ScanReview(IScanJob session, bool findSimilar, bool markFlawed,
        IProgress<(int, int, int)> simProg, IProgress<(int, int, int)> qualProg, CancellationToken ct)
    {
        var caches = _s.CacheFiles;
        var store = new ReviewStore(caches.Reviews);
        var notes = new List<string>();
        if (store.LoadFailed) notes.Add("Önceki benzer kararları okunamadı; boş başlandı.");
        var errors = new List<string>(session.ScanErrors);
        var ignore = session.IgnoreDirs;

        var prints = new Dictionary<MediaFile, ImagePrint>();
        bool similarDone = false, qualityDone = false;
        if (findSimilar)
        {
            OnPhaseForTest?.Invoke("similar");
            try
            {
                ct.ThrowIfCancellationRequested();
                prints = SimilarityScan.Compute(session.Files, session.Duplicates, ignore, new HashCache(caches.Hashes), errors, simProg, ct);
                similarDone = true;
            }
            catch (OperationCanceledException)
            {
                notes.Add("Benzerlik araması durduruldu; hesaplanan izler kaydedildi, bir sonraki taramada kaldığı yerden sürer.");
            }
            catch (Exception ex) { notes.Add("Benzerlik araması yapılamadı: " + ex.Message); }
        }

        var scores = new Dictionary<MediaFile, QualityScore>();
        if (markFlawed && !ct.IsCancellationRequested)
        {
            OnPhaseForTest?.Invoke("quality");
            try
            {
                ct.ThrowIfCancellationRequested();
                scores = QualityScan.Compute(session.Files, session.Duplicates, ignore, new QualityCache(caches.Quality), errors, qualProg, ct);
                qualityDone = true;
            }
            catch (OperationCanceledException)
            {
                notes.Add("Netlik ölçümü durduruldu; ölçülenler kaydedildi, bir sonraki taramada kaldığı yerden sürer.");
            }
            catch (Exception ex) { notes.Add("Netlik ölçülemedi: " + ex.Message); }
        }

        var similar = similarDone
            ? ReviewBuilder.BuildSimilar(session.Files, session.Duplicates, prints, ignore, scores, store)
            : new List<SimilarGroupState>();
        var flawed = qualityDone
            ? ReviewBuilder.BuildFlawed(session.Files, scores, similar, store)
            : new List<SimilarGroupState>();
        var data = new ReviewData(store, similar, flawed, scores, similarDone, qualityDone, errors);
        return (data, notes.Count == 0 ? null : string.Join(" ", notes));
    }

    [ObservableProperty] private ConfirmStepViewModel? confirm;

    [RelayCommand]
    void GoToConfirm()
    {
        if (IsBusy || Review is null || Step != FlowStep.Review) return;
        if (!Review.PrepareForConfirm()) return;
        Confirm = new ConfirmStepViewModel(Review.Plan, Review.Job, _s, _settings, DateTime.Now);
        Confirm.ApplyRequested += StartApply;
        Confirm.ResumeRequested += StartResume;
        Step = FlowStep.Confirm;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NavigationLocked))]
    private ApplyStepViewModel? apply;

    /// <summary>Son başlatılan uygulama / devam işi (testler ve pencere kapanışı bekler).</summary>
    public Task? ApplyTask { get; private set; }

    /// <summary>Uygulama sürerken adım değiştirilemez, Ana ekrana dönülemez.</summary>
    public bool NavigationLocked => Apply?.IsRunning == true;

    void StartApply()
    {
        var c = Confirm!;
        var items = c.Plan.Items.Where(i => i.Action != PlanAction.AlreadyInPlace).ToList();
        // Temizle: kopyalanmaz, yeri değişir; Geri Dönüşüm adımı yok.
        ApplyTask = RunApply(c.Archive, a => a.RunAsync(items, c.JournalPath, !c.MoveOnly && c.RecycleSource, c.MoveOnly));
    }

    void StartResume(string journal) => ApplyTask = RunApply(Confirm!.Archive, a => a.ResumeAsync(journal));

    /// <summary>Kabuktaki Geri Al: seçilen günlük, Uygulama adımında geri alınır. Arşiv: günlüğün <c>_Gunluk</c> klasörünün üstü.</summary>
    public Task StartUndo(string journal)
    {
        var archive = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(journal)))!;
        return ApplyTask = RunApply(archive, a => a.UndoAsync(journal));
    }

    /// <summary>Plan uygulandıktan (ya da devam ettirildikten) sonra geçersizdir: İnceleme ve Onayla kapanır.</summary>
    Task RunApply(string archive, Func<ApplyStepViewModel, Task> work)
    {
        var a = new ApplyStepViewModel(_s, archive) { OnProgressForTest = OnApplyProgressForTest };
        a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ApplyStepViewModel.IsRunning)) OnPropertyChanged(nameof(NavigationLocked)); };
        Apply = a;
        Confirm = null;
        Review = null;
        Step = FlowStep.Apply;
        return work(a);
    }

    /// <summary>Bekleyen inceleme kararlarını hemen yazar (pencere kapanırken, Ana ekrana dönerken).</summary>
    public void FlushReviews() => Review?.FlushReviews();

    [RelayCommand]
    void Cancel() => _cts?.Cancel();

    [RelayCommand]
    async Task Rescan()
    {
        Step = FlowStep.Folders;
        await Scan();
    }

    [RelayCommand]
    void Back()
    {
        if (IsBusy || NavigationLocked) return;
        if (Step == FlowStep.Apply) Apply = null;
        Step = Step switch
        {
            FlowStep.Confirm => FlowStep.Review,
            _ => FlowStep.Folders,
        };
    }
}
