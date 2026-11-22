using System.Text.RegularExpressions;

namespace FotoDuzenleyici.Core;

/// <summary>Zaman çizelgesinin sayım yöntemi.</summary>
public enum TimelineMethod
{
    /// <summary>Düzenle'nin "yyyy-MM" klasörleri; EXIF okunmaz.</summary>
    FolderNames,
    /// <summary>Her dosyanın çekim tarihi okunur; klasör düzeni önemli değil.</summary>
    CaptureDate,
}

/// <summary>
/// Sayılan bir dosya. <paramref name="Folder"/>: klasör adlarından sayımda ay klasörünün tam yolu,
/// çekim tarihinden sayımda dosyanın doğrudan bulunduğu klasör.
/// </summary>
public sealed record TimelineEntry(int Year, int Month, FileKind Kind, long Size, string Folder, DateSource Source);

public sealed class TimelineResult
{
    public required string Root { get; init; }
    public TimelineMethod Method { get; init; }
    public List<TimelineEntry> Entries { get; } = new();
    /// <summary>Kök düzeyindeki özel klasörler → içlerindeki Foto/Raw/Video sayısı (yalnız &gt; 0 olanlar).</summary>
    public SortedDictionary<string, int> Special { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Klasör adlarından sayımda ay ve özel klasörler dışındaki Foto/Raw/Video sayısı.</summary>
    public int Outside { get; set; }
    /// <summary>"yol: ileti" — okunamayan klasör ve dosyalar.</summary>
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
}

/// <summary>Ay ay dosya sayımı. Salt okunur: hiçbir dosyaya dokunmaz.</summary>
public static class Timeline
{
    static readonly Regex MonthName = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    /// <summary>Düzenle ve Temizle'nin kendi klasörleri: hiçbir yöntemde, hiçbir derinlikte taranmaz.</summary>
    public static IReadOnlySet<string> SpecialFolderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "_Tekrarlar", "_Benzerler", "_Kusurlu", "_Gunluk", "_Ayiklananlar", "Tarihsiz" };

    public static bool IsMonthFolderName(string name) => MonthName.IsMatch(name);

    static bool Counted(string path) => FileClassifier.Classify(path) is FileKind.Photo or FileKind.Raw or FileKind.Video;

    /// <summary>Kökün doğrudan altında en az bir ay klasörü varsa klasör adları, yoksa çekim tarihi.</summary>
    public static TimelineMethod DefaultMethod(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root).Any(d => IsMonthFolderName(Path.GetFileName(d)))
                ? TimelineMethod.FolderNames : TimelineMethod.CaptureDate;
        }
        catch (Exception) { return TimelineMethod.CaptureDate; }
    }

    public static TimelineResult ByFolders(string root, CancellationToken ct = default)
    {
        var r = new TimelineResult { Root = root, Method = TimelineMethod.FolderNames };
        try
        {
            foreach (var (f, year, month, dir) in FolderPaths(root, r, ct))
                r.Entries.Add(new TimelineEntry(year, month, FileClassifier.Classify(f), Length(f, r), dir, DateSource.None));
        }
        catch (OperationCanceledException) { r.Entries.Clear(); r.Cancelled = true; }
        return r;
    }

    /// <summary>
    /// Klasör adlarından sayımın dosyaları: ay klasörlerinin altındaki sayılan dosyalar (ay klasörünün yılı, ayı ve
    /// tam yoluyla). Kök dosyaları ve ay dışı klasörler <see cref="TimelineResult.Outside"/>'a, özel klasörler
    /// <see cref="TimelineResult.Special"/>'a sayılır. İptalde OperationCanceledException.
    /// </summary>
    internal static List<(string Path, int Year, int Month, string Folder)> FolderPaths(string root, TimelineResult r, CancellationToken ct)
    {
        var list = new List<(string, int, int, string)>();
        string[] subs, files;
        try { subs = Directory.GetDirectories(root); files = Directory.GetFiles(root); }
        catch (Exception ex) { r.Errors.Add($"{root}: {ex.Message}"); return list; }

        r.Outside += files.Count(Counted);
        foreach (var dir in subs)
        {
            ct.ThrowIfCancellationRequested();
            if (Skipped(dir, r)) continue;
            var name = Path.GetFileName(dir);
            if (SpecialFolderNames.Contains(name))
            {
                int n = Walk(dir, r, ct).Count();
                if (n > 0) r.Special[name] = n;
            }
            else if (IsMonthFolderName(name))
            {
                int year = int.Parse(name[..4]), month = int.Parse(name[5..]);
                foreach (var f in Walk(dir, r, ct)) list.Add((f, year, month, dir));
            }
            else r.Outside += Walk(dir, r, ct).Count();
        }
        return list;
    }

    /// <param name="read">Dosya okuyucu; null → <see cref="Scanner.ReadFile"/>. Atarsa dosya "okunamadı" sayılır.</param>
    public static TimelineResult ByCaptureDate(string root, IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default, Func<string, MediaFile>? read = null)
    {
        read ??= Scanner.ReadFile;
        var r = new TimelineResult { Root = root, Method = TimelineMethod.CaptureDate };
        try
        {
            var paths = CapturePaths(root, r, ct);
            var found = new TimelineEntry?[paths.Count];
            int done = 0;
            Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            {
                try
                {
                    var m = read(paths[i]);
                    if (m.Taken is { } t)
                        found[i] = new TimelineEntry(t.Year, t.Month, m.Kind, m.Size,
                            Path.GetDirectoryName(paths[i]) ?? root, m.DateSource);
                }
                catch (Exception ex) { lock (r.Errors) r.Errors.Add($"{paths[i]}: {ex.Message}"); }
                progress?.Report((Interlocked.Increment(ref done), paths.Count));
            });
            r.Entries.AddRange(found.OfType<TimelineEntry>());
        }
        catch (OperationCanceledException) { r.Entries.Clear(); r.Cancelled = true; }
        catch (Exception ex) { r.Errors.Add($"{root}: {ex.Message}"); }
        return r;
    }

    /// <summary>
    /// Çekim tarihinden sayımın dosyaları: kökün altındaki her sayılan dosya (özel klasörler hariç; onlar
    /// <see cref="TimelineResult.Special"/>'a sayılır). Kök yoksa hata satırı ve boş liste.
    /// </summary>
    internal static List<string> CapturePaths(string root, TimelineResult r, CancellationToken ct)
    {
        // Kullanıcı özel bir klasörün kendisini seçtiyse (ör. Arsiv\Tarihsiz) kökü dışlama: tablo sessizce boş kalmasın.
        var excluded = new HashSet<string>(SpecialFolderNames, StringComparer.OrdinalIgnoreCase);
        excluded.Remove(Path.GetFileName(Path.TrimEndingDirectorySeparator(root)));
        var opt = new ScanOptions
        {
            Roots = { root },
            IncludedKinds = { FileKind.Photo, FileKind.Raw, FileKind.Video },
            ExcludedFolderNames = excluded,
        };
        opt.IncludedKinds.Remove(FileKind.Pdf);
        var scanner = new Scanner(opt);
        var paths = new List<string>();
        if (!Directory.Exists(root)) { r.Errors.Add($"{root}: klasör bulunamadı"); return paths; }
        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (!SpecialFolderNames.Contains(name) || Skipped(dir, r)) continue;
            int n = Walk(dir, r, ct).Count();
            if (n > 0) r.Special[name] = n;
        }
        foreach (var p in scanner.EnumerateFiles()) { ct.ThrowIfCancellationRequested(); paths.Add(p); }
        r.Errors.AddRange(scanner.Errors);
        return paths;
    }

    /// <summary>Klasörün altındaki sayılan dosyalar; özel, gizli+sistem ve bağlantı klasörlerine girmez.</summary>
    internal static IEnumerable<string> Walk(string dir, TimelineResult r, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string[] files = Array.Empty<string>(), subs = Array.Empty<string>();
        try { files = Directory.GetFiles(dir); subs = Directory.GetDirectories(dir); }
        catch (Exception ex) { lock (r.Errors) r.Errors.Add($"{dir}: {ex.Message}"); }
        foreach (var f in files)
            if (Counted(f)) yield return f;
        foreach (var s in subs)
        {
            if (SpecialFolderNames.Contains(Path.GetFileName(s)) || Skipped(s, r)) continue;
            foreach (var f in Walk(s, r, ct)) yield return f;
        }
    }

    /// <summary><see cref="Scanner"/> ile aynı kural: bağlantı noktaları ve gizli+sistem klasörler izlenmez.</summary>
    static bool Skipped(string dir, TimelineResult r)
    {
        try
        {
            var a = File.GetAttributes(dir);
            return a.HasFlag(FileAttributes.ReparsePoint) || (a.HasFlag(FileAttributes.Hidden) && a.HasFlag(FileAttributes.System));
        }
        catch (Exception ex) { lock (r.Errors) r.Errors.Add($"{dir}: {ex.Message}"); return true; }
    }

    internal static long Length(string f, TimelineResult r)
    {
        try { return new FileInfo(f).Length; }
        catch (Exception ex) { lock (r.Errors) r.Errors.Add($"{f}: {ex.Message}"); return 0; }
    }
}
