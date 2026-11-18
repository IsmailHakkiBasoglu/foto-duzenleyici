namespace FotoDuzenleyici.Core;

public sealed class MonthResult
{
    public List<MediaFile> Files { get; } = new();
    /// <summary>"yol: ileti" — okunamayan klasör ve dosyalar.</summary>
    public List<string> Errors { get; } = new();
    public bool Cancelled { get; set; }
}

/// <summary>
/// Zaman çizelgesindeki bir ayın dosyalarını, aya girerken yeniden okur. Salt okunur.
/// Klasör adlarından: hücre klasörlerinin altındaki her sayılan dosya (tarihi başka aya düşenler dahil).
/// Çekim tarihinden: hücre klasörlerinin doğrudan dosyalarından çekim tarihi o aya düşenler.
/// </summary>
public static class MonthReader
{
    public static MonthResult Read(TimelineMethod method, int year, int month, IReadOnlyList<string> folders,
        IProgress<(int done, int total)>? progress = null, CancellationToken ct = default, Func<string, MediaFile>? read = null)
    {
        read ??= Scanner.ReadFile;
        var r = new MonthResult();
        var scratch = new TimelineResult { Root = "", Method = method };
        try
        {
            var dirs = folders
                .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                ct.ThrowIfCancellationRequested();
                IEnumerable<string> found = method == TimelineMethod.FolderNames
                    ? Timeline.Walk(dir, scratch, ct)
                    : DirectFiles(dir, scratch);
                foreach (var p in found)
                    if (seen.Add(p)) paths.Add(p);
            }

            var files = new MediaFile?[paths.Count];
            int done = 0;
            Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            {
                try
                {
                    var m = read(paths[i]);
                    if (method == TimelineMethod.FolderNames
                        || (m.Taken is { } t && t.Year == year && t.Month == month))
                        files[i] = m;
                }
                catch (Exception ex) { lock (r.Errors) r.Errors.Add($"{paths[i]}: {ex.Message}"); }
                progress?.Report((Interlocked.Increment(ref done), paths.Count));
            });
            r.Files.AddRange(files.OfType<MediaFile>());
        }
        catch (OperationCanceledException) { r.Files.Clear(); r.Cancelled = true; }
        r.Errors.AddRange(scratch.Errors);
        return r;
    }

    static IEnumerable<string> DirectFiles(string dir, TimelineResult r)
    {
        try
        {
            return Directory.GetFiles(dir)
                .Where(f => FileClassifier.Classify(f) is FileKind.Photo or FileKind.Raw or FileKind.Video)
                .ToList();
        }
        catch (Exception ex) { r.Errors.Add($"{dir}: {ex.Message}"); return Array.Empty<string>(); }
    }
}
