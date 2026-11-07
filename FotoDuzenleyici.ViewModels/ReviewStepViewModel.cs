using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Taramanın inceleme sonuçları. <see cref="SimilarDone"/> / <see cref="QualityDone"/>: o aşama yapıldı ve bitti.</summary>
public sealed record ReviewData(ReviewStore Store, List<SimilarGroupState> Similar, List<SimilarGroupState> Flawed,
    IReadOnlyDictionary<MediaFile, QualityScore> Scores, bool SimilarDone, bool QualityDone, IReadOnlyList<string> Errors)
{
    public static ReviewData Empty(ReviewStore store, IReadOnlyList<string>? errors = null) =>
        new(store, new(), new(), new Dictionary<MediaFile, QualityScore>(), false, false, errors ?? Array.Empty<string>());
}

public enum PlanFilter { All, Move, Duplicate, InPlace, Renamed, Similar, Flawed }

public enum ReviewSection { Summary, Duplicates, Similar, Flawed, Errors }

/// <summary>
/// 2. adım: özet ve plan, üç inceleme bölümü (Yinelenenler, Benzerler, Kusurlu) ve okunamayanlar. İncelemede her karar
/// planı eskitir; Onayla'ya geçerken plan kararlarla yeniden kurulur (<see cref="PrepareForConfirm"/>).
/// </summary>
public sealed partial class ReviewStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly ReviewSaver _saver;
    List<PlanItem> _all = new();

    public ReviewStepViewModel(IScanJob job, Plan plan, Services s, ReviewData data, Action<string>? warn = null)
    {
        _s = s;
        Job = job;
        Data = data;
        var w = warn ?? (_ => { });
        _saver = new ReviewSaver(data.Store, (d, work) => s.Later(d, work), w);
        DuplicateGroups = job.Duplicates.Count;
        ScanErrors = data.Errors;
        Duplicates = new DuplicatesReviewViewModel(DuplicateGroupState.Build(job.Duplicates, job.IsPreferred), s, MarkStale,
            job.Kind == FlowKind.Clean ? "öncelikli" : "düzenli");
        Similar = new SimilarReviewViewModel(data.Similar, data.SimilarDone, _saver, s, MarkStale, w);
        Flawed = new FlawedReviewViewModel(data.Flawed, data.QualityDone, _saver, s, MarkStale, w);
        SetPlan(plan);
        // Önceki oturumlardan gelen "ayıkla" kararları ilk plana girmedi: Onayla'da kurulur.
        planStale = Similar.ReleasedCount + Flawed.ReleasedCount > 0;
    }

    public IScanJob Job { get; }
    public FlowKind Kind => Job.Kind;
    public ReviewData Data { get; }
    [ObservableProperty] private Plan plan = null!;
    [ObservableProperty] private PlanSummary summary = null!;
    public int DuplicateGroups { get; }
    public IReadOnlyList<string> ScanErrors { get; }

    public DuplicatesReviewViewModel Duplicates { get; }
    public SimilarReviewViewModel Similar { get; }
    public FlawedReviewViewModel Flawed { get; }

    [ObservableProperty] private ReviewSection section;

    [RelayCommand]
    void ShowSection(ReviewSection s) => Section = s;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BarTitle))]
    private bool planStale;

    /// <summary>Alt çubuğun ilk satırı; plan eskidiyse bunu söyler.</summary>
    public string BarTitle => PlanStale
        ? "Plan güncellenecek: onay ekranına geçerken seçimlerinle yeniden kurulur."
        : $"Plan hazır: {Summary.MoveCount:N0} dosya taşınacak, {Summary.DupCount:N0} kopya _Tekrarlar'a ayrılacak.";

    void MarkStale() => PlanStale = true;

    void SetPlan(Plan p)
    {
        Plan = p;
        Summary = PlanSummary.From(p, Job.PlanOptions);
        _all = p.Items.OrderBy(i => i.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(BarTitle));
    }

    /// <summary>
    /// Onayla'ya geçmeden önce: plan eskidiyse kararlarla yeniden kurar. Ayıklanan karelerin RAW eşi varsa bir kez
    /// sorar; İptal → false (İnceleme'de kalınır).
    /// </summary>
    public bool PrepareForConfirm()
    {
        _saver.Flush();
        if (!PlanStale) return true;
        var similarOf = ReviewBuilder.SimilarMap(Similar.Groups);
        var flawed = ReviewBuilder.FlawedSet(Flawed.Groups);
        var raws = ReviewBuilder.PendingRaws(Job.Files, similarOf, flawed);
        if (raws.Count > 0)
        {
            var answer = _s.Dialogs.Ask(ReviewBuilder.RawQuestionTitle, ReviewBuilder.RawQuestion(raws.Count));
            if (answer == Answer.Cancel) return false;
            if (answer == Answer.Yes) ReviewBuilder.IncludeRaws(raws, similarOf, flawed);
        }
        Duplicates.Refresh();
        var p = Job.BuildPlan(similarOf, flawed, Duplicates.UserKeepers);
        SetPlan(p);
        PlanStale = false;
        return true;
    }

    /// <summary>Bekleyen kararları hemen yazar (pencere kapanırken, yeniden taramada).</summary>
    public void FlushReviews() => _saver.Flush();

    /// <summary>Yeniden taramada: bu incelemenin olayları bırakılır, eski gruplar yeni incelemeyi etkilemez.</summary>
    public void Detach()
    {
        _saver.Flush();
        Duplicates.Detach();
        Similar.Detach();
        Flawed.Detach();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private PlanFilter filter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rows))]
    private string search = "";

    public IReadOnlyList<PlanItem> Rows => _all.Where(Match).ToList();

    bool Match(PlanItem i)
    {
        bool f = Filter switch
        {
            PlanFilter.Move => i.Action == PlanAction.Move,
            PlanFilter.Duplicate => i.Action == PlanAction.Duplicate,
            PlanFilter.InPlace => i.Action == PlanAction.AlreadyInPlace,
            PlanFilter.Renamed => i.Renamed,
            PlanFilter.Similar => i.Action == PlanAction.Similar,
            PlanFilter.Flawed => i.Action == PlanAction.Flawed,
            _ => true,
        };
        if (!f) return false;
        var q = Search?.Trim();
        return string.IsNullOrEmpty(q)
            || i.File.Path.Contains(q, StringComparison.OrdinalIgnoreCase)
            || i.TargetPath.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    void SaveCsv()
    {
        // Plan eskiyse önce kararlarla kurulur: CSV ekrandaki seçimleri yansıtsın.
        if (!PrepareForConfirm()) return;
        var path = _s.Files.SaveCsv("plan.csv");
        if (path is null) return;
        try { PlanCsv.Write(Plan, path); }
        catch (Exception ex) { _s.Dialogs.Info("Plan kaydedilemedi", ex.Message + "\n\nDosya başka bir programda (Excel?) açıksa kapatıp yeniden dene."); }
    }
}
