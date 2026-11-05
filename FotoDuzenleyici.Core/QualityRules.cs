namespace FotoDuzenleyici.Core;

/// <param name="Percent">Grubun en netine göre yüzde (en net = 100); ölçülemeyen karede null.</param>
/// <param name="Sharpest">◎: grubun en net karesi (kareler birbirine çok yakınsa hiçbiri).</param>
/// <param name="Soft">BULANIK: en netin <see cref="QualityRules.SoftShare"/> oranının altında.</param>
public readonly record struct BurstRank(int? Percent, bool Sharpest, bool Soft);

/// <summary>
/// Bulanık / karanlık kararları. Eşikler gerçek arşivde (4073 fotoğraf) ölçülüp kullanıcıyla birlikte seçildi
/// (2026-11-06): netlik 25'in altı arşivin en düşük ~%0,7'si; karanlık kuralı ~64 kare.
/// </summary>
public static class QualityRules
{
    public const double BlurThreshold = 25;
    public const double DarkMean = 30;
    public const double DarkShare = 0.60;
    public const double SoftShare = 0.50;
    public const double TieShare = 0.85;

    /// <summary>Düz sahne (ayrıntısız) netliği yargılanamaz: bulanık sayılmaz.</summary>
    public static bool IsBlurry(QualityScore s) => !s.Flat && s.Sharpness < BlurThreshold;

    public static bool IsDark(QualityScore s) => s.MeanLuma < DarkMean && s.DarkFraction > DarkShare;

    public static string? FlawText(QualityScore s) => (IsBlurry(s), IsDark(s)) switch
    {
        (true, true) => "BULANIK · KARANLIK",
        (true, false) => "BULANIK",
        (false, true) => "KARANLIK",
        _ => null,
    };

    /// <summary>Seri çekim grubunu kendi içinde kıyaslar; ölçülemeyen kare yüzde ve işaret almaz.</summary>
    public static List<BurstRank> RankBurst(IReadOnlyList<QualityScore?> scores)
    {
        var measured = scores.Where(s => s.HasValue).Select(s => s!.Value.Sharpness).ToList();
        if (measured.Count == 0) return scores.Select(_ => new BurstRank(null, false, false)).ToList();
        double max = measured.Max();
        bool marks = measured.Count >= 2 && max > 0 && measured.Any(v => v / max < TieShare);
        int sharpest = -1;
        for (int i = 0; i < scores.Count; i++)
            if (scores[i] is { } s && s.Sharpness == max) { sharpest = i; break; }

        return scores.Select((s, i) => s is not { } v
            ? new BurstRank(null, false, false)
            : new BurstRank(max > 0 ? (int)Math.Round(100 * v.Sharpness / max) : 0,
                marks && i == sharpest, marks && v.Sharpness / max < SoftShare)).ToList();
    }
}
