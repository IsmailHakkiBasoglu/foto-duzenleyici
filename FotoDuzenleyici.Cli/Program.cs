using System.Text;
using FotoDuzenleyici.Core;

// Tarama raporu. --apply verilmedikçe hiçbir dosyayı değiştirmez.
// Kullanım: Cli --root <klasör> [--exclude <klasör>]... [--dest <klasör>] [--source-folder] [--rename-date]
//              [--append-source] [--no-hash] [--similar] [--csv <dosya>] [--apply [--keep-source]]
//           --similar: benzer fotoğrafları da arar (görüntü çözer, yavaş; izler önbelleğe yazılır)
//           --quality: netlik ve karanlık ölçer (ölçümler önbelleğe yazılır); --csv ile …-kalite.csv
//           Cli --undo <günlük.jsonl>
//           Cli --resume <günlük.jsonl>   (yarıda kalan --apply işlemini kaldığı yerden sürdürür)
//           Cli --root <arşiv> --backup <yedek> [--include-duplicates] [--csv <dosya>]
//               arşivi yedeğe artımlı, hash doğrulamalı kopyalar; hiçbir şey silinmez (hata varsa çıkış 2)
//           Cli --verify-backup <yedek> [--csv <dosya>]   (yedeği tam okur; sorun varsa çıkış 1)
var roots = new List<string>(); var excl = new List<string>();
string? dest = null, csv = null, undo = null, resume = null, backup = null, verifyBackup = null;
bool srcFolder = false, renameDate = false, appendSrc = false, noHash = false, apply = false, keepSource = false, similar = false, quality = false,
    includeDups = false;
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
        case "--quality": quality = true; break;
        case "--undo": undo = args[++i]; break;
        case "--resume": resume = args[++i]; break;
        case "--backup": backup = args[++i]; break;
        case "--verify-backup": verifyBackup = args[++i]; break;
        case "--include-duplicates": includeDups = true; break;
    }

IProgress<BackupProgress> BackupBar(string label)
{
    int last = -1;
    return new Progress<BackupProgress>(p =>
    {
        int pct = p.BytesTotal == 0 ? 100 : (int)(p.BytesDone * 100 / p.BytesTotal);
        if (pct != last && pct % 10 == 0) { last = pct; Console.Error.WriteLine($"  {label} %{pct}  ({p.Done}/{p.Total})"); }
    });
}
string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):N1} GB" : $"{b / (double)(1L << 20):N1} MB";

if (verifyBackup != null)
{
    var v = new ArchiveBackup().Verify(verifyBackup, BackupBar("doğrulama"));
    if (v.Blocked != null) { Console.WriteLine(v.Blocked); return 2; }
    int missing = v.Problems.Count(p => p.Status == VerifyStatus.Missing), corrupt = v.Problems.Count - missing;
    Console.WriteLine($"{v.Ok:N0} tamam · {missing:N0} eksik · {corrupt:N0} bozuk · listede olmayan: {v.NotInManifest:N0}" +
                      (v.Cancelled ? " (iptal edildi)" : ""));
    foreach (var (e, s) in v.Problems) Console.WriteLine($"  {(s == VerifyStatus.Missing ? "EKSİK" : "BOZUK")}  {e.Path}");
    v.Errors.ForEach(e => Console.WriteLine("  " + e));
    if (csv != null)
    {
        var vCsv = Path.ChangeExtension(csv, null) + "-dogrulama.csv";
        var sb = new StringBuilder("Durum;Dosya\n");
        foreach (var (e, s) in v.Problems) sb.AppendLine($"{(s == VerifyStatus.Missing ? "Eksik" : "Bozuk")};{e.Path}");
        File.WriteAllText(vCsv, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"CSV: {vCsv}");
    }
    return v.Problems.Count == 0 ? 0 : 1;
}
if (backup != null)
{
    if (roots.Count == 0) { Console.WriteLine("--backup için --root <arşiv> gerekli"); return 1; }
    var r = new ArchiveBackup().Run(roots[0], backup, new BackupOptions { IncludeDuplicates = includeDups }, BackupBar("yedek"));
    if (r.Blocked != null) { Console.WriteLine(r.Blocked); return 2; }
    Console.WriteLine($"{r.New:N0} yeni · {r.Changed:N0} değişmiş · {r.Unchanged:N0} aynı · {r.MissingInArchive:N0} arşivde yok · " +
                      $"{r.Errors.Count:N0} hata · {Size(r.BytesCopied)} kopyalandı" + (r.Cancelled ? " (iptal edildi)" : ""));
    r.Errors.ForEach(e => Console.WriteLine("  " + e));
    if (csv != null)
    {
        var bCsv = Path.ChangeExtension(csv, null) + "-yedek.csv";
        var sb = new StringBuilder("Durum;Dosya;Neden\n");
        foreach (var e in r.Errors)
        {
            int k = e.IndexOf(": ", StringComparison.Ordinal);
            sb.AppendLine(k < 0 ? $"Hata;;{e}" : $"Hata;{e[..k]};{e[(k + 2)..]}");
        }
        File.WriteAllText(bCsv, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"CSV: {bCsv}");
    }
    return r.Errors.Count == 0 ? 0 : 2;
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

// Arşiv (hedef) de taranır: başka oturumda arşive girmiş bir dosyanın kopyası tanınır.
var so = new ScanOptions
{
    Roots = OrganizeSession.ScanRoots(roots, dest).Where(Directory.Exists).ToList(),
    ExcludedFolders = new(excl, StringComparer.OrdinalIgnoreCase),
};
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
var ignoreDirs = new[] { similarDir, Path.Combine(Path.GetFullPath(dest), "_Kusurlu") };
var same = SameFrameFinder.Find(files, groups, ignoreDirs);
var sims = new List<SimilarGroup>();
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
    var hashes = SimilarityScan.Compute(files, groups, ignoreDirs, new HashCache(HashCache.DefaultPath), simErrors,
        new Progress<(int d, int t, int c)>(p =>
        {
            cached = Math.Max(cached, p.c);
            int pct = p.t == 0 ? 100 : p.d * 100 / p.t;
            if (pct != lastPct && pct % 10 == 0) { lastPct = pct; Console.Error.WriteLine($"  benzerlik izi %{pct}"); }
        }));
    sims = SimilarFinder.Find(files, hashes, same);
    Console.WriteLine($"Benzerlik izi süresi: {sw.Elapsed:mm\\:ss}  (iz: {hashes.Count}, önbellekten yaklaşık: {cached})");
    string Name(SimilarKind k) => SimilarGroupState.KindName(k);
    Console.WriteLine("\n== Benzerler (yalnızca rapor; arayüzden ayıklanır) ==\n" +
                      string.Join("  ", Enum.GetValues<SimilarKind>().Where(k => k != SimilarKind.Flawed).Select(k => $"{Name(k)}: {sims.Count(g => g.Kind == k)} grup")));
    Console.WriteLine($"İz çıkarılamayan: {simErrors.Count}");
    foreach (var e in simErrors.Take(5)) Console.WriteLine($"  {e}");
    foreach (var k in Enum.GetValues<SimilarKind>().Where(k => k != SimilarKind.Flawed))
        foreach (var g in sims.Where(g => g.Kind == k).Take(5))
            Console.WriteLine($"  [{Name(k)}] " + string.Join("  |  ", g.Members.Select(m => (m == g.Suggested ? "★ " : "") + m.FileName)));

    // Tüm gruplar ayrı bir CSV'ye: elle incelemek için (yalnızca rapor).
    if (csv != null)
    {
        var simCsv = Path.ChangeExtension(csv, null) + "-benzerler.csv";
        var sb = new StringBuilder("Tur;Grup;Oneri;Dosya;Boyut;Cozunurluk;Cekim;Kamera\n");
        int no = 0;
        foreach (var g in sims)
        {
            no++;
            foreach (var m in g.Members)
                sb.AppendLine($"{Name(g.Kind)};{no};{(m == g.Suggested ? "★" : "")};{m.Path};{m.Size};" +
                              $"{m.Width}x{m.Height};{m.Taken:yyyy-MM-dd HH:mm:ss}{(m.SubSec is null ? "" : "." + m.SubSec)};{m.Model}");
        }
        File.WriteAllText(simCsv, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"Benzer grupları CSV: {simCsv}");
    }
}

if (quality)
{
    sw.Restart(); lastPct = -1;
    var qErrors = new List<string>();
    int qCached = 0;
    var scores = QualityScan.Compute(files, groups, ignoreDirs, new QualityCache(QualityCache.DefaultPath), qErrors,
        new Progress<(int d, int t, int c)>(p =>
        {
            qCached = Math.Max(qCached, p.c);
            int pct = p.t == 0 ? 100 : p.d * 100 / p.t;
            if (pct != lastPct && pct % 10 == 0) { lastPct = pct; Console.Error.WriteLine($"  netlik %{pct}"); }
        }));
    Console.WriteLine($"\n== Netlik / karanlık ==\nÖlçüm süresi: {sw.Elapsed:mm\\:ss}  Ölçülen: {scores.Count}  " +
                      $"Önbellekten yaklaşık: {qCached}  Ölçülemeyen: {qErrors.Count}");
    foreach (var e in qErrors.Take(5)) Console.WriteLine($"  {e}");
    Console.WriteLine("En düşük netlik:");
    foreach (var (f, s) in scores.OrderBy(kv => kv.Value.Sharpness).Take(10).Select(kv => (kv.Key, kv.Value)))
        Console.WriteLine($"  {s.Sharpness,8:N1}  parlaklık {s.MeanLuma,5:N0}{(s.Flat ? "  düz" : "")}  {f.Path}");

    if (csv != null)
    {
        var qCsv = Path.ChangeExtension(csv, null) + "-kalite.csv";
        var sb = new StringBuilder("Dosya;Netlik;Parlaklik;KaranlikOrani;Duz;Cekim;Kamera;Genislik\n");
        foreach (var (f, s) in scores.OrderBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase).Select(kv => (kv.Key, kv.Value)))
            sb.AppendLine($"{f.Path};{s.Sharpness:0.##};{s.MeanLuma:0.#};{s.DarkFraction:0.###};{(s.Flat ? 1 : 0)};" +
                          $"{f.Taken:yyyy-MM-dd HH:mm:ss};{f.Model};{f.Width}");
        File.WriteAllText(qCsv, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"Kalite CSV: {qCsv}");
    }

    // Arayüzle aynı kurallar: seri çekimde göreli sıralama, Benzerler dışında kalan kusurlu kareler.
    var states = SimilarGroupState.FromGroups(sims);
    foreach (var g in states) g.ApplyBurstRanks(scores);
    var flawed = FlawedList.Build(scores, states, states.Count + 1).Select(g => g.Rows[0]).ToList();
    Console.WriteLine($"Kusurlu (Benzerler dışında{(similar ? "" : "; --similar verilmedi, tüm fotoğraflar")}): " +
                      $"bulanık {flawed.Count(r => r.Flaw!.Contains("BULANIK"))}, karanlık {flawed.Count(r => r.Flaw!.Contains("KARANLIK"))}");
    if (similar)
        Console.WriteLine($"Seri çekimde en net önerisi: {states.Count(g => g.Rows.Any(r => r.Sharpest))} grup, " +
                          $"BULANIK kare: {states.Sum(g => g.Rows.Count(r => r.Flaw == "BULANIK"))}");
    if (csv != null)
    {
        var fCsv = Path.ChangeExtension(csv, null) + "-kusurlu.csv";
        var sb = new StringBuilder("Dosya;Neden;Netlik;Parlaklik;KaranlikOrani;Cekim\n");
        foreach (var r in flawed)
        {
            var s = scores[r.File];
            sb.AppendLine($"{r.Path};{r.Flaw};{s.Sharpness:0.##};{s.MeanLuma:0.#};{s.DarkFraction:0.###};{r.File.Taken:yyyy-MM-dd HH:mm:ss}");
        }
        File.WriteAllText(fCsv, sb.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"Kusurlu CSV: {fCsv}");
    }
}

var plan = Planner.Build(files, groups, new PlanOptions
{
    DestinationRoot = dest, SourceFolder = srcFolder ? SourceFolderMode.SubFolder : SourceFolderMode.None,
    RenameWithDate = renameDate, AppendSourceToName = appendSrc,
});
Console.WriteLine($"\n== Plan ==  (hedef: {dest})");
foreach (var g in plan.Items.GroupBy(i => i.Action)) Console.WriteLine($"{g.Key,-15} {g.Count(),6}");
Console.WriteLine($"İsim çakışması (tarih eklenen): {plan.Collisions}   Yeniden adlanan: {plan.Renamed}");
var pairs = RawJpgPairs.Groups(files);
var targetOf = plan.Items.ToDictionary(i => i.File);
var split = pairs.Count(g => g.Select(f => Path.GetFileNameWithoutExtension(targetOf[f].TargetName))
    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
Console.WriteLine($"RAW+JPG eşi: {pairs.Count}   Adı eşinden ayrılan: {split}");
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
