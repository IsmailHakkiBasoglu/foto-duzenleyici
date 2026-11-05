using System.Text.RegularExpressions;

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
        string? ignoreFolder = null) =>
        Find(files, dupGroups, ignoreFolder is null ? Array.Empty<string>() : new[] { ignoreFolder });

    /// <param name="ignoreFolders">Bu klasörlerdeki dosyalar (önceki turlarda ayrılan _Benzerler, _Kusurlu) hesaba katılmaz.</param>
    public static List<SameFrameGroup> Find(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups,
        IReadOnlyList<string> ignoreFolders)
    {
        // Birebir kopyalar zaten _Tekrarlar'a gider; onları ayrıca önermeye gerek yok.
        var copies = dupGroups.SelectMany(g => g.Copies).ToHashSet();
        var ignore = ignoreFolders.Select(f => Path.GetFullPath(f).TrimEnd('\\') + "\\").ToList();

        // Yalnızca fotoğraflar: RAW + JPG eşi aynı anı taşır ama iki ayrı dosyadır, videolar da kare değildir.
        var candidates = files.Where(f => f.Kind == FileKind.Photo && f.DateSource == DateSource.Exif
                                          && f.Taken is not null && !string.IsNullOrEmpty(f.Make)
                                          && !copies.Contains(f)
                                          && !ignore.Any(i => Path.GetFullPath(f.Path).StartsWith(i, StringComparison.OrdinalIgnoreCase)));

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

    static bool Compatible(MediaFile a, MediaFile b) =>
        Same(a.SubSec, b.SubSec) && Same(a.Serial, b.Serial) && !DifferentFrameNumbers(a, b);

    static bool Same(string? x, string? y) =>
        string.IsNullOrEmpty(x) || string.IsNullOrEmpty(y) || x.Equals(y, StringComparison.OrdinalIgnoreCase);

    // Kamera her çekimde dosya numarasını artırır (DSCF8746 → DSCF8747). Saniye kesri yazmayan kameralarda
    // (ör. Fuji X-H2) aynı saniyedeki ardışık kareleri ayırmanın tek güvenilir yolu bu.
    static readonly Regex OwnPrefix = new(@"^\d{4}-\d{2}-\d{2} (\d{6} )?", RegexOptions.Compiled);
    static readonly Regex FrameNo = new(@"(?<![A-Za-z0-9])(?<p>[A-Za-z0-9_]{3,5}?)(?<n>\d{4})(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    internal static bool DifferentFrameNumbers(MediaFile a, MediaFile b) =>
        CameraFrame(a) is { } x && CameraFrame(b) is { } y &&
        x.Prefix.Equals(y.Prefix, StringComparison.OrdinalIgnoreCase) && x.Number != y.Number;

    static (string Prefix, string Number)? CameraFrame(MediaFile f)
    {
        var stem = OwnPrefix.Replace(Path.GetFileNameWithoutExtension(f.Path), "");
        foreach (Match m in FrameNo.Matches(stem))
        {
            var p = m.Groups["p"].Value;
            if (p.Any(c => char.IsLetter(c) || c == '_')) return (p, m.Groups["n"].Value);
        }
        return null;
    }
}
