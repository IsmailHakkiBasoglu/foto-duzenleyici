using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class ImageHasherTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_hash_" + Guid.NewGuid().ToString("N"));
    public ImageHasherTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Put(string name, byte[] bytes)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    ulong Hash(string path, int orientation = 1)
    {
        var h = ImageHasher.DHash(path, orientation, out var error);
        Assert.True(h.HasValue, error);
        return h!.Value;
    }

    [Fact]
    public void ResizedRecompressedCopy_IsWithinFourBits()
    {
        var a = Put("orijinal.jpg", TestImages.PatternJpeg(1200, 800, seed: 1, quality: 95));
        var b = Put("whatsapp.jpg", TestImages.PatternJpeg(400, 267, seed: 1, quality: 60));
        Assert.InRange(ImageHasher.Distance(Hash(a), Hash(b)), 0, 4);
    }

    [Fact]
    public void ExifOrientation6_MatchesPhysicallyRotatedImage()
    {
        var a = Put("etiketli.jpg", TestImages.PatternJpeg(1200, 800, 2, orientation: 6));
        var b = Put("dondurulmus.jpg", TestImages.PatternJpeg(1200, 800, 2, rotate90: true));
        Assert.InRange(ImageHasher.Distance(Hash(a, 6), Hash(b)), 0, 4);
        Assert.True(ImageHasher.Distance(Hash(a, 1), Hash(b)) > 12); // yön uygulanmazsa eşleşmez
    }

    [Fact]
    public void DifferentPattern_IsFarApart()
    {
        var a = Put("a.jpg", TestImages.PatternJpeg(800, 600, 1));
        var b = Put("b.jpg", TestImages.PatternJpeg(800, 600, 3));
        Assert.True(ImageHasher.Distance(Hash(a), Hash(b)) > 12);
    }

    [Fact]
    public void CorruptFile_ReturnsNullWithError()
    {
        var bytes = new byte[1024];
        new Random(7).NextBytes(bytes);
        var p = Put("bozuk.jpg", bytes);
        Assert.Null(ImageHasher.DHash(p, 1, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void SolidImage_ReturnsNull_AsFlat()
    {
        var p = Put("siyah.jpg", TestImages.SolidJpeg(0, 0, 0));
        Assert.Null(ImageHasher.DHash(p, 1, out var error));
        Assert.Equal("düz görüntü", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TinyAndTransparentPng_DoNotThrow(bool transparent)
    {
        var p = Put($"kucuk{transparent}.png", TestImages.TinyPng(transparent));
        var ex = Record.Exception(() => ImageHasher.DHash(p, 1, out _));
        Assert.Null(ex);
    }

    [Fact]
    public void Decode_MissingFile_ReturnsNull() =>
        Assert.Null(ImageHasher.Decode(Path.Combine(_tmp, "yok.jpg"), 1, 1000));

    [Fact]
    public void Scanner_ReadsOrientation()
    {
        var tagged = Put("etiketli.jpg", TestImages.PatternJpeg(300, 200, 4, orientation: 6));
        var plain = Put("duz.jpg", TestImages.PatternJpeg(300, 200, 4));
        Assert.Equal(6, Scanner.ReadFile(tagged).Orientation);
        Assert.Equal(1, Scanner.ReadFile(plain).Orientation);
    }
}
