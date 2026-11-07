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
    /// <summary>İş hiç başlamadıysa nedeni (iç içe kök, yer yok, okunamayan liste, kök yok).</summary>
    public string? Blocked { get; set; }
}

/// <summary>
/// Arşivi ikinci bir diske artımlı yedekler. Hiçbir şey silinmez: değişen dosyanın eski hali
/// <c>_EskiSurumler</c>'e taşınır. Her kopya SHA-256 ile doğrulanmadan listeye girmez.
/// </summary>
public sealed class ArchiveBackup
{
    public const string OldVersionsFolder = "_EskiSurumler";
    public const string CorruptFolder = "_Bozuk";
    const string PartExt = ".part";

    internal Func<string, long> FreeSpace { get; set; } = DefaultFreeSpace;
    /// <summary>Test kancası: .part yazıldıktan sonra, doğrulamadan önce çağrılır.</summary>
    internal Action<string>? AfterWrite { get; set; }
    internal Func<DateTime> Today { get; set; } = () => DateTime.Today;

    static long DefaultFreeSpace(string root)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace; }
        catch (ArgumentException) { return long.MaxValue; } // UNC vb.: ölçülemiyorsa engelleme
    }

    public BackupResult Run(string archiveRoot, string backupRoot, BackupOptions opt,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var res = new BackupResult();
        var arc = Path.GetFullPath(archiveRoot).TrimEnd('\\');
        var bak = Path.GetFullPath(backupRoot).TrimEnd('\\');
        if (!Directory.Exists(arc)) { res.Blocked = $"Arşiv klasörü bulunamadı: {arc}"; return res; }
        if (Nested(arc, bak)) { res.Blocked = "Yedek klasörü arşiv klasörüyle iç içe olamaz."; return res; }

        BackupManifest manifest;
        try
        {
            Directory.CreateDirectory(bak);
            manifest = BackupManifest.Load(bak);
        }
        catch (InvalidDataException ex) { res.Blocked = ex.Message; return res; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { res.Blocked = $"Yedek klasörüne erişilemiyor: {ex.Message}"; return res; }

        CleanLeftovers(bak);

        var work = new List<(string Rel, string Src, long Size, DateTime Modified, bool Changed)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in Directory.EnumerateFiles(arc, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(arc, src);
            if (Skip(rel, opt)) continue;
            seen.Add(rel);
            var fi = new FileInfo(src);
            if (manifest.TryGet(rel, out var e))
            {
                if (e.Size == fi.Length && e.Modified == fi.LastWriteTimeUtc) { res.Unchanged++; continue; }
                work.Add((rel, src, fi.Length, fi.LastWriteTimeUtc, true));
            }
            else work.Add((rel, src, fi.Length, fi.LastWriteTimeUtc, false));
        }
        res.MissingInArchive = manifest.Entries.Count(e => !seen.Contains(e.Path));

        long need = work.Sum(w => w.Size);
        if (need > 0)
        {
            long free = FreeSpace(bak);
            if (need + need / 100 > free)
            {
                res.Blocked = $"Yedek diskinde yer yetmiyor: {Gb(need)} gerekli, {Gb(free)} boş.";
                return res;
            }
        }

        long bytesDone = 0;
        for (int i = 0; i < work.Count; i++)
        {
            if (ct.IsCancellationRequested) { res.Cancelled = true; break; }
            var w = work[i];
            var dst = Path.Combine(bak, w.Rel);
            try
            {
                // Hedefte dosya varsa (değişmiş ya da liste dışı) üzerine yazılmaz, eski sürüm olarak saklanır.
                if (File.Exists(dst)) MoveAside(dst, Path.Combine(bak, OldVersionsFolder, Stamp(), w.Rel));
                if (CopyVerified(w.Src, dst, out var sha, out var err))
                {
                    manifest.Set(new BackupEntry(w.Rel, w.Size, w.Modified, sha, DateTime.UtcNow));
                    if (w.Changed) res.Changed++; else res.New++;
                    res.BytesCopied += w.Size;
                }
                else res.Errors.Add($"{w.Rel}: {err}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                res.Errors.Add($"{w.Rel}: {ex.Message}");
                if (Fatal(ex, bak)) { res.Errors.Add("Yedekleme durdu: yedek diski dolu ya da erişilemiyor."); break; }
            }
            bytesDone += w.Size;
            if ((i + 1) % Math.Max(1, opt.SaveEvery) == 0) manifest.Save();
            progress?.Report(new BackupProgress(i + 1, work.Count, bytesDone, need, w.Rel));
        }
        if (ct.IsCancellationRequested) res.Cancelled = true;
        TrySave(manifest, res);
        return res;
    }

    /// <summary>
    /// <paramref name="src"/>'yi <c>dst.part</c>'a kopyalar, yazılanı yeniden okuyup hash'ini karşılaştırır;
    /// eşleşirse zamanını kaynağınkine ayarlayıp gerçek adına çevirir. İki denemede de eşleşmezse false.
    /// </summary>
    internal bool CopyVerified(string src, string dst, out string sha, out string? error)
    {
        sha = ""; error = null;
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        var part = dst + PartExt;
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string srcHash;
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buf = new byte[1 << 20];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0)
                    {
                        h.AppendData(buf, 0, n);
                        output.Write(buf, 0, n);
                    }
                    srcHash = Hex(h.GetHashAndReset());
                }
                AfterWrite?.Invoke(part);
                if (HashFile(part) == srcHash)
                {
                    File.SetLastWriteTimeUtc(part, File.GetLastWriteTimeUtc(src));
                    File.Move(part, dst, overwrite: false);
                    sha = srcHash;
                    return true;
                }
                File.Delete(part);
            }
            error = "kopya doğrulanamadı (hash iki denemede de uyuşmadı)";
            return false;
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
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

    bool Skip(string rel, BackupOptions opt)
    {
        var first = rel.Split('\\')[0];
        if (first.Equals(OldVersionsFolder, StringComparison.OrdinalIgnoreCase) ||
            first.Equals(CorruptFolder, StringComparison.OrdinalIgnoreCase)) return true;
        if (!opt.IncludeDuplicates && first.Equals(opt.DuplicatesFolderName, StringComparison.OrdinalIgnoreCase)) return true;
        return rel.Equals(BackupManifest.FileName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Nested(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(a + "\\", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(b + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Uygulamanın kendi yarım kopyaları (.part) ve yarım kalmış liste kaydı (.tmp).</summary>
    static void CleanLeftovers(string bak)
    {
        foreach (var p in Directory.EnumerateFiles(bak, "*" + PartExt, SearchOption.AllDirectories))
            try { File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var tmp = Path.Combine(bak, BackupManifest.FileName + ".tmp");
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
    }

    static bool Fatal(Exception ex, string bak)
    {
        int code = ex.HResult & 0xFFFF;
        // ERROR_DISK_FULL (112), ERROR_HANDLE_DISK_FULL (39), ERROR_NOT_READY (21)
        return code is 112 or 39 or 21 || !Directory.Exists(bak);
    }

    static void TrySave(BackupManifest m, BackupResult res)
    {
        try { m.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { res.Errors.Add($"Yedek listesi kaydedilemedi: {ex.Message}"); }
    }

    static string Gb(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : $"{b / (double)(1L << 20):0.0} MB";
}
