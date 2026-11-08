using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>3. adım: ne yapılacağının özeti ve uygulamadan önceki denetimler. Bu ekranın kendisi onaydır.</summary>
public sealed partial class ConfirmStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;
    readonly IScanJob _job;

    public ConfirmStepViewModel(Plan plan, IScanJob job, Services s, AppSettings settings, DateTime now)
    {
        _s = s; _settings = settings; _job = job;
        Plan = plan;
        Kind = job.Kind;
        MoveOnly = job.MoveOnly;
        Archive = job.RejectsFolder;
        Summary = PlanSummary.From(plan, job.PlanOptions);
        // _Benzerler / _Kusurlu'ya gidenler de kopyalanıp doğrulanır: yer onlar için de gerekir.
        NeedBytes = Summary.MoveBytes + Summary.DupBytes + Summary.SimilarBytes + Summary.FlawedBytes;
        // Ağ paylaşımı (NAS) ya da takılı olmayan disk: boş alan okunamaz → "bilinmiyor", Uygula engellenmez.
        // Temizle taşır, kopyalamaz: yer denetimi gerekmez.
        if (MoveOnly) { FreeBytes = -1; SpaceKnown = false; }
        else
        {
            try { FreeBytes = s.FreeSpace(ExistingAncestor(Archive)); SpaceKnown = true; }
            catch (Exception) { FreeBytes = -1; SpaceKnown = false; }
        }
        var archiveRoot = Path.GetPathRoot(Path.GetFullPath(Archive)) ?? "";
        OtherDriveSources = MoveOnly ? new List<string>() : job.Sources
            .Where(p => !string.Equals(Path.GetPathRoot(Path.GetFullPath(p)), archiveRoot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        JournalPath = job.NewJournalPath(now);
        recycleSource = settings.RecycleSource;
        RefreshUnfinished();
    }

    public Plan Plan { get; }
    /// <summary>Düzenle: arşiv; Temizle: _Ayiklananlar klasörü.</summary>
    public string Archive { get; }
    public FlowKind Kind { get; }
    /// <summary>Temizle: taşıma; yer denetimi, başka disk uyarısı ve Geri Dönüşüm kutusu yok.</summary>
    public bool MoveOnly { get; }
    public bool ShowSpace => !MoveOnly;

    // Özet satırlarının hedefleri: Düzenle arşivin _Tekrarlar / _Benzerler / _Kusurlu'su, Temizle _Ayiklananlar altı.
    public string DupTarget => MoveOnly ? @"→ _Ayiklananlar\Tekrarlar" : "→ _Tekrarlar";
    public string SimilarTarget => MoveOnly ? @"→ _Ayiklananlar\Benzerler" : "→ _Benzerler";
    public string FlawedTarget => MoveOnly ? @"→ _Ayiklananlar\Kusurlu" : "→ _Kusurlu";
    /// <summary>Temizle: ayıklananların toplam boyutu (kazanılacak alan).</summary>
    public long RejectBytes => Plan.Items.Where(i => i.Action != PlanAction.AlreadyInPlace).Sum(i => i.File.Size);
    public PlanSummary Summary { get; }
    public long NeedBytes { get; }
    public long FreeBytes { get; }
    public bool SpaceKnown { get; }
    public bool SpaceOk => !SpaceKnown || FreeBytes >= NeedBytes;
    public IReadOnlyList<string> OtherDriveSources { get; }
    public string JournalPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(UnfinishedRemaining))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private (string Journal, int Remaining)? unfinished;

    /// <summary>Ekran için: tuple alanlarına bağlanılamaz.</summary>
    public int UnfinishedRemaining => Unfinished?.Remaining ?? 0;

    [ObservableProperty] private bool recycleSource;

    partial void OnRecycleSourceChanged(bool value)
    {
        _settings.RecycleSource = value;
        _s.Settings.TrySave(_settings);
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
        var list = _job.FindUnfinished();
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
