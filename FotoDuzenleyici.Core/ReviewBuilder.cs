namespace FotoDuzenleyici.Core;

/// <summary>
/// Benzerler ve Kusurlu incelemelerinin grupları ve kararların plana çevrilmesi. Klasik pencere ile yeni arayüz
/// aynı mantığı kullanır. Dosyalara dokunmaz; olay bağlamaz (bunu çağıran yapar).
/// </summary>
public static class ReviewBuilder
{
    /// <summary>Önceki turlarda ayrılanlar (_Benzerler, _Kusurlu) ne benzerlik aramasına ne ölçüme girer.</summary>
    public static IReadOnlyList<string> IgnoreDirs(PlanOptions opt)
    {
        var dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(opt.DestinationRoot));
        return new[] { Path.Combine(dest, opt.SimilarFolderName), Path.Combine(dest, opt.FlawedFolderName) };
    }

    /// <summary>Benzer grupları kurar; RAW rozeti, karar anahtarı, seri çekim netlik sırası ve kayıtlı kararlar uygulanır.</summary>
    public static List<SimilarGroupState> BuildSimilar(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dups,
        IReadOnlyDictionary<MediaFile, ImagePrint> prints, IReadOnlyList<string> ignoreDirs,
        IReadOnlyDictionary<MediaFile, QualityScore> scores, ReviewStore store)
    {
        var groups = SimilarFinder.Find(files, prints, SameFrameFinder.Find(files, dups, ignoreDirs));
        var states = SimilarGroupState.FromGroups(groups);
        var pairs = RawJpgPairs.Index(files);
        foreach (var r in states.SelectMany(g => g.Rows))
        {
            r.HasRaw = r.File.Kind == FileKind.Photo && pairs.ContainsKey(r.File);
            r.StoreKey = KeyOf(r.Path);
        }
        foreach (var g in states)
        {
            g.ApplyBurstRanks(scores);
            g.Restore(store);
        }
        return states;
    }

    /// <summary>Kusurlu kareler (Benzerler'deki kareler hariç); numaralar benzer gruplarından sonra başlar.</summary>
    public static List<SimilarGroupState> BuildFlawed(IReadOnlyList<MediaFile> files,
        IReadOnlyDictionary<MediaFile, QualityScore> scores, IReadOnlyList<SimilarGroupState> similar, ReviewStore store)
    {
        var list = FlawedList.Build(scores, similar, similar.Count + 1);
        var pairs = RawJpgPairs.Index(files);
        foreach (var g in list)
        {
            var r = g.Rows[0];
            r.HasRaw = pairs.ContainsKey(r.File);
            r.StoreKey = KeyOf(r.Path);
            g.Restore(store);
        }
        return list;
    }

    /// <summary>Kalsın işareti kaldırılan dosya → grubunda kalan dosya (önerilen işaretliyse o).</summary>
    public static Dictionary<MediaFile, MediaFile> SimilarMap(IEnumerable<SimilarGroupState> similar)
    {
        var map = new Dictionary<MediaFile, MediaFile>();
        foreach (var g in similar)
        {
            var keeper = g.Rows.FirstOrDefault(r => r.Keep && r.Suggested) ?? g.Rows.FirstOrDefault(r => r.Keep);
            if (keeper is null) continue;
            foreach (var r in g.Rows.Where(r => !r.Keep)) map[r.File] = keeper.File;
        }
        return map;
    }

    public static HashSet<MediaFile> FlawedSet(IEnumerable<SimilarGroupState> flawed) =>
        flawed.SelectMany(g => g.Rows).Where(r => !r.Keep).Select(r => r.File).ToHashSet();

    /// <summary>Ayıklanan karelerin henüz ayıklanmamış RAW eşleri (kullanıcıya bir kez sorulur).</summary>
    public static List<(MediaFile Raw, MediaFile Photo)> PendingRaws(IReadOnlyList<MediaFile> files,
        IReadOnlyDictionary<MediaFile, MediaFile> similarOf, IReadOnlySet<MediaFile> flawed) =>
        RawJpgPairs.UnreleasedRaws(files, similarOf.Keys.Concat(flawed).ToHashSet());

    /// <summary>"RAW'lar da ayrılsın": eşi benzerde ayıklandıysa aynı keeper'a, değilse Kusurlu'ya.</summary>
    public static void IncludeRaws(IEnumerable<(MediaFile Raw, MediaFile Photo)> raws,
        Dictionary<MediaFile, MediaFile> similarOf, HashSet<MediaFile> flawed)
    {
        foreach (var (raw, photo) in raws)
            if (similarOf.TryGetValue(photo, out var keeper)) similarOf[raw] = keeper;
            else flawed.Add(raw);
    }

    public const string RawQuestionTitle = "RAW eşleri";

    /// <summary>Evet: RAW'lar da ayrılsın · Hayır: RAW'lar normal klasöre · İptal: plan kurulmasın.</summary>
    public static string RawQuestion(int count) =>
        $"Benzerler'de ve Kusurlu'da ayıkladığın {count} karenin RAW eşi var.\n\n" +
        "Evet: RAW'lar da JPG'leriyle birlikte taşınsın (_Benzerler / _Kusurlu).\n" +
        "Hayır: RAW'lar normal klasörlerine gitsin, yalnız JPG'ler ayıklansın.\n" +
        "İptal: plan oluşturulmasın.";

    static string? KeyOf(string path)
    {
        try { return HashCache.Key(path); } catch (Exception) { return null; }
    }
}
