using System.Text;
using FotoDuzenleyici.Core;

// Salt okunur tarama raporu. Hiçbir dosyayı değiştirmez.
// Kullanım: Cli --root <klasör> [--exclude <klasör>]... [--dest <klasör>] [--source-folder] [--rename-date]
//              [--append-source] [--no-hash] [--csv <dosya>]
var roots = new List<string>(); var excl = new List<string>();
string? dest = null, csv = null; bool srcFolder = false, renameDate = false, appendSrc = false, noHash = false;
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
return 0;
