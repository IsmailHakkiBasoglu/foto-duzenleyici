using System.Windows.Media;
using System.Windows.Media.Imaging;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Arka planda küçük resim: eşzamanlılık sınırı, LRU önbellek, iptal.</summary>
public class ThumbnailLoaderTests
{
    static readonly ImageSource Pixel = MakePixel();

    static ImageSource MakePixel()
    {
        var b = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Gray8, null, new byte[] { 128 }, 1);
        b.Freeze();
        return b;
    }

    static SimilarRow Row(string name) => new()
    {
        Group = 1, Kind = SimilarKind.Burst,
        File = new MediaFile { Path = @"C:\f\" + name, Kind = FileKind.Photo },
    };

    static void WaitFor(Func<bool> cond) => Assert.True(SpinWait.SpinUntil(cond, TimeSpan.FromSeconds(5)));

    /// <summary>Çağrıları sayan sahte çözücü; istenirse bir semaforla bekletilir.</summary>
    sealed class FakeDecoder
    {
        readonly SemaphoreSlim? _gate;
        int _running;
        public FakeDecoder(SemaphoreSlim? gate = null) => _gate = gate;
        public List<string> Calls { get; } = new();
        public int MaxConcurrent;
        public ImageSource? Result = Pixel;

        public ImageSource? Decode(MediaFile f)
        {
            lock (Calls) Calls.Add(System.IO.Path.GetFileName(f.Path));
            var now = Interlocked.Increment(ref _running);
            lock (Calls) MaxConcurrent = Math.Max(MaxConcurrent, now);
            if (_gate is null) Thread.Sleep(20); else _gate.Wait();
            Interlocked.Decrement(ref _running);
            return Result;
        }

        public int CountOf(string name) { lock (Calls) return Calls.Count(c => c == name); }
    }

    [Fact]
    public void Request_SetsThumb_AndCaches()
    {
        var d = new FakeDecoder();
        var loader = new ThumbnailLoader(d.Decode);
        var a = Row("a.jpg");
        loader.Request(a);
        WaitFor(() => a.Thumb is not null);

        var again = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = a.File };
        loader.Request(again);
        Assert.NotNull(again.Thumb); // önbellekten hemen
        Assert.Equal(1, d.CountOf("a.jpg"));
        Assert.Equal(1, loader.CachedCount);
    }

    [Fact]
    public void NeverMoreThanParallelismAtOnce()
    {
        var d = new FakeDecoder();
        var loader = new ThumbnailLoader(d.Decode, parallelism: 4);
        var rows = Enumerable.Range(0, 10).Select(i => Row($"{i}.jpg")).ToList();
        foreach (var r in rows) loader.Request(r);
        WaitFor(() => rows.All(r => r.Thumb is not null));
        Assert.InRange(d.MaxConcurrent, 1, 4);
    }

    [Fact]
    public void Lru_EvictsOldest()
    {
        var d = new FakeDecoder();
        var loader = new ThumbnailLoader(d.Decode, capacity: 2, parallelism: 1);
        var a = Row("a.jpg"); var b = Row("b.jpg"); var c = Row("c.jpg");
        foreach (var r in new[] { a, b, c }) { loader.Request(r); WaitFor(() => r.Thumb is not null); }

        var c2 = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = c.File };
        loader.Request(c2);
        Assert.NotNull(c2.Thumb); // en yenisi önbellekte
        var a2 = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = a.File };
        loader.Request(a2);
        WaitFor(() => a2.Thumb is not null);
        Assert.Equal(1, d.CountOf("c.jpg"));
        Assert.Equal(2, d.CountOf("a.jpg"));
    }

    [Fact]
    public void Cancel_QueuedRequest_IsNeverDecoded()
    {
        using var gate = new SemaphoreSlim(0);
        var d = new FakeDecoder(gate);
        var loader = new ThumbnailLoader(d.Decode, parallelism: 1);
        var first = Row("first.jpg"); var second = Row("second.jpg");
        loader.Request(first);
        WaitFor(() => d.CountOf("first.jpg") == 1);
        loader.Request(second);
        loader.Cancel(second);
        gate.Release(5);
        WaitFor(() => first.Thumb is not null);
        Thread.Sleep(100);
        Assert.Equal(0, d.CountOf("second.jpg"));
        Assert.Null(second.Thumb);
    }

    // Gözden geçirmeden: yeniden taramada eski kuyruk boşaltılmalı; aksi hâlde eski dosyalar çözülmeye devam eder.
    [Fact]
    public void Clear_DropsQueuedRequests()
    {
        using var gate = new SemaphoreSlim(0);
        var d = new FakeDecoder(gate);
        var loader = new ThumbnailLoader(d.Decode, parallelism: 1);
        var first = Row("first.jpg");
        loader.Request(first);
        WaitFor(() => d.CountOf("first.jpg") == 1);
        var queued = Enumerable.Range(0, 5).Select(i => Row($"q{i}.jpg")).ToList();
        foreach (var r in queued) loader.Request(r);

        loader.Clear();
        gate.Release(10);
        Thread.Sleep(200);
        Assert.All(queued, r => Assert.Equal(0, d.CountOf(System.IO.Path.GetFileName(r.Path))));
        Assert.All(queued, r => Assert.Null(r.Thumb));
    }

    [Fact]
    public void Cancel_WhileDecoding_ThenRequestAgain_StillDelivers()
    {
        using var gate = new SemaphoreSlim(0);
        var d = new FakeDecoder(gate);
        var loader = new ThumbnailLoader(d.Decode, parallelism: 1);
        var a = Row("a.jpg");
        loader.Request(a);
        WaitFor(() => d.CountOf("a.jpg") == 1);
        loader.Cancel(a);
        var again = new SimilarRow { Group = 1, Kind = SimilarKind.Burst, File = a.File };
        loader.Request(again);
        gate.Release(5);
        WaitFor(() => again.Thumb is not null);
        Assert.Equal(1, d.CountOf("a.jpg"));
    }

    [Fact]
    public void DecodeReturnsNull_SetsThumbFailed_NoThrow()
    {
        var d = new FakeDecoder { Result = null };
        var loader = new ThumbnailLoader(d.Decode);
        var a = Row("bozuk.jpg");
        loader.Request(a);
        WaitFor(() => a.ThumbFailed);
        Assert.Null(a.Thumb);
    }
}
