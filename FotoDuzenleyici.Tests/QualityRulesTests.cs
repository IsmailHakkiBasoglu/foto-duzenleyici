using FotoDuzenleyici.Core;
using Xunit;
using static FotoDuzenleyici.Core.QualityRules;

namespace FotoDuzenleyici.Tests;

/// <summary>Eşikler (gerçek arşivde kullanıcıyla seçildi, 2026-11-06) ve seri çekim sıralaması.</summary>
public class QualityRulesTests
{
    static QualityScore S(double sharp, double mean = 120, double dark = 0, bool flat = false) => new(sharp, mean, dark, flat);

    [Fact]
    public void Thresholds_AreTheCalibratedValues()
    {
        Assert.Equal(25, BlurThreshold);
        Assert.Equal(30, DarkMean);
        Assert.Equal(0.60, DarkShare);
        Assert.Equal(0.50, SoftShare);
        Assert.Equal(0.85, TieShare);
    }

    [Fact]
    public void Blurry_BelowThreshold()
    {
        Assert.True(IsBlurry(S(BlurThreshold - 1)));
        Assert.False(IsBlurry(S(BlurThreshold + 1)));
    }

    [Fact]
    public void Flat_IsNeverBlurry() => Assert.False(IsBlurry(S(0, flat: true)));

    [Fact]
    public void FlatDark_IsDarkOnly() =>
        Assert.Equal("KARANLIK", FlawText(S(0, mean: DarkMean - 1, dark: DarkShare + 0.01, flat: true)));

    [Fact]
    public void FlawText_BothAndNone()
    {
        Assert.Equal("BULANIK · KARANLIK", FlawText(S(1, mean: DarkMean - 1, dark: DarkShare + 0.01)));
        Assert.Equal("BULANIK", FlawText(S(1)));
        Assert.Null(FlawText(S(500)));
    }

    [Fact]
    public void Dark_NeedsBothConditions()
    {
        Assert.False(IsDark(S(500, DarkMean - 1, DarkShare - 0.01)));
        Assert.False(IsDark(S(500, DarkMean + 1, DarkShare + 0.01)));
        Assert.True(IsDark(S(500, DarkMean - 1, DarkShare + 0.01)));
    }

    [Fact]
    public void Rank_PercentOfSharpest()
    {
        var r = RankBurst(new QualityScore?[] { S(100), S(40), S(90) });
        Assert.Equal(new int?[] { 100, 40, 90 }, r.Select(x => x.Percent));
        Assert.Equal(new[] { true, false, false }, r.Select(x => x.Sharpest));
        Assert.Equal(new[] { false, true, false }, r.Select(x => x.Soft));
    }

    [Fact]
    public void Rank_AllClose_NoMarks() =>
        Assert.All(RankBurst(new QualityScore?[] { S(100), S(TieShare * 100 + 1) }), x => Assert.False(x.Sharpest || x.Soft));

    // Gözden geçirme odağı: ölçülemeyen kare sıralamayı bozmamalı.
    [Fact]
    public void Rank_Unmeasured_IgnoredAndUnmarked()
    {
        var r = RankBurst(new QualityScore?[] { null, S(100), S(30) });
        Assert.Equal(new BurstRank(null, false, false), r[0]);
        Assert.True(r[1].Sharpest);
        Assert.True(r[2].Soft);
    }

    [Fact]
    public void Rank_SingleMeasured_NoMarks() =>
        Assert.All(RankBurst(new QualityScore?[] { null, S(100) }), x => Assert.False(x.Sharpest || x.Soft));
}
