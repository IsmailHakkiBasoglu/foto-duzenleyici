using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Kalite önbelleği ve arşiv taraması.</summary>
public class QualityScanTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_qs_" + Guid.NewGuid().ToString("N"));
    readonly string _src, _cachePath;

    public QualityScanTests()
    {
        _src = Path.Combine(_tmp, "src");
        _cachePath = Path.Combine(_tmp, "onbellek", "kalite-onbellek.json");
        Directory.CreateDirectory(_src);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    sealed class Recorder(CancellationTokenSource? cancelOnFirst = null) : IProgress<(int done, int total, int fromCache)>
    {
        public (int done, int total, int fromCache) Last;
        public void Report((int done, int total, int fromCache) p)
        {
            lock (this) if (p.done >= Last.done) Last = p;
            cancelOnFirst?.Cancel();
        }
    }

    void MakePhotos(int count)
    {
        for (int i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(_src, $"IMG_{i:0000}.JPG"), TestImages.PatternJpeg(480, 320, seed: 100 + i));
    }

    static List<MediaFile> Scan(string root) => new Scanner(new ScanOptions { Roots = { root } }).Scan();

    (Dictionary<MediaFile, QualityScore> scores, Recorder rec, List<string> errors) Run(string root,
        CancellationToken ct = default, Recorder? rec = null)
    {
        rec ??= new Recorder();
        var errors = new List<string>();
        var r = QualityScan.Compute(Scan(root), new List<DuplicateGroup>(), Array.Empty<string>(), new QualityCache(_cachePath),
            errors, rec, ct);
        return (r, rec, errors);
    }

    [Fact]
    public void Cache_SaveLoad_RoundTrips()
    {
        var c = new QualityCache(_cachePath);
        var s = new QualityScore(12.5, 80.25, 0.125, true);
        c.Set("k", s);
        c.Save();
        Assert.True(new QualityCache(_cachePath).TryGet("k", out var back));
        Assert.Equal(s, back);
    }

    [Fact]
    public void Cache_CorruptFile_StartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath, "{bozuk");
        Assert.Equal(0, new QualityCache(_cachePath).Count);
    }

    [Fact]
    public void Scan_SkipsRawCopiesAndIgnoredFolders()
    {
        var bytes = TestImages.PatternJpeg(480, 320, seed: 7);
        string Write(string rel) { var p = Path.Combine(_tmp, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, bytes); return p; }
        MediaFile F(string path, FileKind kind) => new() { Path = path, Kind = kind };

        var photo = F(Write(@"src\IMG_0000.JPG"), FileKind.Photo);
        var raw = F(Write(@"src\IMG_0000.CR3"), FileKind.Raw);
        var keeper = F(Write(@"src\a.jpg"), FileKind.Photo);
        var copy = F(Write(@"src\a (1).jpg"), FileKind.Photo);
        var flawed = F(Write(@"out\_Kusurlu\2024-03\b.jpg"), FileKind.Photo);
        var similar = F(Write(@"out\_Benzerler\2024-03\c.jpg"), FileKind.Photo);
        var dups = new List<DuplicateGroup> { new() { Keeper = keeper, Copies = new() { copy } } };

        var r = QualityScan.Compute(new[] { photo, raw, keeper, copy, flawed, similar }, dups,
            new[] { Path.Combine(_tmp, "out", "_Benzerler"), Path.Combine(_tmp, "out", "_Kusurlu") },
            new QualityCache(_cachePath), new List<string>());
        Assert.Equal(new[] { "IMG_0000.JPG", "a.jpg" }, r.Keys.Select(k => k.FileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Scan_SecondRun_ComesFromCache()
    {
        MakePhotos(3);
        var first = Run(_src);
        Assert.Equal(0, first.rec.Last.fromCache);
        var second = Run(_src);
        Assert.Equal(3, second.rec.Last.fromCache);
        Assert.Equal(first.scores.Values.OrderBy(s => s.Sharpness), second.scores.Values.OrderBy(s => s.Sharpness));
    }

    [Fact]
    public void Scan_FileMovedAndRenamed_StillCached()
    {
        MakePhotos(1);
        Run(_src);
        var moved = Path.Combine(_tmp, "baska");
        Directory.CreateDirectory(moved);
        File.Move(Path.Combine(_src, "IMG_0000.JPG"), Path.Combine(moved, "2024-01-01 IMG_0000.JPG"));
        Assert.Equal(1, Run(moved).rec.Last.fromCache);
    }

    // Gözden geçirme odağı: yarıda durdurulunca ölçülenler kaybolmamalı.
    [Fact]
    public void Scan_Cancelled_SavesWhatWasMeasured()
    {
        MakePhotos(8);
        var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => Run(_src, cts.Token, new Recorder(cts)));
        Assert.True(new QualityCache(_cachePath).Count >= 1);
    }

    [Fact]
    public void Scan_UnreadableFile_GoesToErrors()
    {
        File.WriteAllBytes(Path.Combine(_src, "bozuk.jpg"), new byte[] { 1, 2, 3 });
        var (scores, _, errors) = Run(_src);
        Assert.Empty(scores);
        var e = Assert.Single(errors);
        Assert.StartsWith(Path.Combine(_src, "bozuk.jpg") + ": ", e);
    }

    // Gözden geçirmeden: özet, çok küçük görüntüleri gerçek hatalardan ayrı saymalı.
    [Fact]
    public void CountErrors_SeparatesTooSmallFromFailures()
    {
        File.WriteAllBytes(Path.Combine(_src, "kucuk.jpg"), TestImages.PatternJpeg(200, 150, seed: 3));
        File.WriteAllBytes(Path.Combine(_src, "bozuk.jpg"), new byte[] { 1, 2, 3 });
        var (_, _, errors) = Run(_src);
        Assert.Equal((1, 1), QualityScan.CountErrors(errors));
    }
}
