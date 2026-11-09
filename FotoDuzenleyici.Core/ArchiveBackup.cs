using System.IO;
using System.Security.Cryptography;

namespace FotoDuzenleyici.Core;

public sealed class BackupOptions
{
    /// <summary><c>_Tekrarlar</c> da yedeklensin mi? İçindekilerin bayt bayt aynısı arşivde zaten var.</summary>
    public bool IncludeDuplicates { get; init; }
    public string DuplicatesFolderName { get; init; } = "_Tekrarlar";
    /// <summary>Yedek listesi kaç dosyada bir kaydedilsin (iptal ya da çökmede kaybolan iş en fazla bu kadar).</summary>
    public int SaveEvery { get; init; } = 200;
}

public readonly record struct BackupProgress(int Done, int Total, long BytesDone, long BytesTotal, string Current);

public sealed class BackupResult
{
    public int New { get; set; }
    public int Changed { get; set; }
    public int Unchanged { get; set; }
    /// <summary>Yedek listesinde olup arşivde artık bulunmayan dosyalar (yedekte kalırlar).</summary>
    public int MissingInArchive { get; set; }
    public long BytesCopied { get; set; }
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
    /// <summary>Yedek diski doldu ya da koptu; iş yarıda bırakıldı, kalanlar sonra devam ettirilebilir.</summary>
    public bool Aborted { get; set; }
    /// <summary>İş hiç başlamadıysa nedeni (iç içe kök, yer yok, okunamayan liste, kök yok).</summary>
    public string? Blocked { get; set; }
}

/// <summary>Kopyalamadan önceki özet; <see cref="ArchiveBackup.Preview"/> üretir.</summary>
public sealed class BackupPreview
{
    public int New { get; set; }
    public int Changed { get; set; }
    public int Unchanged { get; set; }
    public int MissingInArchive { get; set; }
    public long BytesNeeded { get; set; }
    /// <summary>-1: ölçülmedi (tarama engelle erken durdu); long.MaxValue: ölçülemedi.</summary>
    public long FreeBytes { get; set; }
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
    public string? Blocked { get; set; }
}

public enum VerifyStatus { Ok, Missing, Corrupt }

public sealed class VerifyResult
{
    public int Ok { get; set; }
    public List<(BackupEntry Entry, VerifyStatus Status)> Problems { get; } = new();
    /// <summary>Yedekte olup listede olmayan dosyalar (ayrılmış klasörler hariç); yalnızca sayılır.</summary>
    public int NotInManifest { get; set; }
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
    public string? Blocked { get; set; }
}

public sealed class RepairResult
{
    public int Repaired { get; set; }
    public List<string> NotRepaired { get; } = new();
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
}

/// <summary>
/// Arşivi ikinci bir diske artımlı yedekler. Hiçbir şey silinmez: değişen dosyanın eski hali
/// <c>_EskiSurumler</c>'e taşınır. Her kopya SHA-256 ile doğrulanmadan listeye girmez.
/// Yarım kopyalar yalnız uygulamanın kendi <c>_Gecici</c> klasöründe durur; kullanıcı dosyalarıyla çakışmaz.
/// </summary>
public sealed class ArchiveBackup
{
    public const string OldVersionsFolder = "_EskiSurumler";
    public const string CorruptFolder = "_Bozuk";
    public const string TempFolder = "_Gecici";

    internal sealed record CopyInfo(string Sha256, long Size, DateTime Modified);

    public Func<string, long> FreeSpace { get; set; } = DefaultFreeSpace;
    /// <summary>Test kancası: geçici kopya yazıldıktan sonra, doğrulamadan önce çağrılır.</summary>
    internal Action<string>? AfterWrite { get; set; }
    /// <summary>Test kancası: her 1 MB'lık parça yazıldıktan sonra, o ana kadar yazılan baytla çağrılır.</summary>
    internal Action<long>? OnChunk { get; set; }
    internal Func<DateTime> Today { get; set; } = () => DateTime.Today;

    static long DefaultFreeSpace(string root)
    {
        try { return new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return long.MaxValue; } // UNC vb.: ölçülemiyorsa engelleme
    }

    /// <summary>Tam yol; sondaki ayıracı atar ama sürücü kökünü (<c>E:\</c>) korur.</summary>
    internal static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));

    static string WithSep(string p) => p.EndsWith('\\') ? p : p + "\\";

    internal static bool Nested(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(WithSep(a), StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(WithSep(b), StringComparison.OrdinalIgnoreCase);

    static bool IsReserved(string topName) =>
        topName.Equals(OldVersionsFolder, StringComparison.OrdinalIgnoreCase) ||
        topName.Equals(CorruptFolder, StringComparison.OrdinalIgnoreCase) ||
        topName.Equals(TempFolder, StringComparison.OrdinalIgnoreCase);

    public BackupResult Run(string archiveRoot, string backupRoot, BackupOptions opt,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var res = new BackupResult();
        var bak = Norm(backupRoot);
        var survey = Inspect(Norm(archiveRoot), bak, opt, res.Errors, ct);
        res.Unchanged = survey.Unchanged; res.MissingInArchive = survey.Missing;
        if (survey.Blocked is not null) { res.Blocked = survey.Blocked; return res; }
        if (survey.Cancelled) { res.Cancelled = true; return res; }
        try { Directory.CreateDirectory(bak); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { res.Blocked = $"Yedek klasörüne erişilemiyor: {ex.Message}"; return res; }
        CleanLeftovers(bak);

        var manifest = survey.Manifest!;
        var work = survey.Work;
        long need = survey.Need;

        long bytesDone = 0;
        for (int i = 0; i < work.Count; i++)
        {
            if (ct.IsCancellationRequested) { res.Cancelled = true; break; }
            var w = work[i];
            try
            {
                // Hedefte farklı içerikli bir dosya varsa üzerine yazılmaz, eski sürüm olarak saklanır
                // (yeni kopya doğrulandıktan sonra; kopya başarısızsa yedekteki mevcut dosya yerinde kalır).
                var aside = Path.Combine(bak, OldVersionsFolder, Stamp(), w.Rel);
                if (CopyVerified(w.Src, Path.Combine(bak, w.Rel), aside, bak, ct, out var info, out var err))
                {
                    manifest.Set(new BackupEntry(w.Rel, info.Size, info.Modified, info.Sha256, DateTime.UtcNow));
                    if (w.Changed) res.Changed++; else res.New++;
                    res.BytesCopied += info.Size;
                }
                else res.Errors.Add($"{w.Rel}: {err}");
            }
            catch (OperationCanceledException) { res.Cancelled = true; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                res.Errors.Add($"{w.Rel}: {ex.Message}");
                if (Fatal(ex, bak))
                {
                    res.Errors.Add("Yedekleme durdu: yedek diski dolu ya da erişilemiyor.");
                    res.Aborted = true;
                    break;
                }
            }
            bytesDone += w.Size;
            if ((i + 1) % Math.Max(1, opt.SaveEvery) == 0) TrySave(manifest, res.Errors);
            progress?.Report(new BackupProgress(i + 1, work.Count, bytesDone, need, w.Rel));
        }
        if (ct.IsCancellationRequested) res.Cancelled = true;
        TrySave(manifest, res.Errors);
        return res;
    }

    /// <summary>Kopyalamadan önceki özet: <see cref="Run"/> ile aynı denetim ve tarama. Hiçbir yere yazmaz.</summary>
    public BackupPreview Preview(string archiveRoot, string backupRoot, BackupOptions opt, CancellationToken ct = default)
    {
        var p = new BackupPreview();
        var s = Inspect(Norm(archiveRoot), Norm(backupRoot), opt, p.Errors, ct);
        p.New = s.Work.Count(w => !w.Changed);
        p.Changed = s.Work.Count(w => w.Changed);
        p.Unchanged = s.Unchanged;
        p.MissingInArchive = s.Missing;
        p.BytesNeeded = s.Need;
        p.FreeBytes = s.Free;
        p.Cancelled = s.Cancelled;
        p.Blocked = s.Blocked;
        return p;
    }

    sealed class Survey
    {
        public BackupManifest? Manifest;
        public List<(string Rel, string Src, long Size, bool Changed)> Work = new();
        public int Unchanged, Missing;
        public long Need, Free = -1;
        public bool Cancelled;
        public string? Blocked;
    }

    /// <summary>Kök denetimleri, liste, tarama ve yer hesabı; özet ile yedekleme bunu paylaşır. Yazmaz.</summary>
    Survey Inspect(string arc, string bak, BackupOptions opt, List<string> errors, CancellationToken ct)
    {
        var s = new Survey();
        if (!Directory.Exists(arc)) { s.Blocked = $"Arşiv klasörü bulunamadı: {arc}"; return s; }
        if (Nested(arc, bak)) { s.Blocked = "Yedek klasörü arşiv klasörüyle iç içe olamaz."; return s; }
        try { s.Manifest = BackupManifest.Load(bak); }
        catch (InvalidDataException ex) { s.Blocked = ex.Message; return s; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { s.Blocked = $"Yedek klasörüne erişilemiyor: {ex.Message}"; return s; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool SkipTop(string top) => IsReserved(top) ||
            (!opt.IncludeDuplicates && top.Equals(opt.DuplicatesFolderName, StringComparison.OrdinalIgnoreCase));
        foreach (var src in Walk(arc, SkipTop, errors))
        {
            if (ct.IsCancellationRequested) { s.Cancelled = true; return s; }
            var rel = Path.GetRelativePath(arc, src);
            if (rel.Equals(BackupManifest.FileName, StringComparison.OrdinalIgnoreCase)) continue;
            FileInfo fi;
            try { fi = new FileInfo(src); _ = fi.Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { errors.Add($"{rel}: {ex.Message}"); continue; }
            seen.Add(rel);
            if (s.Manifest.TryGet(rel, out var e))
            {
                if (e.Size == fi.Length && e.Modified == fi.LastWriteTimeUtc) { s.Unchanged++; continue; }
                s.Work.Add((rel, src, fi.Length, true));
            }
            else s.Work.Add((rel, src, fi.Length, false));
        }
        if (ct.IsCancellationRequested) { s.Cancelled = true; return s; }
        s.Work.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Rel, y.Rel));
        s.Missing = s.Manifest.Entries.Count(e => !seen.Contains(e.Path));
        s.Need = s.Work.Sum(w => w.Size);
        // Yedeklemenin başında silinecek yarım kopyalar da boş yer sayılır; yoksa özet yanlışlıkla "yer yetmiyor" der.
        s.Free = FreeSpace(bak);
        if (s.Free != long.MaxValue) s.Free = SaturatingAdd(s.Free, LeftoverBytes(bak));
        if (s.Need > 0 && s.Need + s.Need / 100 > s.Free)
            s.Blocked = $"Yedek diskinde yer yetmiyor: {Gb(s.Need)} gerekli, {Gb(s.Free)} boş.";
        return s;
    }

    /// <summary>Yedek listesindeki her dosyayı yedek diskinden okuyup hash'ini karşılaştırır. Arşive dokunmaz.</summary>
    public VerifyResult Verify(string backupRoot, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var res = new VerifyResult();
        var bak = Norm(backupRoot);
        if (!Directory.Exists(bak)) { res.Blocked = $"Yedek klasörü bulunamadı: {bak}"; return res; }
        BackupManifest manifest;
        try { manifest = BackupManifest.Load(bak); }
        catch (InvalidDataException ex) { res.Blocked = ex.Message; return res; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { res.Blocked = $"Yedek listesi okunamadı: {ex.Message}"; return res; }

        var entries = manifest.Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList();
        long total = entries.Sum(e => e.Size), done = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (ct.IsCancellationRequested) { res.Cancelled = true; break; }
            var e = entries[i];
            var path = Path.Combine(bak, e.Path);
            VerifyStatus status;
            try
            {
                if (!File.Exists(path)) status = VerifyStatus.Missing;
                else if (new FileInfo(path).Length != e.Size) status = VerifyStatus.Corrupt;
                else status = HashFile(path) == e.Sha256 ? VerifyStatus.Ok : VerifyStatus.Corrupt;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status = VerifyStatus.Corrupt;
                res.Errors.Add($"{e.Path}: {ex.Message}");
            }
            if (status == VerifyStatus.Ok) res.Ok++; else res.Problems.Add((e, status));
            done += e.Size;
            progress?.Report(new BackupProgress(i + 1, entries.Count, done, total, e.Path));
        }

        var known = new HashSet<string>(entries.Select(e => e.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var f in Walk(bak, IsReserved, res.Errors))
        {
            var rel = Path.GetRelativePath(bak, f);
            if (rel.Equals(BackupManifest.FileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!known.Contains(rel)) res.NotInManifest++;
        }
        return res;
    }

    /// <summary>
    /// Doğrulamada eksik ya da bozuk çıkan dosyaları arşivden yeniden kopyalar; yalnız arşivdeki dosya
    /// listedeki hash'le hâlâ aynıysa. Bozuk kopya silinmez, <c>_Bozuk\yyyy-MM-dd</c>'ye taşınır.
    /// </summary>
    public RepairResult Repair(string archiveRoot, string backupRoot, VerifyResult problems,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var res = new RepairResult();
        var arc = Norm(archiveRoot);
        var bak = Norm(backupRoot);
        if (Nested(arc, bak)) { res.Errors.Add("Yedek klasörü arşiv klasörüyle iç içe olamaz."); return res; }
        BackupManifest manifest;
        try { manifest = BackupManifest.Load(bak); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        { res.Errors.Add(ex.Message); return res; }

        var list = problems.Problems;
        long total = list.Sum(p => p.Entry.Size), done = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (ct.IsCancellationRequested) { res.Cancelled = true; break; }
            var (e, _) = list[i];
            var src = Path.Combine(arc, e.Path);
            try
            {
                if (!File.Exists(src)) res.NotRepaired.Add($"{e.Path}: arşivde artık yok");
                else if (HashFile(src) != e.Sha256) res.NotRepaired.Add($"{e.Path}: arşivdeki dosya yedeklendikten sonra değişmiş");
                else
                {
                    var aside = Path.Combine(bak, CorruptFolder, Stamp(), e.Path);
                    if (CopyVerified(src, Path.Combine(bak, e.Path), aside, bak, ct, out var info, out var err))
                    {
                        manifest.Set(e with { Sha256 = info.Sha256, Size = info.Size, Modified = info.Modified, BackedUp = DateTime.UtcNow });
                        res.Repaired++;
                    }
                    else res.Errors.Add($"{e.Path}: {err}");
                }
            }
            catch (OperationCanceledException) { res.Cancelled = true; break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                res.Errors.Add($"{e.Path}: {ex.Message}");
            }
            done += e.Size;
            progress?.Report(new BackupProgress(i + 1, list.Count, done, total, e.Path));
        }
        TrySave(manifest, res.Errors);
        return res;
    }

    /// <summary>
    /// <paramref name="src"/>'yi <c>_Gecici</c> altına kopyalar, yazılanı yeniden okuyup hash'ini karşılaştırır.
    /// Eşleşirse: hedefte aynı içerik zaten varsa onu benimser; farklı içerik varsa onu <paramref name="aside"/>'a
    /// taşır; sonra kopyayı hedefe koyar. Boyut ve zaman, okunan dosya tutamacından alınır (okuma sırasında
    /// başka yazan olamaz). İki denemede de hash eşleşmezse false.
    /// </summary>
    internal bool CopyVerified(string src, string dst, string aside, string bak, CancellationToken ct,
        out CopyInfo info, out string? error)
    {
        info = null!; error = null;
        var tmpDir = Path.Combine(bak, TempFolder);
        Directory.CreateDirectory(tmpDir);
        var tmp = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string srcHash;
                long size;
                DateTime modified;
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                {
                    size = input.Length;
                    modified = File.GetLastWriteTimeUtc(input.SafeFileHandle);
                    using var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                    using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buf = new byte[1 << 20];
                    long written = 0;
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        h.AppendData(buf, 0, n);
                        output.Write(buf, 0, n);
                        written += n;
                        OnChunk?.Invoke(written);
                    }
                    ct.ThrowIfCancellationRequested();
                    srcHash = Hex(h.GetHashAndReset());
                }
                AfterWrite?.Invoke(tmp);
                if (HashFile(tmp) == srcHash)
                {
                    info = new CopyInfo(srcHash, size, modified);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst))
                    {
                        // Önceki bir çalıştırma kopyalayıp listeye yazamadan kesildiyse aynı içerik zaten orada.
                        if (HashFile(dst) == srcHash) return true;
                        MoveAside(dst, aside);
                    }
                    File.SetLastWriteTimeUtc(tmp, modified);
                    File.Move(tmp, dst, overwrite: false);
                    return true;
                }
                File.Delete(tmp);
            }
            error = "kopya doğrulanamadı (hash iki denemede de uyuşmadı)";
            return false;
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    internal static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        return Hex(SHA256.HashData(fs));
    }

    static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    internal string Stamp() => Today().ToString("yyyy-MM-dd");

    /// <summary>Dosyayı <paramref name="target"/>'a taşır; orada dosya varsa adına " (2)", " (3)"… ekler.</summary>
    internal static string MoveAside(string path, string target)
    {
        var dir = Path.GetDirectoryName(target)!;
        var stem = Path.GetFileNameWithoutExtension(target);
        var ext = Path.GetExtension(target);
        for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, $"{stem} ({n}){ext}");
        Directory.CreateDirectory(dir);
        var attr = File.GetAttributes(path);
        if (attr.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        File.Move(path, target);
        return target;
    }

    /// <summary>
    /// Klasör ağacını gezer. Okunamayan klasör hata olarak kaydedilir ve atlanır; bağlantı noktaları
    /// (junction/symlink) ile gizli+sistem klasörleri (System Volume Information, $RECYCLE.BIN) gezilmez.
    /// <paramref name="skipTop"/> kökün hemen altındaki klasör adları için çağrılır.
    /// </summary>
    static IEnumerable<string> Walk(string root, Func<string, bool> skipTop, List<string> errors)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetRelativePath(root, dir)}: klasör okunamadı: {ex.Message}");
                continue;
            }
            foreach (var f in files) yield return f;
            bool atRoot = dir.Equals(root, StringComparison.OrdinalIgnoreCase);
            foreach (var d in dirs)
            {
                if (atRoot && skipTop(Path.GetFileName(d))) continue;
                FileAttributes a;
                try { a = File.GetAttributes(d); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetRelativePath(root, d)}: klasör okunamadı: {ex.Message}");
                    continue;
                }
                if (a.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (a.HasFlag(FileAttributes.Hidden) && a.HasFlag(FileAttributes.System)) continue;
                stack.Push(d);
            }
        }
    }

    /// <summary>Uygulamanın kendi yarım kopyaları (<c>_Gecici</c>) ve yarım kalmış liste kaydı (.tmp).</summary>
    static void CleanLeftovers(string bak)
    {
        var tmpDir = Path.Combine(bak, TempFolder);
        try
        {
            if (Directory.Exists(tmpDir))
                foreach (var p in Directory.GetFiles(tmpDir)) TryDelete(p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        TryDelete(Path.Combine(bak, BackupManifest.FileName + ".tmp"));
    }

    /// <summary><see cref="CleanLeftovers"/>'ın sileceği dosyaların toplam boyutu; okunamazsa 0.</summary>
    static long LeftoverBytes(string bak)
    {
        long sum = 0;
        try
        {
            var tmpDir = Path.Combine(bak, TempFolder);
            if (Directory.Exists(tmpDir))
                foreach (var p in Directory.GetFiles(tmpDir)) sum += new FileInfo(p).Length;
            var tmpList = new FileInfo(Path.Combine(bak, BackupManifest.FileName + ".tmp"));
            if (tmpList.Exists) sum += tmpList.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return sum;
    }

    static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static bool Fatal(Exception ex, string bak)
    {
        int code = ex.HResult & 0xFFFF;
        // ERROR_DISK_FULL (112), ERROR_HANDLE_DISK_FULL (39), ERROR_NOT_READY (21)
        return code is 112 or 39 or 21 || !Directory.Exists(bak);
    }

    static void TrySave(BackupManifest m, List<string> errors)
    {
        try { m.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { errors.Add($"Yedek listesi kaydedilemedi: {ex.Message}"); }
    }

    static string Gb(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : $"{b / (double)(1L << 20):0.0} MB";
}
