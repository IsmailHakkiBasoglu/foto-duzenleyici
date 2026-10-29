using System.Collections.Concurrent;

namespace FotoDuzenleyici.Core;

/// <summary>Taranan fotoğrafların benzerlik izlerini önbellekten ya da hesaplayarak toplar. Dosyalara dokunmaz.</summary>
public static class SimilarityScan
{
    /// <param name="ignoreFolder">Bu klasördekiler (önceki turda ayıklanan _Benzerler) hesaba katılmaz.</param>
    /// <param name="errors">İzi çıkarılamayan dosyalar ("yol: sebep") ve önbellek kayıt hatası buraya eklenir.</param>
    public static Dictionary<MediaFile, ImagePrint> Compute(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups,
        string? ignoreFolder, HashCache cache, List<string> errors,
        IProgress<(int done, int total, int fromCache)>? progress = null, CancellationToken ct = default)
    {
        // Yalnızca fotoğraflar: RAW yavaş çözülür ve eşi olan JPG zaten taranır; birebir kopyalar _Tekrarlar'a gider.
        var copies = dupGroups.SelectMany(g => g.Copies).ToHashSet();
        var ignore = ignoreFolder is null ? null : Path.GetFullPath(ignoreFolder).TrimEnd('\\') + "\\";
        var todo = files.Where(f => f.Kind == FileKind.Photo && !copies.Contains(f)
                                    && (ignore is null || !Path.GetFullPath(f.Path).StartsWith(ignore, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var result = new ConcurrentDictionary<MediaFile, ImagePrint>();
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
                    else if (ImageHasher.Print(f.Path, f.Orientation, out var error) is { } h)
                    {
                        cache.Set(key, h);
                        result[f] = h;
                    }
                    else lock (errors) errors.Add($"{f.Path}: {error}");
                }
                catch (Exception ex) { lock (errors) errors.Add($"{f.Path}: {ex.Message}"); }
                progress?.Report((Interlocked.Increment(ref done), todo.Count, Volatile.Read(ref fromCache)));
            });
        }
        finally
        {
            // Durdurulsa da o ana kadarki izler kalsın; bir sonraki tarama kaldığı yerden sürer.
            try { cache.Save(); }
            catch (Exception ex) { lock (errors) errors.Add("Önbellek kaydedilemedi: " + ex.Message); }
        }
        return new Dictionary<MediaFile, ImagePrint>(result);
    }
}
