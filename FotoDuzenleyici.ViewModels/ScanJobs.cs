namespace FotoDuzenleyici.ViewModels;

public enum FlowKind { Organize, Clean }

/// <summary>
/// Düzenle ile Temizle'nin farkları. Sihirbazın adımları (Klasörler, İnceleme, Onayla, Uygula) ortak; neyin
/// taranacağı, ayıklananların nereye gideceği ve nasıl uygulanacağı burada.
/// </summary>
public interface IScanJob
{
    FlowKind Kind { get; }
    IReadOnlyList<string> Sources { get; }
    List<MediaFile> Files { get; }
    List<DuplicateGroup> Duplicates { get; }
    IReadOnlyList<string> ScanErrors { get; }
    /// <summary>Düzenle: hedef ayarları; Temizle: null (Tarihsiz sayımı yok).</summary>
    PlanOptions? PlanOptions { get; }
    /// <summary>Benzerlik / netlik ölçümünde atlanacak klasörler.</summary>
    IReadOnlyList<string> IgnoreDirs { get; }
    /// <summary>true: kopyala + doğrula yok, aynı disk içinde yeri değişir (Temizle).</summary>
    bool MoveOnly { get; }
    /// <summary>Bitti ekranında açılacak klasör. Düzenle: arşiv; Temizle: ilk kökün _Ayiklananlar klasörü.</summary>
    string RejectsFolder { get; }
    void Scan(IProgress<(int, int)>? scan, IProgress<(int, int)>? hash, CancellationToken ct);
    /// <summary>Yinelenenlerde önerilen konum ("düzenli" / "öncelikli").</summary>
    bool IsPreferred(MediaFile f);
    Plan BuildPlan(IReadOnlyDictionary<MediaFile, MediaFile>? similarOf, IReadOnlySet<MediaFile>? flawed,
        IReadOnlySet<MediaFile>? keepDuplicates);
    string NewJournalPath(DateTime now);
    List<(string Journal, int Remaining)> FindUnfinished();
}

/// <summary>Düzenle: kaynaklar + arşiv, tarih klasörlerine kopyala + doğrula.</summary>
public sealed class OrganizeJob : IScanJob
{
    public OrganizeJob(OrganizeSession session) => Session = session;

    public OrganizeSession Session { get; }
    PlanOptions Opt => Session.PlanOptions;

    public FlowKind Kind => FlowKind.Organize;
    public IReadOnlyList<string> Sources => Session.Sources;
    public List<MediaFile> Files => Session.Files;
    public List<DuplicateGroup> Duplicates => Session.Duplicates;
    public IReadOnlyList<string> ScanErrors => Session.ScanErrors;
    public PlanOptions? PlanOptions => Opt;
    public IReadOnlyList<string> IgnoreDirs => ReviewBuilder.IgnoreDirs(Opt);
    public bool MoveOnly => false;
    public string RejectsFolder => Opt.DestinationRoot;

    public void Scan(IProgress<(int, int)>? scan, IProgress<(int, int)>? hash, CancellationToken ct) => Session.Scan(scan, hash, ct);
    public bool IsPreferred(MediaFile f) => DuplicateKeeper.IsOrganized(f, Opt);
    public Plan BuildPlan(IReadOnlyDictionary<MediaFile, MediaFile>? similarOf, IReadOnlySet<MediaFile>? flawed,
        IReadOnlySet<MediaFile>? keepDuplicates) => Session.BuildPlan(similarOf, flawed, keepDuplicates);
    public string NewJournalPath(DateTime now) => Path.Combine(Opt.DestinationRoot, "_Gunluk", $"islem-{now:yyyyMMdd-HHmmss}.jsonl");
    public List<(string Journal, int Remaining)> FindUnfinished() => Executor.FindUnfinishedIn(Path.Combine(Opt.DestinationRoot, "_Gunluk"));
}

/// <summary>Temizle: klasörler yerinde kalır, ayıklananlar her kökün _Ayiklananlar klasörüne taşınır.</summary>
public sealed class CleanJob : IScanJob
{
    public CleanJob(CleanSession session) => Session = session;

    public CleanSession Session { get; }

    public FlowKind Kind => FlowKind.Clean;
    public IReadOnlyList<string> Sources => Session.Roots;
    public List<MediaFile> Files => Session.Files;
    public List<DuplicateGroup> Duplicates => Session.Duplicates;
    public IReadOnlyList<string> ScanErrors => Session.ScanErrors;
    public PlanOptions? PlanOptions => null;
    /// <summary>_Ayiklananlar zaten taranmaz.</summary>
    public IReadOnlyList<string> IgnoreDirs => Array.Empty<string>();
    public bool MoveOnly => true;
    public string RejectsFolder => Path.Combine(Session.Roots[0], CleanSession.RejectsFolder);

    public void Scan(IProgress<(int, int)>? scan, IProgress<(int, int)>? hash, CancellationToken ct) => Session.Scan(scan, hash, ct);
    public bool IsPreferred(MediaFile f) => DuplicateKeeper.InAny(f, Session.PriorityFolders);
    public Plan BuildPlan(IReadOnlyDictionary<MediaFile, MediaFile>? similarOf, IReadOnlySet<MediaFile>? flawed,
        IReadOnlySet<MediaFile>? keepDuplicates) => Session.BuildPlan(keepDuplicates, similarOf, flawed);
    public string NewJournalPath(DateTime now) =>
        Path.Combine(Session.Roots[0], CleanSession.RejectsFolder, "_Gunluk", $"temizlik-{now:yyyyMMdd-HHmmss}.jsonl");
    public List<(string Journal, int Remaining)> FindUnfinished() => Session.FindUnfinished();
}
