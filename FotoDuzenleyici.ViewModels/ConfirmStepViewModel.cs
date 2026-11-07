using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>3. adım: ne yapılacağının özeti ve uygulamadan önceki denetimler. Bu ekranın kendisi onaydır.</summary>
public sealed partial class ConfirmStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;

    public ConfirmStepViewModel(Plan plan, PlanOptions opt, IEnumerable<string> sources, Services s, AppSettings settings, DateTime now)
    {
        _s = s; _settings = settings;
        Plan = plan;
        Archive = opt.DestinationRoot;
        Summary = PlanSummary.From(plan, opt);
        NeedBytes = Summary.MoveBytes + Summary.DupBytes;
        FreeBytes = s.FreeSpace(ExistingAncestor(Archive));
        var archiveRoot = Path.GetPathRoot(Path.GetFullPath(Archive)) ?? "";
        OtherDriveSources = sources
            .Where(p => !string.Equals(Path.GetPathRoot(Path.GetFullPath(p)), archiveRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        JournalPath = Path.Combine(Archive, "_Gunluk", $"islem-{now:yyyyMMdd-HHmmss}.jsonl");
        recycleSource = settings.RecycleSource;
        RefreshUnfinished();
    }

    public Plan Plan { get; }
    public string Archive { get; }
    public PlanSummary Summary { get; }
    public long NeedBytes { get; }
    public long FreeBytes { get; }
    public bool SpaceOk => FreeBytes >= NeedBytes;
    public IReadOnlyList<string> OtherDriveSources { get; }
    public string JournalPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private (string Journal, int Remaining)? unfinished;

    [ObservableProperty] private bool recycleSource;

    partial void OnRecycleSourceChanged(bool value)
    {
        _settings.RecycleSource = value;
        _s.Settings.Save(_settings);
    }

    public bool CanApply => SpaceOk && Unfinished is null && Summary.ApplyCount > 0;

    public event Action<string>? ResumeRequested;
    public event Action? ApplyRequested;

    [RelayCommand]
    void Abandon()
    {
        if (Unfinished is not { } u) return;
        if (!_s.Dialogs.Confirm("Yarım kalan işlem",
                $"Kalan {u.Remaining:N0} dosyaya dokunulmayacak ve bu işlem bir daha sorulmayacak. Vazgeçilsin mi?")) return;
        Executor.Abandon(u.Journal);
        RefreshUnfinished();
    }

    [RelayCommand]
    void Resume()
    {
        if (Unfinished is { } u) ResumeRequested?.Invoke(u.Journal);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    void Apply()
    {
        if (CanApply) ApplyRequested?.Invoke();   // Execute, CanExecute'a bakmaz
    }

    public void RefreshUnfinished()
    {
        var list = Directory.Exists(Archive) ? Executor.FindUnfinished(Archive) : new();
        Unfinished = list.Count > 0 ? list[0] : null;
    }

    /// <summary>Arşiv henüz yoksa boş alan, onu içeren ilk var olan üst klasörün sürücüsünden okunur.</summary>
    static string ExistingAncestor(string path)
    {
        var d = Path.GetFullPath(path);
        while (!Directory.Exists(d))
        {
            var parent = Path.GetDirectoryName(d);
            if (parent is null) return d;
            d = parent;
        }
        return d;
    }
}
