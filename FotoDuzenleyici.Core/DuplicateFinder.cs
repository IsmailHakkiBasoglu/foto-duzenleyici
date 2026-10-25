using System.Security.Cryptography;

namespace FotoDuzenleyici.Core;

public sealed class DuplicateGroup
{
    public required MediaFile Keeper { get; init; }
    public required List<MediaFile> Copies { get; init; }
}

/// <summary>Önce boyut, sonra tam SHA-256. İçerik birebir aynı olmadıkça dosya kopya sayılmaz.</summary>
public static class DuplicateFinder
{
    public static List<DuplicateGroup> Find(IReadOnlyList<MediaFile> files, int parallelism = 4,
        IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        var candidates = files.GroupBy(f => f.Size).Where(g => g.Count() > 1 && g.Key > 0)
            .SelectMany(g => g).ToList();

        int done = 0;
        Parallel.ForEach(candidates,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = ct },
            f =>
            {
                try { f.Sha256 = Hash(f.Path); }
                catch (Exception ex) { f.Error = (f.Error is null ? "" : f.Error + "; ") + "hash: " + ex.Message; }
                progress?.Report((Interlocked.Increment(ref done), candidates.Count));
            });

        var groups = new List<DuplicateGroup>();
        foreach (var g in candidates.Where(f => f.Sha256 is not null).GroupBy(f => (f.Size, f.Sha256)))
        {
            if (g.Count() < 2) continue;
            // Orijinal = en eski oluşturma tarihli (kopyalar yeni tarih alır), eşitlikte yol sırası.
            var ordered = g.OrderBy(f => f.Created).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            groups.Add(new DuplicateGroup { Keeper = ordered[0], Copies = ordered.Skip(1).ToList() });
        }
        return groups;
    }

    public static string Hash(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs));
    }
}
