using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public enum PlanFilter { All, Move, Duplicate, InPlace, Renamed }

/// <summary>2. adım (ilk parça): özet kutucukları, dikkat listesi, süzülebilir plan tablosu.</summary>
public sealed partial class ReviewStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly List<PlanItem> _all;

    public ReviewStepViewModel(OrganizeSession session, Plan plan, Services s)
    {
        _s = s;
        Session = session;
        Plan = plan;
        Summary = PlanSummary.From(plan, session.PlanOptions);
        DuplicateGroups = session.Duplicates.Count;
        ScanErrors = session.ScanErrors.ToList();
        _all = plan.Items.OrderBy(i => i.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public OrganizeSession Session { get; }
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
        if (path is not null) PlanCsv.Write(Plan, path);
    }
}
