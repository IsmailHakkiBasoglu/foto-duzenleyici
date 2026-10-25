using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace FotoDuzenleyici.Core;

public sealed class JournalEntry
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Action { get; set; } = "";
    public string Sha256 { get; set; } = "";
    /// <summary>Copied = hedefe kopyalandı ve doğrulandı; Recycled = kaynak Geri Dönüşüm Kutusu'na alındı.</summary>
    public string State { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class ExecuteResult
{
    public int Done, Failed, Skipped;
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Güvenli uygulama: kopyala → SHA-256 ile doğrula → zaman damgalarını doğrula → ancak sonra kaynağı
/// Geri Dönüşüm Kutusu'na al. Hedefte var olan hiçbir dosyanın üzerine yazmaz. Günlük (JSONL) tutar.
/// </summary>
public sealed class Executor
{
    readonly string _journalPath;
    public Executor(string journalPath) => _journalPath = journalPath;

    public ExecuteResult Run(IEnumerable<PlanItem> items, bool recycleSource = true,
        IProgress<(int done, int total, string current)>? progress = null, CancellationToken ct = default)
    {
        var list = items.Where(i => i.Action != PlanAction.AlreadyInPlace).ToList();
        var res = new ExecuteResult();
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        using var jw = new StreamWriter(_journalPath, append: true, new System.Text.UTF8Encoding(false));

        int n = 0;
        foreach (var it in list)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((n++, list.Count, it.File.FileName));
            var entry = new JournalEntry { Source = it.File.Path, Target = it.TargetPath, Action = it.Action.ToString() };
            try
            {
                if (!File.Exists(it.File.Path)) throw new FileNotFoundException("Kaynak yok");
                if (File.Exists(it.TargetPath)) throw new IOException("Hedef zaten var, üzerine yazılmaz");
                Directory.CreateDirectory(it.TargetDir);

                var srcInfo = new FileInfo(it.File.Path);
                var tmp = it.TargetPath + ".kopya_tmp";
                File.Copy(it.File.Path, tmp, overwrite: false);

                // Doğrulama: boyut + tam hash
                var srcHash = it.File.Sha256 ?? DuplicateFinder.Hash(it.File.Path);
                var dstHash = DuplicateFinder.Hash(tmp);
                if (new FileInfo(tmp).Length != srcInfo.Length || srcHash != dstHash)
                {
                    File.Delete(tmp);
                    throw new IOException("Doğrulama başarısız (hash uyuşmadı)");
                }
                // Zaman damgalarını kaynakla eşitle ve doğrula
                File.SetCreationTime(tmp, srcInfo.CreationTime);
                File.SetLastWriteTime(tmp, srcInfo.LastWriteTime);
                File.Move(tmp, it.TargetPath);
                var dstInfo = new FileInfo(it.TargetPath);
                if (dstInfo.LastWriteTime != srcInfo.LastWriteTime || dstInfo.CreationTime != srcInfo.CreationTime)
                    throw new IOException("Zaman damgası korunamadı; kaynak silinmedi");

                entry.Sha256 = dstHash; entry.State = "Copied";
                if (recycleSource)
                {
                    FileSystem.DeleteFile(it.File.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    entry.State = "Recycled";
                }
                res.Done++;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message; entry.State = "Failed";
                res.Failed++; res.Errors.Add($"{it.File.Path}: {ex.Message}");
            }
            jw.WriteLine(JsonSerializer.Serialize(entry));
            jw.Flush();
        }
        return res;
    }

    /// <summary>
    /// Geri alma: hedefteki dosyayı doğrulayarak eski yerine kopyalar, ancak sonra hedefi Geri Dönüşüm Kutusu'na alır.
    /// Eski yerde başka bir dosya varsa dokunmaz.
    /// </summary>
    public static ExecuteResult Undo(string journalPath, IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        var entries = ReadJournal(journalPath).Where(e => e.State is "Copied" or "Recycled").ToList();
        var res = new ExecuteResult(); int n = 0;
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((n++, entries.Count, Path.GetFileName(e.Target)));
            try
            {
                if (!File.Exists(e.Target)) { res.Skipped++; continue; }
                if (File.Exists(e.Source)) { res.Skipped++; continue; }
                var t = new FileInfo(e.Target);
                Directory.CreateDirectory(Path.GetDirectoryName(e.Source)!);
                var tmp = e.Source + ".geri_tmp";
                File.Copy(e.Target, tmp, false);
                if (DuplicateFinder.Hash(tmp) != e.Sha256) { File.Delete(tmp); throw new IOException("Doğrulama başarısız"); }
                File.SetCreationTime(tmp, t.CreationTime); File.SetLastWriteTime(tmp, t.LastWriteTime);
                File.Move(tmp, e.Source);
                FileSystem.DeleteFile(e.Target, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                res.Done++;
            }
            catch (Exception ex) { res.Failed++; res.Errors.Add($"{e.Target}: {ex.Message}"); }
        }
        return res;
    }

    public static List<JournalEntry> ReadJournal(string path) =>
        File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<JournalEntry>(l)!).ToList();
}
