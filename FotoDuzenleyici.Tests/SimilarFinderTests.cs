using System.Diagnostics;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class SimilarFinderTests
{
    const ulong Base = 0x0123_4567_89AB_CDEF;
    static readonly DateTime T0 = new(2024, 3, 10, 9, 15, 30);

    /// <summary>Base'den tam n bit farklı iz.</summary>
    static ulong Flip(int n, int shift = 0) => Base ^ (((1UL << n) - 1) << shift);

    static MediaFile Photo(string name, bool exif = true, DateTime? taken = null, string model = "EOS 250D",
        int w = 6000, int h = 4000, DateTime? modified = null) => new()
    {
        Path = @"C:\f\" + name, Kind = FileKind.Photo, Size = 1000,
        Taken = taken ?? T0, DateSource = exif ? DateSource.Exif : DateSource.FileTime,
        Make = exif ? "Canon" : null, Model = exif ? model : null,
        Width = w, Height = h, Modified = modified ?? T0,
    };

    static readonly byte[] Gray = Enumerable.Repeat((byte)128, 256).ToArray();

    /// <summary>İzler elle verilir; küçük resimler aynı (yalnızca izin etkisi sınanır).</summary>
    static List<SimilarGroup> Find(Dictionary<MediaFile, ulong> hashes, List<SameFrameGroup>? same = null) =>
        SimilarFinder.Find(hashes.Keys.ToList(), hashes.ToDictionary(kv => kv.Key, kv => new ImagePrint(kv.Value, Gray)),
            same ?? new List<SameFrameGroup>());

    [Fact]
    public void CopyEdge_RequiresSimilarThumbnail()
    {
        var a = Photo("a.jpg", exif: false); var b = Photo("b.jpg", exif: false);
        var dark = Enumerable.Repeat((byte)60, 256).ToArray();
        var prints = new Dictionary<MediaFile, ImagePrint> { [a] = new(Base, Gray), [b] = new(Flip(1), dark) };
        Assert.Empty(SimilarFinder.Find(new[] { a, b }, prints, new List<SameFrameGroup>()));
    }

    [Fact]
    public void FourBitsApart_IsResizedCopy_EvenWithoutExif()
    {
        var a = Photo("IMG_0001.JPG");
        var b = Photo("WhatsApp.jpeg", exif: false, taken: new DateTime(2025, 1, 1), w: 1600, h: 1067);
        var g = Assert.Single(Find(new() { [a] = Base, [b] = Flip(4) }));
        Assert.Equal(SimilarKind.ResizedCopy, g.Kind);
        Assert.Equal(2, g.Members.Count);
    }

    [Fact]
    public void FiveBitsApart_WithoutExif_IsNotGrouped() =>
        Assert.Empty(Find(new() { [Photo("a.jpg")] = Base, [Photo("b.jpg", exif: false)] = Flip(5) }));

    [Fact]
    public void TwelveBitsApart_SameCamera_WithinTenSeconds_IsBurst()
    {
        var g = Assert.Single(Find(new() { [Photo("1.jpg")] = Base, [Photo("2.jpg", taken: T0.AddSeconds(9))] = Flip(12) }));
        Assert.Equal(SimilarKind.Burst, g.Kind);
        Assert.Null(g.Suggested);
    }

    [Fact]
    public void NearlyIdentical_ButDifferentExifMoments_IsBurstNotCopy()
    {
        // Sabit sahnede seri çekim: görüntü neredeyse aynı ama kareler farklı; ★ önerisi almamalı.
        var g = Assert.Single(Find(new() { [Photo("1.jpg")] = Base, [Photo("2.jpg", taken: T0.AddSeconds(2))] = Flip(1) }));
        Assert.Equal(SimilarKind.Burst, g.Kind);
        Assert.Null(g.Suggested);
    }

    [Fact]
    public void SameSecondBurst_DifferentSubSec_IsBurstNotCopy()
    {
        // Telefon/DSLR seri çekimi: aynı saniyede 10 kare; saniye kesri farklı.
        var a = Photo("1.jpg"); a.SubSec = "10";
        var b = Photo("2.jpg"); b.SubSec = "45";
        var g = Assert.Single(Find(new() { [a] = Base, [b] = Flip(1) }));
        Assert.Equal(SimilarKind.Burst, g.Kind);
        Assert.Null(g.Suggested);
    }

    [Fact]
    public void SameMomentExport_StaysSameFrame_WithSuggestion()
    {
        // Orijinal + Lightroom dışa aktarımı: aynı kamera, aynı an, izleri yakın. Seri çekim değil, aynı kare.
        var orig = Photo("IMG_0300.JPG");
        var export = Photo("IMG_0300-duzenlenmis.jpg", w: 600, h: 400, modified: T0.AddDays(5));
        var same = new List<SameFrameGroup> { new() { Members = new() { orig, export }, Suggested = orig } };
        var g = Assert.Single(Find(new() { [orig] = Base, [export] = Flip(6) }, same));
        Assert.Equal(SimilarKind.SameFrame, g.Kind);
        Assert.Same(orig, g.Suggested);
    }

    [Fact]
    public void BurstJoinedByCopy_GetsNoSuggestion()
    {
        // A, B, C seri çekim; A' de A'nın WhatsApp kopyası. Ayrı kareler ★ yüzünden ayıklanmamalı.
        var a = Photo("A.jpg"); var b = Photo("B.jpg", taken: T0.AddSeconds(2)); var c = Photo("C.jpg", taken: T0.AddSeconds(4));
        var copy = Photo("A-whatsapp.jpg", exif: false, w: 1600, h: 1067);
        var g = Assert.Single(Find(new() { [a] = Base, [b] = Flip(8), [c] = Flip(9), [copy] = Flip(1) }));
        Assert.Equal(4, g.Members.Count);
        Assert.Equal(SimilarKind.Burst, g.Kind);
        Assert.Null(g.Suggested);
    }

    [Fact]
    public void NearlyIdentical_ExifMinutesApart_IsNotGrouped() =>
        Assert.Empty(Find(new() { [Photo("1.jpg")] = Base, [Photo("2.jpg", taken: T0.AddMinutes(5))] = Flip(1) }));

    [Fact]
    public void TwelveBitsApart_ElevenSecondsApart_IsNotGrouped() =>
        Assert.Empty(Find(new() { [Photo("1.jpg")] = Base, [Photo("2.jpg", taken: T0.AddSeconds(11))] = Flip(12) }));

    [Fact]
    public void TwelveBitsApart_DifferentCamera_IsNotGrouped() =>
        Assert.Empty(Find(new() { [Photo("1.jpg")] = Base, [Photo("2.jpg", model: "EOS R6", taken: T0.AddSeconds(2))] = Flip(12) }));

    [Fact]
    public void ChainedPairs_FormOneGroup()
    {
        var a = Photo("a.jpg", exif: false); var b = Photo("b.jpg", exif: false); var c = Photo("c.jpg", exif: false);
        var g = Assert.Single(Find(new() { [a] = Base, [b] = Flip(3), [c] = Flip(3) ^ (0b111UL << 3) }));
        Assert.Equal(3, g.Members.Count);
    }

    [Fact]
    public void SameFrameGroup_MergesWithCopyOfIt()
    {
        var a = Photo("IMG_0001.JPG"); var b = Photo("IMG_0001 (1).JPG");
        var c = Photo("WhatsApp.jpeg", exif: false, w: 1600, h: 1067);
        var same = new List<SameFrameGroup> { new() { Members = new() { a, b }, Suggested = a } };
        var g = Assert.Single(Find(new() { [a] = Base, [b] = Flip(20), [c] = Flip(2) }, same));
        Assert.Equal(SimilarKind.SameFrame, g.Kind);
        Assert.Equal(3, g.Members.Count);
    }

    [Fact]
    public void NoHashes_ReturnsSameFrameGroupsOnly()
    {
        var a = Photo("a.jpg"); var b = Photo("b.jpg", w: 2048, h: 1365);
        var same = new List<SameFrameGroup> { new() { Members = new() { a, b }, Suggested = a } };
        var g = Assert.Single(SimilarFinder.Find(new[] { a, b }, new Dictionary<MediaFile, ImagePrint>(), same));
        Assert.Equal(SimilarKind.SameFrame, g.Kind);
        Assert.Same(a, g.Suggested);
    }

    [Fact]
    public void ResizedCopy_SuggestsHighestResolutionThenExif()
    {
        var bigNoExif = Photo("buyuk.jpg", exif: false);
        var smallExif = Photo("kucuk.jpg", w: 1600, h: 1067);
        Assert.Same(bigNoExif, Assert.Single(Find(new() { [bigNoExif] = Base, [smallExif] = Flip(1) })).Suggested);

        var noExif = Photo("a.jpg", exif: false, w: 1600, h: 1067);
        var withExif = Photo("b.jpg", w: 1600, h: 1067, modified: T0.AddDays(3));
        Assert.Same(withExif, Assert.Single(Find(new() { [noExif] = Base, [withExif] = Flip(1) })).Suggested);
    }

    [Fact]
    public void TwentyThousandHashes_UnderThreeSeconds()
    {
        var rnd = new Random(42);
        var hashes = new Dictionary<MediaFile, ulong>();
        for (int i = 0; i < 20_000; i++)
            hashes[Photo($"{i}.jpg", taken: T0.AddMinutes(rnd.Next(0, 525_600)))] = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 63);
        for (int i = 0; i < 100; i++) // 100 seri çekim çifti
        {
            var t = new DateTime(2023, 1, 1).AddHours(i);
            hashes[Photo($"s{i}a.jpg", taken: t)] = (ulong)i * 0x9E37_79B9_7F4A_7C15;
            hashes[Photo($"s{i}b.jpg", taken: t.AddSeconds(3))] = ((ulong)i * 0x9E37_79B9_7F4A_7C15) ^ 0xFF;
        }
        var sw = Stopwatch.StartNew();
        var groups = Find(hashes);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"{sw.Elapsed}");
        Assert.True(groups.Count(g => g.Kind == SimilarKind.Burst) >= 100);
    }
}
