using System.Text;
using FotoDuzenleyici.Core;

// Tarama raporu. --apply verilmedikçe hiçbir dosyayı değiştirmez.
// Kullanım: Cli --root <klasör> [--exclude <klasör>]... [--dest <klasör>] [--source-folder] [--rename-date]
//              [--append-source] [--no-hash] [--similar] [--csv <dosya>] [--apply [--keep-source]]
//           --similar: benzer fotoğrafları da arar (görüntü çözer, yavaş; izler önbelleğe yazılır)
//           Cli --undo <günlük.jsonl>
//           Cli --resume <günlük.jsonl>   (yarıda kalan --apply işlemini kaldığı yerden sürdürür)
var roots = new List<string>(); var excl = new List<string>();
string? dest = null, csv = null, undo = null, resume = null;
bool srcFolder = false, renameDate = false, appendSrc = false, noHash = false, apply = false, keepSource = false, similar = false;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--root": roots.Add(args[++i]); break;
        case "--exclude": excl.Add(args[++i]); break;
        case "--dest": dest = args[++i]; break;
        case "--csv": csv = args[++i]; break;
        case "--source-folder": srcFolder = true; break;
        case "--rename-date": renameDate = true; break;
        case "--append-source": appendSrc = true; break;
        case "--no-hash": noHash = true; break;
        case "--apply": apply = true; break;
        case "--keep-source": keepSource = true; break;
        case "--similar": similar = true; break;
        case "--undo": undo = args[++i]; break;
        case "--resume": resume = args[++i]; break;
    }
if (undo != null)
{
    var u = Executor.Undo(undo);
    Console.WriteLine($"Geri alındı: {u.Done}  Atlandı: {u.Skipped}  Hatalı: {u.Failed}");
    u.Errors.ForEach(Console.WriteLine);
    return u.Failed == 0 ? 0 : 2;
}
if (resume != null)
{
    Console.WriteLine($"Kalan dosya: {Executor.Pending(resume).Count}");
    var r = new Executor(resume).Resume();
    Console.WriteLine($"Başarılı: {r.Done}  Hatalı: {r.Failed}\nGünlük: {resume}");
    r.Errors.ForEach(Console.WriteLine);
    return r.Failed == 0 ? 0 : 2;
}
if (roots.Count == 0) { Console.WriteLine("--root gerekli"); return 1; }
dest ??= Path.Combine(roots[0], "_YENI_DUZEN");

var so = new ScanOptions { Roots = roots, ExcludedFolders = new(excl, StringComparer.OrdinalIgnoreCase) };
var sw = System.Diagnostics.Stopwatch.StartNew();
var scanner = new Scanner(so);
int lastPct = -1;
var files = scanner.Scan(new Progress<(int d, int t)>(p =>
{
    int pct = p.t == 0 ? 100 : p.d * 100 / p.t;
    if (pct != lastPct && pct % 10 == 0) { lastPct = pct; Console.Error.WriteLine($"  tarama %{pct}"); }
}));
Console.WriteLine($"Taranan medya/belge: {files.Count}  ({sw.Elapsed:mm\\:ss})");

var groups = new List<DuplicateGroup>();
if (!noHash)
{
    sw.Restart(); lastPct = -1;
    groups = DuplicateFinder.Find(files, 4, new Progress<(int d, int t)>(p =>
    {
        int pct = p.t == 0 ? 100 : p.d * 100 / p.t;
        if (pct != lastPct && pct % 10 == 0) { lastPct = pct; Console.Error.WriteLine($"  hash %{pct}"); }
    }));
    Console.WriteLine($"Hash süresi: {sw.Elapsed:mm\\:ss}");
}

Console.WriteLine("\n== Tür ==");
foreach (var g in files.GroupBy(f => f.Kind)) Console.WriteLine($"{g.Key,-10} {g.Count(),6}  {g.Sum(f => f.Size) / 1e9,8:N2} GB");
Console.WriteLine("\n== Tarih kaynağı ==");
foreach (var g in files.GroupBy(f => f.DateSource)) Console.WriteLine($"{g.Key,-14} {g.Count(),6}");
Console.WriteLine("\n== Kaynak ==");
foreach (var g in files.GroupBy(f => (f.Source ?? "(yok)") + (f.Source != null && !f.SourceFromExif ? " [dosya adından]" : "")))
    Console.WriteLine($"{g.Key,-26} {g.Count(),6}");
Console.WriteLine("\n== EXIF/video tarihi OLMAYANLAR (uzantı / tarih kaynağı) ==");
foreach (var g in files.Where(f => f.DateSource is not (DateSource.Exif or DateSource.VideoMetadata))
             .GroupBy(f => Path.GetExtension(f.Path).ToLowerInvariant() + " / " + f.DateSource))
    Console.WriteLine($"{g.Key,-24} {g.Count(),6}");

long reclaim = groups.Sum(g => g.Copies.Sum(c => c.Size));
Console.WriteLine($"\n== Yinelenen ==\nGrup: {groups.Count}  Fazla kopya: {groups.Sum(g => g.Copies.Count)}  Kazanılacak: {reclaim / 1e9:N2} GB");

var similarDir = Path.Combine(Path.GetFullPath(dest), "_Benzerler");
var same = SameFrameFinder.Find(files, groups, similarDir);
if (!similar)
{
    Console.WriteLine($"\n== Muhtemelen aynı kare (yalnızca rapor; arayüzden ayıklanır) ==\nGrup: {same.Count}  Dosya: {same.Sum(g => g.Members.Count)}");
    foreach (var g in same.Take(10))
        Console.WriteLine($"  {g.Members[0].Taken:yyyy-MM-dd HH:mm:ss}  " +
                          string.Join("  |  ", g.Members.Select(m => (m == g.Suggested ? "★ " : "") + m.FileName)));
}
else
{
    sw.Restart(); lastPct = -1;
    var simErrors = new List<string>();
    int cached = 0;
    var hashes = SimilarityScan.Compute(files, groups, similarDir, new HashCache(HashCache.DefaultPath), simErrors,
        new Progress<(int d, int t, int c)>(p =>
        {
            cached = Math.Max(cached, p.c);
            int pct = p.t == 0 ? 100 : p.d * 100 / p.t;
            if (pct != lastPct && pct % 10 == 0) { lastPct = pct; Console.Error.WriteLine($"  benzerlik izi %{pct}"); }
        }));
    var sims = SimilarFinder.Find(files, hashes, same);
    Console.WriteLine($"Benzerlik izi süresi: {sw.Elapsed:mm\\:ss}  (iz: {hashes.Count}, önbellekten yaklaşık: {cached})");
    string Name(SimilarKind k) => k switch
    {
        SimilarKind.SameFrame => "Aynı kare", SimilarKind.ResizedCopy => "Küçültülmüş kopya", _ => "Seri çekim",
    };
    Console.WriteLine("\n== Benzerler (yalnızca rapor; arayüzden ayıklanır) ==\n" +
                      string.Join("  ", Enum.GetValues<SimilarKind>().Select(k => $"{Name(k)}: {sims.Count(g => g.Kind == k)} grup")));
    Console.WriteLine($"İz çıkarılamayan: {simErrors.Count}");
    foreach (var e in simErrors.Take(5)) Console.WriteLine($"  {e}");
    foreach (var k in Enum.GetValues<SimilarKind>())
        foreach (var g in sims.Where(g => g.Kind == k).Take(5))
            Console.WriteLine($"  [{Name(k)}] " + string.Join("  |  ", g.Members.Select(m => (m == g.Suggested ? "★ " : "") + m.FileName)));
}

var plan = Planner.Build(files, groups, new PlanOptions
{
    DestinationRoot = dest, SourceFolder = srcFolder ? SourceFolderMode.SubFolder : SourceFolderMode.None,
    RenameWithDate = renameDate, AppendSourceToName = appendSrc,
});
Console.WriteLine($"\n== Plan ==  (hedef: {dest})");
foreach (var g in plan.Items.GroupBy(i => i.Action)) Console.WriteLine($"{g.Key,-15} {g.Count(),6}");
Console.WriteLine($"İsim çakışması (tarih eklenen): {plan.Collisions}   Yeniden adlanan: {plan.Renamed}");
Console.WriteLine("\n== Aylara göre (taşınacak) ==");
foreach (var g in plan.Items.Where(i => i.Action == PlanAction.Move)
             .GroupBy(i => Path.GetRelativePath(dest, i.TargetDir).Split('\\')[0]).OrderBy(g => g.Key))
    Console.WriteLine($"{g.Key,-14} {g.Count(),6}");
if (scanner.Errors.Count > 0) { Console.WriteLine("\n== Gezinme hataları =="); scanner.Errors.Take(10).ToList().ForEach(Console.WriteLine); }
var metaErr = files.Where(f => f.Error != null).ToList();
Console.WriteLine($"\nMetadata okuma hatası: {metaErr.Count}");
foreach (var f in metaErr.Take(8)) Console.WriteLine($"  {f.FileName}: {f.Error}");

if (csv != null)
{
    var sb = new StringBuilder("Eylem;Kaynak;Hedef;TarihKaynagi;Kamera;Not\n");
    foreach (var i in plan.Items)
        sb.AppendLine($"{i.Action};{i.File.Path};{i.TargetPath};{i.File.DateSource};{i.File.Source};{i.Note}");
    File.WriteAllText(csv, sb.ToString(), new UTF8Encoding(true));
    Console.WriteLine($"\nCSV: {csv}");
}

if (apply)
{
    if (noHash) { Console.WriteLine("--apply ile --no-hash birlikte kullanılamaz"); return 1; }
    foreach (var (j, left) in Executor.FindUnfinished(Path.GetFullPath(dest)))
        Console.WriteLine($"Uyarı: yarım kalan işlem ({left} dosya): --resume \"{j}\"");
    var journal = Path.Combine(Path.GetFullPath(dest), "_Gunluk", $"islem-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
    var r = new Executor(journal).Run(plan.Items, recycleSource: !keepSource);
    Console.WriteLine($"\n== Uygulandı ==\nBaşarılı: {r.Done}  Hatalı: {r.Failed}\nGünlük: {journal}");
    r.Errors.ForEach(Console.WriteLine);
    return r.Failed == 0 ? 0 : 2;
}
return 0;
