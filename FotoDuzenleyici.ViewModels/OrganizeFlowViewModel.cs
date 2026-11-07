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
        if (IsBusy || Review is null) return;
        Confirm = new ConfirmStepViewModel(Review.Plan, Review.Session.PlanOptions, Review.Session.Sources, _s, _settings, DateTime.Now);
        OnConfirmCreated(Confirm);
        Step = FlowStep.Confirm;
    }

    /// <summary>Uygulama adımı (Görev 5) olaylara buradan bağlanır.</summary>
    partial void OnConfirmCreated(ConfirmStepViewModel c);

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
        if (IsBusy) return;
        Step = Step switch
        {
            FlowStep.Confirm => FlowStep.Review,
            _ => FlowStep.Folders,
        };
    }
}
