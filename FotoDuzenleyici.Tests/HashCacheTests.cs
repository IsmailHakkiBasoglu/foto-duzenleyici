using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class HashCacheTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cache_" + Guid.NewGuid().ToString("N"));
    readonly string _src, _cachePath;

    public HashCacheTests()
    {
        _src = Path.Combine(_tmp, "src");
        _cachePath = Path.Combine(_tmp, "onbellek", "benzerlik-onbellek.json");
        Directory.CreateDirectory(_src);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    /// <summary>İlerlemeyi eşzamanlı kaydeder; istenirse ilk bildirimde iptal eder.</summary>
    sealed class Recorder(CancellationTokenSource? cancelOnFirst = null) : IProgress<(int done, int total, int fromCache)>
    {
        public (int done, int total, int fromCache) Last;
        public void Report((int done, int total, int fromCache) p)
        {
            lock (this) if (p.done >= Last.done) Last = p;
            cancelOnFirst?.Cancel();
        }
    }

    void MakePhotos(int count, int size = 300)
    {
        for (int i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(_src, $"IMG_{i:0000}.JPG"), TestImages.PatternJpeg(size, size * 2 / 3, seed: 100 + i));
    }

    static List<MediaFile> Scan(string root) => new Scanner(new ScanOptions { Roots = { root } }).Scan();

    (Dictionary<MediaFile, ImagePrint> hashes, Recorder rec, List<string> errors) Run(string root, CancellationToken ct = default,
        Recorder? rec = null, string? cachePath = null)
    {
        rec ??= new Recorder();
        var errors = new List<string>();
        var r = SimilarityScan.Compute(Scan(root), new List<DuplicateGroup>(), null, new HashCache(cachePath ?? _cachePath),
            errors, rec, ct);
        return (r, rec, errors);
    }

    static Dictionary<string, ulong> ByName(Dictionary<MediaFile, ImagePrint> h) => h.ToDictionary(kv => kv.Key.FileName, kv => kv.Value.Hash);

    [Fact]
    public void SecondRun_ComesFromCache()
    {
        MakePhotos(3);
        var first = Run(_src);
        Assert.Equal(0, first.rec.Last.fromCache);
        var second = Run(_src);
        Assert.Equal(3, second.rec.Last.fromCache);
        Assert.Equal(ByName(first.hashes), ByName(second.hashes));
    }

    [Fact]
    public void ChangedFile_IsRecomputed()
    {
        MakePhotos(3);
        var first = Run(_src);
        var p = Path.Combine(_src, "IMG_0000.JPG");
        File.WriteAllBytes(p, TestImages.PatternJpeg(300, 200, seed: 999));
        File.SetLastWriteTime(p, DateTime.Now.AddHours(1));
        var second = Run(_src);
        Assert.Equal(2, second.rec.Last.fromCache);
        Assert.NotEqual(ByName(first.hashes)["IMG_0000.JPG"], ByName(second.hashes)["IMG_0000.JPG"]);
    }

    [Fact]
    public void MovedAndRenamed_StillHitsCache()
    {
        MakePhotos(3);
        Run(_src);
        var moved = Path.Combine(_tmp, "duzenli", "2024-03");
        Directory.CreateDirectory(moved);
        foreach (var f in Directory.GetFiles(_src))
            File.Move(f, Path.Combine(moved, "2024-03-10 " + Path.GetFileName(f)));
        Assert.Equal(3, Run(moved).rec.Last.fromCache);
    }

    [Fact]
    public void CorruptCacheFile_IsIgnored()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath, "{bozuk");
        Assert.Equal(0, new HashCache(_cachePath).Count);
    }

    [Fact]
    public void Cancel_SavesPartialProgress()
    {
        MakePhotos(12);
        var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => Run(_src, cts.Token, new Recorder(cts)));
        Assert.True(new HashCache(_cachePath).Count >= 1);
    }

    [Fact]
    public void LockedFile_IsReportedAndOthersHashed()
    {
        MakePhotos(3);
        var locked = Path.Combine(_src, "IMG_0001.JPG");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var (hashes, _, errors) = Run(_src);
            Assert.Equal(2, hashes.Count);
            Assert.Contains(errors, e => e.Contains("IMG_0001.JPG"));
        }
    }

    [Fact]
    public void ParallelWrites_LoseNothing()
    {
        MakePhotos(50, size: 120);
        Assert.Equal(50, Run(_src).hashes.Count);
        Assert.Equal(50, new HashCache(_cachePath).Count);
    }

    [Fact]
    public void ExcludesRawVideoDupCopiesAndSimilarFolder()
    {
        MakePhotos(1);
        var bytes = TestImages.PatternJpeg(200, 150, seed: 7);
        string Write(string rel) { var p = Path.Combine(_tmp, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, bytes); return p; }
        MediaFile F(string path, FileKind kind) => new() { Path = path, Kind = kind };

        var photo = F(Path.Combine(_src, "IMG_0000.JPG"), FileKind.Photo);
        var raw = F(Write(@"src\IMG_0000.CR3"), FileKind.Raw);
        var video = F(Write(@"src\MVI_0001.MP4"), FileKind.Video);
        var keeper = F(Write(@"src\a.jpg"), FileKind.Photo);
        var copy = F(Write(@"src\a (1).jpg"), FileKind.Photo);
        var parked = F(Write(@"out\_Benzerler\2024-03\b.jpg"), FileKind.Photo);
        var dups = new List<DuplicateGroup> { new() { Keeper = keeper, Copies = new() { copy } } };

        var r = SimilarityScan.Compute(new[] { photo, raw, video, keeper, copy, parked }, dups,
            Path.Combine(_tmp, "out", "_Benzerler"), new HashCache(_cachePath), new List<string>());
        Assert.Equal(new[] { "IMG_0000.JPG", "a.jpg" }, r.Keys.Select(k => k.FileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void UnwritableCache_ScanStillReturnsHashes()
    {
        MakePhotos(3);
        var dirAsCache = Path.Combine(_tmp, "klasor");
        Directory.CreateDirectory(dirAsCache); // önbellek yolu bir klasörü gösteriyor: yazılamaz
        var (hashes, _, errors) = Run(_src, cachePath: dirAsCache);
        Assert.Equal(3, hashes.Count);
        Assert.Contains(errors, e => e.StartsWith("Önbellek kaydedilemedi"));
    }
}
