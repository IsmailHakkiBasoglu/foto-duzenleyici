using System.Text.Json;

namespace FotoDuzenleyici.Core;

/// <summary>Windows Geri Dönüşüm Kutusu. Testler sahte bir kutu verir.</summary>
public interface IRecycleBin
{
    /// <summary>false → bu dosya kutuya gitmez (sürücüde kutu yok, kutu kapalı, dosya kutudan büyük).</summary>
    bool CanRecycle(string path, long size);
    /// <summary>Kutuya gönderir; true yalnız dosya sonradan yerinde yoksa. Windows sormadan kalıcı silmez.</summary>
    bool Send(string path);
}

public enum DiscardRoute { RecycleBin, MoveToRejects }

/// <param name="Target">Taşınacaksa <c>_Ayiklananlar\Ayikla\…</c> içindeki boş ad; kutuya gidecekse null.</param>
public sealed record DiscardStep(MediaFile File, DiscardRoute Route, string? Target);

public sealed class DiscardResult
{
    public List<DiscardStep> Done { get; } = new();
    public List<(DiscardStep Step, string Error)> Failed { get; } = new();
    /// <summary>Taşıma günlüğü (<c>_Ayiklananlar\_Gunluk</c>): Geri Al bununla çalışır.</summary>
    public string? MoveJournal { get; set; }
    /// <summary>Kutuya gönderilenlerin kaydı (uygulama veri klasörü); geri getirme Windows'un "Geri yükle"siyle.</summary>
    public string? RecycleJournal { get; set; }
}

/// <summary>
/// Ay görünümünde "atılsın" denen dosyalar. Önce her dosya için kutunun alıp almayacağına bakılır; alamayacaksa
/// dosya kökün <c>_Ayiklananlar\Ayikla</c> klasörüne, Temizle'nin güvenceleriyle taşınır. Kalıcı silme yoktur.
/// </summary>
public static class Discarder
{
    public const string SubFolder = "Ayikla";

    public static List<DiscardStep> Plan(IEnumerable<MediaFile> files, string root, IRecycleBin bin)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var baseDir = Path.Combine(rootFull, CleanSession.RejectsFolder, SubFolder);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steps = new List<DiscardStep>();
        foreach (var f in files)
        {
            if (bin.CanRecycle(f.Path, f.Size)) { steps.Add(new DiscardStep(f, DiscardRoute.RecycleBin, null)); continue; }
            var full = Path.GetFullPath(f.Path);
            if (!full.StartsWith(rootFull + "\\", StringComparison.OrdinalIgnoreCase)) continue; // taşınacak yer yok
            var rel = Path.GetRelativePath(rootFull, full);
            var dir = Path.Combine(baseDir, Path.GetDirectoryName(rel) ?? "");
            var name = CleanSession.FreeName(dir, Path.GetFileName(rel), taken);
            steps.Add(new DiscardStep(f, DiscardRoute.MoveToRejects, Path.Combine(dir, name)));
        }
        return steps;
    }

    public static DiscardResult Run(IReadOnlyList<DiscardStep> steps, string root, IRecycleBin bin, string logDir,
        IProgress<(int done, int total, string current)>? progress = null, CancellationToken ct = default)
    {
        var res = new DiscardResult();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var recycles = steps.Where(s => s.Route == DiscardRoute.RecycleBin).ToList();
        var moves = steps.Where(s => s.Route == DiscardRoute.MoveToRejects).ToList();
        int n = 0;

        if (recycles.Count > 0)
        {
            Directory.CreateDirectory(logDir);
            res.RecycleJournal = FreeJournal(logDir, stamp);
            using var jw = new StreamWriter(res.RecycleJournal, append: true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            foreach (var s in recycles)
            {
                if (ct.IsCancellationRequested) break;
                progress?.Report((n++, steps.Count, Path.GetFileName(s.File.Path)));
                var e = new JournalEntry { Source = s.File.Path, Action = PlanAction.Discard.ToString() };
                try
                {
                    if (!File.Exists(s.File.Path)) throw new FileNotFoundException("Dosya yerinde yok");
                    if (!bin.Send(s.File.Path)) throw new IOException("Geri Dönüşüm Kutusu'na gönderilemedi");
                    e.State = "Recycled";
                    res.Done.Add(s);
                }
                catch (Exception ex)
                {
                    e.State = "Failed"; e.Error = ex.Message;
                    res.Failed.Add((s, ex.Message));
                }
                jw.WriteLine(JsonSerializer.Serialize(e));
            }
        }

        if (moves.Count > 0 && !ct.IsCancellationRequested)
        {
            var gunluk = Path.Combine(Path.GetFullPath(root), CleanSession.RejectsFolder, "_Gunluk");
            Directory.CreateDirectory(gunluk);
            res.MoveJournal = FreeJournal(gunluk, stamp);
            var items = moves.Select(s => new PlanItem
            {
                File = s.File, Action = PlanAction.Discard,
                TargetDir = Path.GetDirectoryName(s.Target!)!, TargetName = Path.GetFileName(s.Target!),
            }).ToList();
            int offset = n;
            var inner = progress is null ? null
                : new Progress(p => progress.Report((offset + p.done, steps.Count, p.current)));
            ExecuteResult er;
            try { er = new Executor(res.MoveJournal).Run(items, progress: inner, ct: ct, move: true); }
            catch (OperationCanceledException) { er = new ExecuteResult(); }
            foreach (var s in moves)
            {
                if (!File.Exists(s.File.Path) && File.Exists(s.Target)) { res.Done.Add(s); continue; }
                var err = er.Errors.FirstOrDefault(x => x.StartsWith(s.File.Path + ": ", StringComparison.OrdinalIgnoreCase));
                if (err is not null) res.Failed.Add((s, err[(s.File.Path.Length + 2)..]));
            }
        }
        return res;
    }

    /// <summary>Aynı saniyede ikinci bir işlem eskisinin günlüğüne eklenmesin.</summary>
    static string FreeJournal(string dir, string stamp)
    {
        var p = Path.Combine(dir, $"ayikla-{stamp}.jsonl");
        for (int i = 2; File.Exists(p); i++) p = Path.Combine(dir, $"ayikla-{stamp}-{i}.jsonl");
        return p;
    }

    sealed class Progress(Action<(int done, int total, string current)> a) : IProgress<(int done, int total, string current)>
    {
        public void Report((int done, int total, string current) v) => a(v);
    }
}
