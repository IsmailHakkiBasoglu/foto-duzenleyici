namespace FotoDuzenleyici.Core;

/// <summary>
/// Temizle akışı, arayüzden bağımsız: klasörler taranır, birebir kopyalar bulunur; ayıklananlar
/// (fazla kopya, seçilmeyen benzer, kusurlu kare) kendi kökünün <c>_Ayiklananlar</c> klasörüne,
/// asıl göreli yoluyla taşınmak üzere planlanır. Tarih klasörü yok; düzen olduğu gibi kalır.
/// Benzerlik ve netlik ölçümü arayüzde <see cref="SimilarityScan"/> / <see cref="QualityScan"/> ile yapılır.
/// </summary>
public sealed class CleanSession
{
    public const string RejectsFolder = "_Ayiklananlar";
    public const string DuplicatesSub = "Tekrarlar", SimilarSub = "Benzerler", FlawedSub = "Kusurlu";

    readonly ScanOptions _scan;

    public CleanSession(IEnumerable<string> roots, IEnumerable<string>? priorityFolders = null, ScanOptions? scan = null)
    {
        Roots = OrganizeSession.ScanRoots(roots, "");
        PriorityFolders = priorityFolders?.ToList() ?? new List<string>();
        _scan = scan ?? new ScanOptions();
    }

    public IReadOnlyList<string> Roots { get; }
    /// <summary>Kopyalardan buradakiler kalır (en eskiden önce gelir).</summary>
    public IReadOnlyList<string> PriorityFolders { get; }

    public List<MediaFile> Files { get; internal set; } = new();
    public List<DuplicateGroup> Duplicates { get; internal set; } = new();
    public List<string> ScanErrors { get; } = new();

    /// <summary>Okur, hiçbir şeyi değiştirmez. <c>_Ayiklananlar</c> ve Düzenle'nin <c>_Tekrarlar</c> / <c>_Benzerler</c> /
    /// <c>_Kusurlu</c> / <c>_Gunluk</c> klasörleri taranmaz.</summary>
    public void Scan(IProgress<(int done, int total)>? scanProgress = null, IProgress<(int done, int total)>? hashProgress = null,
        CancellationToken ct = default)
    {
        // Düzenli arşiv temizlenirken Düzenle'nin ayırdıkları (_Tekrarlar, _Benzerler, _Kusurlu) ve günlükleri de taranmaz:
        // _Tekrarlar'daki eski kopya, tarih klasöründeki asıl dosyayı "fazla kopya" yapmasın.
        var o = new PlanOptions { DestinationRoot = "" };
        var names = new HashSet<string>(_scan.ExcludedFolderNames, StringComparer.OrdinalIgnoreCase)
            { RejectsFolder, o.DuplicatesFolderName, o.SimilarFolderName, o.FlawedFolderName, "_Gunluk" };
        var scanner = new Scanner(new ScanOptions
        {
            Roots = Roots.Where(Directory.Exists).ToList(),
            ExcludedFolders = _scan.ExcludedFolders,
            ExcludedFiles = _scan.ExcludedFiles,
            ExcludedFolderNames = names,
            IncludedKinds = _scan.IncludedKinds,
            MaxParallelism = _scan.MaxParallelism,
        });
        Files = scanner.Scan(scanProgress, ct);
        ScanErrors.Clear();
        ScanErrors.AddRange(scanner.Errors);
        Duplicates = DuplicateFinder.Find(Files, 4, hashProgress, ct);
    }

    /// <summary>Kopya grubunda kalan: kullanıcının seçtiği → öncelikli klasördeki → en eski.</summary>
    public MediaFile Keeper(DuplicateGroup g, IReadOnlySet<MediaFile>? chosen = null) =>
        DuplicateKeeper.Choose(g, m => DuplicateKeeper.InAny(m, PriorityFolders), chosen);

    /// <summary>Yalnız ayıklananların planı. Öncelik: kopya → benzer → kusurlu.</summary>
    public Plan BuildPlan(IReadOnlySet<MediaFile>? keepDuplicates = null,
        IReadOnlyDictionary<MediaFile, MediaFile>? similarOf = null, IReadOnlySet<MediaFile>? flawed = null)
    {
        var dupOf = new Dictionary<MediaFile, MediaFile>();
        foreach (var g in Duplicates)
        {
            var keeper = Keeper(g, keepDuplicates);
            foreach (var m in DuplicateKeeper.Members(g).Where(m => m != keeper)) dupOf[m] = keeper;
        }

        var plan = new Plan();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            MediaFile? similar = null;
            (PlanAction action, string sub)? route =
                dupOf.ContainsKey(f) ? (PlanAction.Duplicate, DuplicatesSub)
                : similarOf?.TryGetValue(f, out similar) == true ? (PlanAction.Similar, SimilarSub)
                : flawed?.Contains(f) == true ? (PlanAction.Flawed, FlawedSub)
                : null;
            if (route is null) continue;

            var root = RootOf(f.Path);
            if (root is null) continue; // köklerin dışında kalan dosya (olmamalı): dokunma
            var rel = Path.GetRelativePath(root, Path.GetFullPath(f.Path));
            var dir = Path.GetDirectoryName(Path.Combine(root, RejectsFolder, route.Value.sub, rel))!;
            var name = FreeName(dir, Path.GetFileName(rel), taken);

            dupOf.TryGetValue(f, out var keeper);
            plan.Items.Add(new PlanItem
            {
                File = f, TargetDir = dir, TargetName = name, Action = route.Value.action, DuplicateOf = keeper,
                Renamed = !name.Equals(f.FileName, StringComparison.Ordinal),
                Note = route.Value.action switch
                {
                    PlanAction.Similar => "Muhtemelen aynı kare: " + similar!.FileName,
                    PlanAction.Flawed => "Kusurlu kare",
                    _ => null,
                },
            });
        }
        return plan;
    }

    /// <summary>İşlem günlüğü: ilk kökün <c>_Ayiklananlar\_Gunluk</c> klasöründe.</summary>
    public string NewJournalPath() =>
        Path.Combine(Roots[0], RejectsFolder, "_Gunluk", $"temizlik-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

    /// <summary>Her kökün <c>_Ayiklananlar\_Gunluk</c> klasöründe yarım kalmış temizlik günlükleri.</summary>
    public List<(string Journal, int Remaining)> FindUnfinished() =>
        Roots.SelectMany(r => Executor.FindUnfinishedIn(Path.Combine(r, RejectsFolder, "_Gunluk"))).ToList();

    /// <summary>Dosyayı kapsayan en uzun kök.</summary>
    string? RootOf(string path)
    {
        var full = Path.GetFullPath(path);
        return Roots.Where(r => full.StartsWith(r.EndsWith('\\') ? r : r + "\\", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Length).FirstOrDefault();
    }

    /// <summary>Hedefte ya da plandaki başka bir hedefte aynı ad varsa " (2)", " (3)"… eklenir.</summary>
    static string FreeName(string dir, string name, HashSet<string> taken)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        for (int n = 2; taken.Contains(Path.Combine(dir, candidate)) || File.Exists(Path.Combine(dir, candidate)); n++)
            candidate = $"{stem} ({n}){ext}";
        taken.Add(Path.Combine(dir, candidate));
        return candidate;
    }
}
