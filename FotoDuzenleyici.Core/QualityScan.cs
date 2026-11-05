using System.Collections.Concurrent;

namespace FotoDuzenleyici.Core;

/// <summary>Taranan fotoğrafların netlik/karanlık ölçümlerini önbellekten ya da ölçerek toplar. Dosyalara dokunmaz.</summary>
public static class QualityScan
{
    /// <param name="ignoreFolders">Bu klasördekiler (önceki turlarda ayrılan _Benzerler, _Kusurlu) ölçülmez.</param>
    /// <param name="errors">Ölçülemeyen dosyalar ("yol: sebep") ve önbellek kayıt hatası buraya eklenir.</param>
    public static Dictionary<MediaFile, QualityScore> Compute(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups,
        IReadOnlyList<string> ignoreFolders, QualityCache cache, List<string> errors,
        IProgress<(int done, int total, int fromCache)>? progress = null, CancellationToken ct = default)
    {
        // Yalnızca fotoğraflar: RAW'ın eşi olan JPG zaten ölçülür; birebir kopyalar _Tekrarlar'a gider.
        var copies = dupGroups.SelectMany(g => g.Copies).ToHashSet();
        var ignore = ignoreFolders.Select(f => Path.GetFullPath(f).TrimEnd('\\') + "\\").ToList();
        var todo = files.Where(f => f.Kind == FileKind.Photo && !copies.Contains(f)
                                    && !ignore.Any(i => Path.GetFullPath(f.Path).StartsWith(i, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var result = new ConcurrentDictionary<MediaFile, QualityScore>();
        int done = 0, fromCache = 0;
        try
        {
            Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, f =>
            {
                try
                {
                    var key = HashCache.Key(f.Path);
                    if (cache.TryGet(key, out var cached))
                    {
                        result[f] = cached;
                        Interlocked.Increment(ref fromCache);
                    }
                    else if (QualityMeter.Measure(f.Path, f.Orientation, out var error) is { } s)
                    {
                        cache.Set(key, s);
                        result[f] = s;
                    }
                    else lock (errors) errors.Add($"{f.Path}: {error}");
                }
                catch (Exception ex) { lock (errors) errors.Add($"{f.Path}: {ex.Message}"); }
                progress?.Report((Interlocked.Increment(ref done), todo.Count, Volatile.Read(ref fromCache)));
            });
        }
        finally
        {
            // Durdurulsa da o ana kadarki ölçümler kalsın; bir sonraki tarama kaldığı yerden sürer.
            try { cache.Save(); }
            catch (Exception ex) { lock (errors) errors.Add("Önbellek kaydedilemedi: " + ex.Message); }
        }
        return new Dictionary<MediaFile, QualityScore>(result);
    }
}
