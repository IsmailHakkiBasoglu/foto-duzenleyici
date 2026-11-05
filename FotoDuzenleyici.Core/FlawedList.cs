namespace FotoDuzenleyici.Core;

/// <summary>
/// Kusurlu sekmesi: tek başına bulanık ya da karanlık kareler. Her kare tek kareli bir gruptur; böylece şerit kartı,
/// küçük resim yükleyici, karar kaydı ve karşılaştırma penceresi olduğu gibi kullanılır.
/// </summary>
public static class FlawedList
{
    /// <param name="similarGroups">Bu gruplardaki kareler listeye girmez (onları Benzerler sekmesi ele alır).</param>
    /// <param name="firstNumber">İlk grubun numarası (Benzerler'in numaralarıyla çakışmasın).</param>
    public static List<SimilarGroupState> Build(IReadOnlyDictionary<MediaFile, QualityScore> scores,
        IEnumerable<SimilarGroupState> similarGroups, int firstNumber)
    {
        var inSimilar = similarGroups.SelectMany(g => g.Rows).Select(r => r.File).ToHashSet();
        return scores
            .Where(kv => !inSimilar.Contains(kv.Key) && QualityRules.FlawText(kv.Value) is not null)
            .OrderBy(kv => kv.Key.Taken ?? DateTime.MaxValue).ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .Select((kv, i) =>
            {
                var row = new SimilarRow
                {
                    Group = firstNumber + i, File = kv.Key, Kind = SimilarKind.Flawed, Flaw = QualityRules.FlawText(kv.Value),
                };
                return new SimilarGroupState(firstNumber + i, SimilarKind.Flawed, new[] { row }, allowEmpty: true);
            })
            .ToList();
    }
}
