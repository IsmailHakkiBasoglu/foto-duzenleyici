namespace FotoDuzenleyici.Core;

/// <summary>
/// Düzenle akışı, arayüzden bağımsız: kaynak klasörler + arşiv → tarama → kopya tespiti → plan.
/// Arşiv her zaman taranır: başka bir oturumda arşive girmiş bir dosyanın kopyası yeniden gelirse
/// tanınır, arşive elle atılmış dosyalar da aynı adlandırmaya uyar.
/// </summary>
public sealed class OrganizeSession
{
    readonly ScanOptions _scan;

    public OrganizeSession(IEnumerable<string> sources, PlanOptions plan, ScanOptions? scan = null)
    {
        Sources = sources.ToList();
        PlanOptions = plan;
        _scan = scan ?? new ScanOptions();
        Roots = ScanRoots(Sources, plan.DestinationRoot);
    }

    public IReadOnlyList<string> Sources { get; }
    public PlanOptions PlanOptions { get; }
    /// <summary>Taranacak kökler: kaynaklar + arşiv; iç içe olanlar tekilleştirilmiş.</summary>
    public IReadOnlyList<string> Roots { get; }

    public List<MediaFile> Files { get; private set; } = new();
    public List<DuplicateGroup> Duplicates { get; private set; } = new();
    public List<string> ScanErrors { get; } = new();

    /// <summary>
    /// Kaynaklara arşivi ekler; başka bir kökün içinde kalan (ya da onunla aynı) kökleri atar,
    /// böylece hiçbir dosya iki kez taranmaz. Sıra korunur, arşiv sona eklenir.
    /// </summary>
    public static List<string> ScanRoots(IEnumerable<string> sources, string archive)
    {
        static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        static bool Inside(string child, string parent) =>
            child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent.EndsWith('\\') ? parent : parent + "\\", StringComparison.OrdinalIgnoreCase);

        var all = sources.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Norm).ToList();
        if (!string.IsNullOrWhiteSpace(archive)) all.Add(Norm(archive));
        var result = new List<string>();
        for (int i = 0; i < all.Count; i++)
        {
            var r = all[i];
            // Kendisinden önce gelen aynı kök ya da herhangi bir üst kök varsa atla.
            bool covered = all.Where((o, j) => j != i && Inside(r, o) && (!o.Equals(r, StringComparison.OrdinalIgnoreCase) || j < i)).Any();
            if (!covered) result.Add(r);
        }
        return result;
    }

    /// <summary>Okur, hiçbir şeyi değiştirmez: dosyaları ve metadatayı tarar, birebir kopyaları bulur.</summary>
    public void Scan(IProgress<(int done, int total)>? scanProgress = null, IProgress<(int done, int total)>? hashProgress = null,
        CancellationToken ct = default)
    {
        var opt = new ScanOptions
        {
            Roots = Roots.Where(Directory.Exists).ToList(),
            ExcludedFolders = _scan.ExcludedFolders,
            ExcludedFiles = _scan.ExcludedFiles,
            IncludedKinds = _scan.IncludedKinds,
            MaxParallelism = _scan.MaxParallelism,
        };
        var scanner = new Scanner(opt);
        Files = scanner.Scan(scanProgress, ct);
        ScanErrors.Clear();
        ScanErrors.AddRange(scanner.Errors);
        Duplicates = DuplicateFinder.Find(Files, 4, hashProgress, ct);
    }

    /// <summary>Planı kurar; Benzerler / Kusurlu / Yinelenenler sekmelerindeki kullanıcı kararlarını alır.</summary>
    public Plan BuildPlan(IReadOnlyDictionary<MediaFile, MediaFile>? similarOf = null, IReadOnlySet<MediaFile>? flawed = null,
        IReadOnlySet<MediaFile>? keepDuplicates = null) =>
        Planner.Build(Files, Duplicates, PlanOptions, similarOf, flawed, keepDuplicates);
}
