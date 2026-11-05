using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Netlik (parça parça Laplace varyansı) ve karanlık ölçümü.</summary>
public class QualityMeterTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_q_" + Guid.NewGuid().ToString("N"));
    public QualityMeterTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Write(byte[] bytes)
    {
        var p = Path.Combine(_tmp, Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    QualityScore M(byte[] bytes)
    {
        var s = QualityMeter.Measure(Write(bytes), 1, out var error);
        Assert.True(s.HasValue, error);
        return s!.Value;
    }

    static byte[] Pattern(int w, int h, int seed) => TestImages.PatternJpeg(w, h, seed);

    [Fact]
    public void Sharp_ScoresHigherThan_SameImageBlurred() =>
        Assert.True(M(Pattern(1600, 1200, 1)).Sharpness > 4 * M(TestImages.BlurredPatternJpeg(1600, 1200, 1, 6)).Sharpness);

    [Fact]
    public void HalfBlurred_ScoresFarAboveFullyBlurred()
    {
        // Arkası flu portre: en net parçalar keskin yarıda; bulanık kareden çok uzak olmalı.
        var half = M(TestImages.HalfBlurredPatternJpeg(1600, 1200, 1, 6)).Sharpness;
        var blurred = M(TestImages.BlurredPatternJpeg(1600, 1200, 1, 6)).Sharpness;
        Assert.True(half > 10 * blurred, $"{half} vs {blurred}");
    }

    [Fact]
    public void Dark_HasLowMeanAndHighDarkFraction()
    {
        var s = M(TestImages.DarkPatternJpeg(1600, 1200, 1, 0.08));
        Assert.True(s.MeanLuma < 20, $"mean {s.MeanLuma}");
        Assert.True(s.DarkFraction > 0.9, $"dark {s.DarkFraction}");
    }

    [Fact]
    public void SoftSky_IsFlat() => Assert.True(M(TestImages.GradientJpeg(140, 170)).Flat);

    [Fact]
    public void Pattern_IsNotFlat() => Assert.False(M(Pattern(1600, 1200, 1)).Flat);

    // Gözden geçirme odağı: küçük görüntü büyütülerek çözülürse yapay bulanıklık çıkar.
    [Fact]
    public void SmallImage_NotUpscaled_ScoresLikeLargeSharp() =>
        Assert.True(M(Pattern(800, 600, 1)).Sharpness > 0.5 * M(Pattern(4000, 3000, 1)).Sharpness);

    [Fact]
    public void TinyImage_ReturnsNull_WithReason()
    {
        Assert.Null(QualityMeter.Measure(Write(Pattern(200, 150, 1)), 1, out var e));
        Assert.Equal("çok küçük", e);
    }

    [Fact]
    public void CorruptFile_ReturnsNull()
    {
        Assert.Null(QualityMeter.Measure(Write(new byte[] { 1, 2, 3 }), 1, out var e));
        Assert.NotNull(e);
    }

    [Fact]
    public void Orientation_DoesNotChangeScore()
    {
        var p = Write(TestImages.PatternJpeg(1600, 1200, 1));
        var a = QualityMeter.Measure(p, 1, out _)!.Value.Sharpness;
        var b = QualityMeter.Measure(p, 6, out _)!.Value.Sharpness;
        Assert.InRange(b / a, 0.9, 1.1);
    }
}
