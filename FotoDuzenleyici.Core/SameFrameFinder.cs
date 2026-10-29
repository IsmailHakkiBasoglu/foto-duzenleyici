namespace FotoDuzenleyici.Core;

public sealed class SameFrameGroup
{
    public required List<MediaFile> Members { get; init; }
    /// <summary>Kalması önerilen: en yüksek çözünürlük, sonra ilk yazılan (orijinal), sonra en büyük dosya.</summary>
    public required MediaFile Suggested { get; init; }
}

/// <summary>
/// Aynı EXIF parmak izi: kamera, model ve çekim anı (saniyesine kadar) aynı olan fotoğraflar büyük olasılıkla
/// aynı karedir (yeniden kaydedilmiş, dışa aktarılmış ya da başka yerden tekrar gelmiş). Hiçbir dosyaya dokunmaz;
/// hangisinin kalacağına kullanıcı karar verir.
/// </summary>
public static class SameFrameFinder
{
    /// <param name="ignoreFolder">Bu klasördeki dosyalar (ör. önceki turda ayıklanan _Benzerler) hesaba katılmaz.</param>
    public static List<SameFrameGroup> Find(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups,
        string? ignoreFolder = null)
    {
        // Birebir kopyalar zaten _Tekrarlar'a gider; onları ayrıca önermeye gerek yok.
        var copies = dupGroups.SelectMany(g => g.Copies).ToHashSet();
        var ignore = ignoreFolder is null ? null : Path.GetFullPath(ignoreFolder).TrimEnd('\\') + "\\";

        // Yalnızca fotoğraflar: RAW + JPG eşi aynı anı taşır ama iki ayrı dosyadır, videolar da kare değildir.
        var candidates = files.Where(f => f.Kind == FileKind.Photo && f.DateSource == DateSource.Exif
                                          && f.Taken is not null && !string.IsNullOrEmpty(f.Make)
                                          && !copies.Contains(f)
                                          && (ignore is null || !Path.GetFullPath(f.Path).StartsWith(ignore, StringComparison.OrdinalIgnoreCase)));

        var groups = new List<SameFrameGroup>();
        foreach (var bucket in candidates.GroupBy(f => (
                     Make: f.Make!.ToUpperInvariant(), Model: (f.Model ?? "").ToUpperInvariant(),
                     Moment: f.Taken!.Value.ToString("yyyyMMddHHmmss"))))
        {
            // Saniye kesri ya da gövde seri no iki dosyada da varsa ve farklıysa ayrı karedir (seri çekim, iki gövde).
            var clusters = new List<List<MediaFile>>();
            foreach (var f in bucket.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                var home = clusters.FirstOrDefault(c => c.All(o => Compatible(f, o)));
                if (home is null) clusters.Add(new List<MediaFile> { f });
                else home.Add(f);
            }
            foreach (var c in clusters.Where(c => c.Count > 1))
                groups.Add(new SameFrameGroup
                {
                    Members = c,
                    // Eşit çözünürlükte orijinal: ilk yazılan. Yeniden kaydedilen kopya genelde daha büyüktür, daha iyi değil.
                    Suggested = c.OrderByDescending(f => (long)(f.Width ?? 0) * (f.Height ?? 0))
                        .ThenBy(f => f.Modified).ThenByDescending(f => f.Size).First(),
                });
        }
        return groups.OrderBy(g => g.Members[0].Taken).ToList();
    }

    static bool Compatible(MediaFile a, MediaFile b) => Same(a.SubSec, b.SubSec) && Same(a.Serial, b.Serial);

    static bool Same(string? x, string? y) =>
        string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y) || x.Equals(y, StringComparison.OrdinalIgnoreCase);
}
