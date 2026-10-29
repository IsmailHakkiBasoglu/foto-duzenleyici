using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace FotoDuzenleyici.Core;

public sealed class JournalEntry
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Action { get; set; } = "";
    public string Sha256 { get; set; } = "";
    /// <summary>
    /// Planned = işlem başında plana alındı; Started = bu dosyaya başlandı; Copied = hedefe kopyalandı ve doğrulandı;
    /// Recycled = kaynak Geri Dönüşüm Kutusu'na alındı; Failed = dokunulmadı; Skipped = yarım işlemden vazgeçildi.
    /// Bir dosya için birden çok satır olabilir, geçerli olan sonuncusudur.
    /// </summary>
    public string State { get; set; } = "";
    /// <summary>true ise kaynak Geri Dönüşüm Kutusu'na alınmaz (devam ederken de aynı seçim korunur).</summary>
    public bool KeepSource { get; set; }
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
/// İşlem başında planın tamamı günlüğe yazılır; yarıda kalırsa <see cref="Resume"/> kaldığı yerden sürdürür.
/// </summary>
public sealed class Executor
{
    const string TempSuffix = ".kopya_tmp";
    readonly string _journalPath;
    public Executor(string journalPath) => _journalPath = journalPath;

    public ExecuteResult Run(IEnumerable<PlanItem> items, bool recycleSource = true,
        IProgress<(int done, int total, string current)>? progress = null, CancellationToken ct = default)
    {
        var planned = items.Where(i => i.Action != PlanAction.AlreadyInPlace).Select(it => new JournalEntry
        {
            Source = it.File.Path, Target = it.TargetPath, Action = it.Action.ToString(),
            Sha256 = it.File.Sha256 ?? "", State = "Planned", KeepSource = !recycleSource,
        }).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
        using (var jw = OpenJournal())
        {
            foreach (var p in planned) jw.WriteLine(JsonSerializer.Serialize(p));
        }
        return Execute(planned, progress, ct);
    }

    /// <summary>Günlükte henüz bitmemiş dosyaları işler; yeniden tarama ve hash gerekmez.</summary>
    public ExecuteResult Resume(IProgress<(int done, int total, string current)>? progress = null, CancellationToken ct = default) =>
        Execute(Pending(_journalPath), progress, ct);

    StreamWriter OpenJournal()
    {
        // Kesintide son satır yarım kalmışsa yeni satır ona yapışmasın.
        bool partial = false;
        if (File.Exists(_journalPath))
            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                if (fs.Length > 0) { fs.Seek(-1, SeekOrigin.End); partial = fs.ReadByte() != '\n'; }
        var w = new StreamWriter(_journalPath, append: true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        if (partial) w.WriteLine();
        return w;
    }

    ExecuteResult Execute(List<JournalEntry> list, IProgress<(int done, int total, string current)>? progress, CancellationToken ct)
    {
        var res = new ExecuteResult();
        using var jw = OpenJournal();
        int n = 0;
        foreach (var p in list)
        {
            progress?.Report((n++, list.Count, Path.GetFileName(p.Source)));
            ct.ThrowIfCancellationRequested();
            var entry = new JournalEntry { Source = p.Source, Target = p.Target, Action = p.Action, Sha256 = p.Sha256, KeepSource = p.KeepSource };
            try
            {
                // Hash'i başlamadan günlüğe yaz: kaynak Geri Dönüşüm Kutusu'na gidip satır yazılamazsa hedef yine doğrulanabilsin.
                if (entry.Sha256.Length == 0 && File.Exists(p.Source)) entry.Sha256 = DuplicateFinder.Hash(p.Source);
                entry.State = "Started";
                jw.WriteLine(JsonSerializer.Serialize(entry));
                Process(entry);
                res.Done++;
            }
            catch (Exception ex)
            {
                entry.Error = ex.Message; entry.State = "Failed";
                res.Failed++; res.Errors.Add($"{p.Source}: {ex.Message}");
            }
            jw.WriteLine(JsonSerializer.Serialize(entry));
        }
        return res;
    }

    /// <summary>Tek dosya. Önceki bir turda yarıda kalmışsa oradan tamamlar; State'i sonuca göre ayarlar.</summary>
    static void Process(JournalEntry e)
    {
        var tmp = e.Target + TempSuffix;
        if (File.Exists(tmp)) File.Delete(tmp); // kopyalama ortasında kesilmiş, kaynak hâlâ yerinde

        if (File.Exists(e.Target))
        {
            // Hedefe yerleşmiş ama sonucu yazılamamış olabilir: içerik aynıysa kopyalama bitmiştir.
            if (e.Sha256.Length == 0 || DuplicateFinder.Hash(e.Target) != e.Sha256)
                throw new IOException("Hedef zaten var, üzerine yazılmaz");
            if (!File.Exists(e.Source)) { e.State = "Recycled"; return; }
        }
        else
        {
            if (!File.Exists(e.Source)) throw new FileNotFoundException("Kaynak yok");
            Directory.CreateDirectory(Path.GetDirectoryName(e.Target)!);

            var srcInfo = new FileInfo(e.Source);
            File.Copy(e.Source, tmp, overwrite: false);

            // Doğrulama: boyut + tam hash
            var dstHash = DuplicateFinder.Hash(tmp);
            if (new FileInfo(tmp).Length != srcInfo.Length || e.Sha256 != dstHash)
            {
                File.Delete(tmp);
                throw new IOException("Doğrulama başarısız (hash uyuşmadı)");
            }
            // Zaman damgalarını kaynakla eşitle ve doğrula
            File.SetCreationTime(tmp, srcInfo.CreationTime);
            File.SetLastWriteTime(tmp, srcInfo.LastWriteTime);
            File.Move(tmp, e.Target);
            var dstInfo = new FileInfo(e.Target);
            if (dstInfo.LastWriteTime != srcInfo.LastWriteTime || dstInfo.CreationTime != srcInfo.CreationTime)
                throw new IOException("Zaman damgası korunamadı; kaynak silinmedi");
        }

        e.State = "Copied";
        if (!e.KeepSource)
        {
            FileSystem.DeleteFile(e.Source, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            e.State = "Recycled";
        }
    }

    /// <summary>
    /// Geri alma: hedefteki dosyayı doğrulayarak eski yerine kopyalar, ancak sonra hedefi Geri Dönüşüm Kutusu'na alır.
    /// Eski yerde başka bir dosya varsa dokunmaz.
    /// </summary>
    public static ExecuteResult Undo(string journalPath, IProgress<(int done, int total, string current)>? progress = null,
        CancellationToken ct = default)
    {
        var entries = FinalStates(journalPath).Where(e => e.State is "Copied" or "Recycled").ToList();
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
                if (File.Exists(tmp)) File.Delete(tmp); // önceki geri almadan kalmış
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

    /// <summary>Günlüğün tüm satırları. Elektrik kesintisinde yarım kalmış satır atlanır.</summary>
    public static List<JournalEntry> ReadJournal(string path)
    {
        var list = new List<JournalEntry>();
        foreach (var l in File.ReadLines(path))
        {
            if (l.Length == 0) continue;
            try { if (JsonSerializer.Deserialize<JournalEntry>(l) is { } e) list.Add(e); }
            catch (JsonException) { }
        }
        return list;
    }

    /// <summary>Her dosyanın son durumu, plandaki sırasıyla.</summary>
    public static List<JournalEntry> FinalStates(string path)
    {
        var last = new Dictionary<string, JournalEntry>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach (var e in ReadJournal(path))
        {
            var key = e.Source + "|" + e.Target;
            if (!last.ContainsKey(key)) order.Add(key);
            last[key] = e;
        }
        return order.Select(k => last[k]).ToList();
    }

    /// <summary>Plana alınmış ama sonucu yazılmamış dosyalar.</summary>
    public static List<JournalEntry> Pending(string path) =>
        FinalStates(path).Where(e => e.State is "Planned" or "Started").ToList();

    /// <summary>Hedefin _Gunluk klasöründe bitmemiş dosyası olan günlükler.</summary>
    public static List<(string Journal, int Remaining)> FindUnfinished(string destinationRoot)
    {
        var dir = Path.Combine(destinationRoot, "_Gunluk");
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir, "*.jsonl").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => (f, Pending(f).Count)).Where(x => x.Count > 0).ToList();
    }

    /// <summary>Yarım işlemden vazgeç: kalan dosyalar Skipped olarak işaretlenir, dosyalara dokunulmaz.</summary>
    public static void Abandon(string journalPath)
    {
        var pending = Pending(journalPath);
        using var jw = new Executor(journalPath).OpenJournal();
        foreach (var e in pending)
        {
            if (File.Exists(e.Target + TempSuffix)) File.Delete(e.Target + TempSuffix);
            e.State = "Skipped"; jw.WriteLine(JsonSerializer.Serialize(e));
        }
    }
}
