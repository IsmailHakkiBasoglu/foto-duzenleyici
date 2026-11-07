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

/// <summary>2. adım (ilk parça): özet kutucukları, dikkat listesi, süzülebilir plan tablosu.</summary>
public sealed partial class ReviewStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly List<PlanItem> _all;

    public ReviewStepViewModel(OrganizeSession session, Plan plan, Services s, ReviewData data)
    {
        _s = s;
        Session = session;
        Data = data;
        Plan = plan;
        Summary = PlanSummary.From(plan, session.PlanOptions);
        DuplicateGroups = session.Duplicates.Count;
        ScanErrors = data.Errors;
        _all = plan.Items.OrderBy(i => i.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public OrganizeSession Session { get; }
    public ReviewData Data { get; }
    public Plan Plan { get; }
    public PlanSummary Summary { get; }
    public int DuplicateGroups { get; }
    public IReadOnlyList<string> ScanErrors { get; }

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
        var path = _s.Files.SaveCsv("plan.csv");
        if (path is null) return;
        try { PlanCsv.Write(Plan, path); }
        catch (Exception ex) { _s.Dialogs.Info("Plan kaydedilemedi", ex.Message + "\n\nDosya başka bir programda (Excel?) açıksa kapatıp yeniden dene."); }
    }
}
