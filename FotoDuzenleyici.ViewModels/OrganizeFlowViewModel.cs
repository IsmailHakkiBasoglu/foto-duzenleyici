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

    public OrganizeFlowViewModel(Services s, AppSettings settings)
    {
        _s = s; _settings = settings;
        Folders = new FoldersStepViewModel(s, settings);
    }

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
        IsBusy = true;
        _cts = new CancellationTokenSource();
        if (CancelBeforeStartForTest) _cts.Cancel();
        var ct = _cts.Token;
        try
        {
            Folders.RememberCurrent(DateTime.Now);
            var session = new OrganizeSession(Folders.ExistingSources, Folders.BuildPlanOptions());
            var scanProg = new Progress<(int d, int t)>(p => { ProgressText = $"Taranıyor {p.d:N0} / {p.t:N0}"; Progress = p.t == 0 ? 0 : (double)p.d / p.t; });
            var hashProg = new Progress<(int d, int t)>(p => { ProgressText = $"Kopyalar aranıyor {p.d:N0} / {p.t:N0}"; Progress = p.t == 0 ? 0 : (double)p.d / p.t; });
            var plan = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                session.Scan(scanProg, hashProg, ct);
                ct.ThrowIfCancellationRequested();
                return session.BuildPlan();
            }, ct);
            Review = new ReviewStepViewModel(session, plan, _s);
            ProgressText = null;
            Step = FlowStep.Review;
        }
        catch (OperationCanceledException) { ProgressText = "Durduruldu"; Step = FlowStep.Folders; }
        catch (Exception ex) { ErrorMessage = ex.Message; ProgressText = null; }
        finally { IsBusy = false; _cts = null; }
    }

    [ObservableProperty] private ConfirmStepViewModel? confirm;

    [RelayCommand]
    void GoToConfirm()
    {
        if (IsBusy || Review is null || Step != FlowStep.Review) return;
        Confirm = new ConfirmStepViewModel(Review.Plan, Review.Session.PlanOptions, Review.Session.Sources, _s, _settings, DateTime.Now);
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
        ApplyTask = RunApply(c.Archive, a => a.RunAsync(items, c.JournalPath, c.RecycleSource));
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
        var a = new ApplyStepViewModel(_s, archive);
        a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ApplyStepViewModel.IsRunning)) OnPropertyChanged(nameof(NavigationLocked)); };
        Apply = a;
        Confirm = null;
        Review = null;
        Step = FlowStep.Apply;
        return work(a);
    }

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
