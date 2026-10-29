namespace FotoDuzenleyici.Core;

public enum SimilarKind
{
    /// <summary>Aynı kamera ve aynı çekim anı (EXIF parmak izi).</summary>
    SameFrame,
    /// <summary>Görüntüsü neredeyse aynı: küçültülmüş / yeniden sıkıştırılmış kopya (ör. WhatsApp).</summary>
    ResizedCopy,
    /// <summary>Saniyeler içinde çekilmiş çok benzer kareler.</summary>
    Burst,
}

public sealed class SimilarGroup
{
    public required List<MediaFile> Members { get; init; }
    public required SimilarKind Kind { get; init; }
    /// <summary>Kalması önerilen; seri çekimde null (en iyi kareyi kullanıcı seçer).</summary>
    public MediaFile? Suggested { get; init; }
}

public sealed class SimilarOptions
{
    public int CopyMaxDistance { get; init; } = 4;
    public int BurstMaxDistance { get; init; } = 12;
    public TimeSpan BurstWindow { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Benzerlik izlerinden ve aynı kare gruplarından türlü gruplar kurar. A~B ve B~C ise {A,B,C} tek gruptur.
/// Hiçbir dosyaya dokunmaz; hangisinin kalacağına kullanıcı karar verir.
/// </summary>
public static class SimilarFinder
{
    public static List<SimilarGroup> Find(IReadOnlyList<MediaFile> files, IReadOnlyDictionary<MediaFile, ulong> hashes,
        IReadOnlyList<SameFrameGroup> sameFrame, SimilarOptions? options = null)
    {
        var opt = options ?? new SimilarOptions();
        var index = new Dictionary<MediaFile, int>();
        var nodes = new List<MediaFile>();
        int Id(MediaFile f)
        {
            if (!index.TryGetValue(f, out var i)) { i = nodes.Count; index[f] = i; nodes.Add(f); }
            return i;
        }
        var edges = new List<(int a, int b, SimilarKind kind)>();

        foreach (var g in sameFrame)
            foreach (var m in g.Members.Skip(1)) edges.Add((Id(g.Members[0]), Id(m), SimilarKind.SameFrame));

        var hashed = hashes.Keys.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var seen = new HashSet<(MediaFile, MediaFile)>();

        // Kopya: uzaklık ≤ 4 ise 5 parçadan en az biri birebir eşittir; yalnızca ortak parçası olanlar karşılaştırılır.
        var buckets = new Dictionary<(int part, ulong value), List<MediaFile>>();
        foreach (var f in hashed)
            for (int p = 0; p < 5; p++)
            {
                int shift = p * 13, bits = p == 4 ? 12 : 13;
                var key = (p, (hashes[f] >> shift) & ((1UL << bits) - 1));
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<MediaFile>();
                list.Add(f);
            }
        foreach (var list in buckets.Values)
            for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (seen.Add((list[i], list[j])) &&
                    ImageHasher.Distance(hashes[list[i]], hashes[list[j]]) <= opt.CopyMaxDistance)
                    edges.Add((Id(list[i]), Id(list[j]), SimilarKind.ResizedCopy));

        // Seri çekim: aynı kamera, EXIF tarihli, aradaki süre pencereden kısa.
        var dated = hashed.Where(f => f.DateSource == DateSource.Exif && f.Taken is not null && !string.IsNullOrEmpty(f.Make))
            .OrderBy(f => f.Taken).ToList();
        for (int i = 0; i < dated.Count; i++)
            for (int j = i + 1; j < dated.Count && dated[j].Taken!.Value - dated[i].Taken!.Value < opt.BurstWindow; j++)
            {
                var (a, b) = (dated[i], dated[j]);
                if (SameCamera(a, b) && ImageHasher.Distance(hashes[a], hashes[b]) <= opt.BurstMaxDistance)
                    edges.Add((Id(a), Id(b), SimilarKind.Burst));
            }

        // Birleşim-bul
        var parent = Enumerable.Range(0, nodes.Count).ToArray();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var (a, b, _) in edges) parent[Root(a)] = Root(b);

        var kindOf = new Dictionary<int, SimilarKind>();
        foreach (var (a, _, kind) in edges)
        {
            var r = Root(a);
            if (!kindOf.TryGetValue(r, out var k) || kind < k) kindOf[r] = kind; // SameFrame > ResizedCopy > Burst
        }

        return Enumerable.Range(0, nodes.Count).GroupBy(Root)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var members = g.Select(i => nodes[i]).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
                var kind = kindOf[g.Key];
                return new SimilarGroup { Members = members, Kind = kind, Suggested = Suggest(members, kind) };
            })
            .OrderBy(g => g.Members.Min(m => m.Taken ?? DateTime.MaxValue))
            .ToList();
    }

    static bool SameCamera(MediaFile a, MediaFile b) =>
        string.Equals(a.Make, b.Make, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.Model ?? "", b.Model ?? "", StringComparison.OrdinalIgnoreCase);

    static long Pixels(MediaFile f) => (long)(f.Width ?? 0) * (f.Height ?? 0);

    static MediaFile? Suggest(List<MediaFile> m, SimilarKind kind) => kind switch
    {
        // Aynı kare: eşit çözünürlükte orijinal (ilk yazılan); yeniden kaydedilen genelde daha büyüktür, daha iyi değil.
        SimilarKind.SameFrame => m.OrderByDescending(Pixels).ThenBy(f => f.Modified).ThenByDescending(f => f.Size).First(),
        // Küçültülmüş kopya: büyük olan; eşitlikte EXIF'i duran (WhatsApp EXIF'i siler).
        SimilarKind.ResizedCopy => m.OrderByDescending(Pixels).ThenByDescending(f => f.DateSource == DateSource.Exif)
            .ThenBy(f => f.Modified).First(),
        // Seri çekim: en iyi kareyi netlik ölçmeden bilemeyiz.
        _ => null,
    };
}
