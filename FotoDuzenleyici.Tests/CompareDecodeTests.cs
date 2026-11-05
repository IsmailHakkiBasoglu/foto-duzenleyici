using System.Windows.Media;
using System.Windows.Media.Imaging;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Karşılaştırma penceresindeki büyük çözmeler: eşzamanlılık sınırı, iptal, keskin görüntünün korunması.</summary>
public class CompareDecodeTests
{
    static ImageSource Pixel()
    {
        var b = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 128 }, 1);
        b.Freeze();
        return b;
    }

    static MediaFile Photo(string name) => new() { Path = @"C:\f\" + name, Kind = FileKind.Photo };

    // Gözden geçirmeden: 14 karelik grupta yakınlaşınca 14 tam çözme birden ~1,3 GB bellek istiyordu.
    [Fact]
    public async Task DecodeLimiter_NeverMoreThanLimitAtOnce()
    {
        var limiter = new DecodeLimiter(2);
        int running = 0, max = 0;
        var tasks = Enumerable.Range(0, 10).Select(_ => limiter.RunAsync(() =>
        {
            var now = Interlocked.Increment(ref running);
            lock (limiter) max = Math.Max(max, now);
            Thread.Sleep(30);
            Interlocked.Decrement(ref running);
            return Pixel();
        }, CancellationToken.None)).ToList();
        await Task.WhenAll(tasks);
        Assert.InRange(max, 1, 2);
    }

    [Fact]
    public async Task DecodeLimiter_CancelledBeforeStart_NeverRuns()
    {
        var limiter = new DecodeLimiter(1);
        using var gate = new SemaphoreSlim(0);
        var blocker = limiter.RunAsync(() => { gate.Wait(); return Pixel(); }, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        bool ran = false;
        var waiting = limiter.RunAsync(() => { ran = true; return Pixel(); }, cts.Token);
        cts.Cancel();
        gate.Release();
        await blocker;
        Assert.Null(await waiting);
        Assert.False(ran);
    }

    // Gözden geçirmeden: geç biten ekran boyutlu çözme, tam çözünürlüklünün üstüne yazıp kareyi bulanık bırakıyordu.
    [Fact]
    public void Tile_SmallerDecodeAfterFull_IsIgnored()
    {
        var t = new CompareTile { Row = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = Photo("a.jpg") }, Number = 1 };
        var full = Pixel(); var screen = Pixel();
        Assert.True(t.Offer(full, CompareTile.FullWidth));
        Assert.False(t.Offer(screen, 1200));
        Assert.Same(full, t.Image);
    }

    // Gerçek arşivde ölçüldü: 14 karelik grupta (7728×5152) yakınlaşınca bellek 4,5 GB'a çıktı.
    [Theory]
    [InlineData(2, 7728, 0)]      // az kare: tam çözünürlük
    [InlineData(4, 7728, 0)]
    [InlineData(5, 7728, 3200)]   // çok kare: sınırlı
    [InlineData(14, 7728, 3200)]
    [InlineData(14, 2000, 0)]     // zaten küçükse tam
    [InlineData(14, null, 3200)]  // boyutu bilinmiyorsa sınırlı
    public void FullDecodeWidth_CapsLargeGroups(int frames, int? fileWidth, int expected) =>
        Assert.Equal(expected, CompareTile.FullDecodeWidth(frames, fileWidth));

    [Fact]
    public void Tile_NullDecode_MarksFailedOnlyWhenNothingShown()
    {
        var t = new CompareTile { Row = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = Photo("a.jpg") }, Number = 1 };
        t.Offer(Pixel(), 1200);
        t.Offer(null, CompareTile.FullWidth);
        Assert.False(t.Failed);
        Assert.NotNull(t.Image);

        var empty = new CompareTile { Row = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = Photo("b.jpg") }, Number = 2 };
        empty.Offer(null, 1200);
        Assert.True(empty.Failed);
    }

    [Fact]
    public void Caption_ShowsSharpPercent_WhenMeasured()
    {
        var row = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = Photo("a.jpg"), SharpPercent = 42 };
        Assert.StartsWith("3 · Netlik %42  a.jpg", new CompareTile { Row = row, Number = 3 }.Caption);
        var plain = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = Photo("b.jpg") };
        Assert.StartsWith("3  b.jpg", new CompareTile { Row = plain, Number = 3 }.Caption);
    }
}
