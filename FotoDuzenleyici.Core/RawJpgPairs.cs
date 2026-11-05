using System.Text.RegularExpressions;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Aynı klasörde aynı adı taşıyan RAW ve fotoğraf (JPG/HEIC) dosyaları aynı karedir:
/// tarihleri ve adları eş tutulur, istenirse birlikte ayıklanır.
/// </summary>
public static class RawJpgPairs
{
    // Uygulamanın "RAW ayrı klasör" seçeneğiyle açtığı klasör: 2026-01\2026-01-Raw (altında kaynak klasörü olabilir).
    static readonly Regex RawFolder = new(@"\\(\d{4}-\d{2})-Raw(?=\\|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Eş anahtarı: klasör + ad. Düzenli arşivde -Raw klasörü kardeşi -Foto klasörüyle aynı sayılır.</summary>
    static string Key(MediaFile f)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(f.Path))!;
        if (f.Kind == FileKind.Raw) dir = RawFolder.Replace(dir, @"\$1-Foto");
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(f.Path));
    }

    /// <summary>En az bir RAW ve bir fotoğraf içeren gruplar; her grupta önce fotoğraflar, sonra yola göre.</summary>
    public static List<List<MediaFile>> Groups(IEnumerable<MediaFile> files) =>
        files.Where(f => f.Kind is FileKind.Photo or FileKind.Raw)
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Any(f => f.Kind == FileKind.Raw) && g.Any(f => f.Kind == FileKind.Photo))
            .Select(g => g.OrderBy(f => f.Kind == FileKind.Raw).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList())
            .ToList();

    /// <summary>Her eş üyesinden kendi grubuna.</summary>
    public static Dictionary<MediaFile, List<MediaFile>> Index(IEnumerable<MediaFile> files)
    {
        var map = new Dictionary<MediaFile, List<MediaFile>>();
        foreach (var g in Groups(files))
            foreach (var f in g) map[f] = g;
        return map;
    }

    /// <summary>
    /// Ayıklanan fotoğrafların henüz ayıklanmamış RAW eşleri, ayıklanan eşin grupta kalan karesiyle birlikte.
    /// Kullanıcı "birlikte" derse bunlar ayıklama haritasına eklenir.
    /// </summary>
    public static List<(MediaFile Raw, MediaFile Keeper)> UnreleasedRaws(IEnumerable<MediaFile> files,
        IReadOnlyDictionary<MediaFile, MediaFile> similarOf)
    {
        var result = new List<(MediaFile, MediaFile)>();
        var seen = new HashSet<MediaFile>();
        foreach (var g in Groups(files))
        {
            var released = g.FirstOrDefault(f => f.Kind == FileKind.Photo && similarOf.ContainsKey(f));
            if (released is null) continue;
            foreach (var raw in g.Where(f => f.Kind == FileKind.Raw && !similarOf.ContainsKey(f) && seen.Add(f)))
                result.Add((raw, similarOf[released]));
        }
        return result;
    }
}
