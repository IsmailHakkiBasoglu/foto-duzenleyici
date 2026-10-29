using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>
/// Uçtan uca: gerçek gibi fotoğraf ve videolarla Tara → Plan → Uygula (kaynak Geri Dönüşüm Kutusu'na) →
/// tekrar çalıştır → Geri Al. Geri Dönüşüm Kutusu yerine geçici bir klasör kullanılır.
/// </summary>
public class IntegrationTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_int_" + Guid.NewGuid().ToString("N"));
    readonly string _src, _dest, _bin;

    public IntegrationTests()
    {
        _src = Path.Combine(_tmp, "kaynak");
        _dest = Path.Combine(_tmp, "duzenli");
        _bin = Path.Combine(_tmp, "_SahteCop");
        Directory.CreateDirectory(_bin);
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    /// <summary>Geri Dönüşüm Kutusu yerine: dosyayı benzersiz adla sahte çöpe taşır.</summary>
    void FakeRecycle(string path) =>
        File.Move(path, Path.Combine(_bin, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(path)));

    sealed record Original(string Path, byte[] Bytes, DateTime Created, DateTime Modified);

    Original Put(string rel, byte[] bytes, DateTime created, DateTime modified)
    {
        var p = Path.Combine(_src, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
        File.SetCreationTime(p, created);
        File.SetLastWriteTime(p, modified);
        return new Original(p, bytes, created, modified);
    }

    static (List<MediaFile>, List<DuplicateGroup>) Scan(string root)
    {
        var files = new Scanner(new ScanOptions { Roots = { root } }).Scan();
        return (files, DuplicateFinder.Find(files));
    }

    static PlanOptions Options(string dest) => new() { DestinationRoot = dest };

    [Fact]
    public void GeneratedMedia_IsReadLikeRealCameraFiles()
    {
        var jpg = Put("IMG_0001.JPG", TestMedia.Jpeg("Canon", new DateTime(2024, 3, 10, 9, 0, 0)), DateTime.Now, DateTime.Now);
        var utc = new DateTime(2024, 5, 31, 22, 30, 0);
        var mp4 = Put("MVI_0001.MP4", TestMedia.Mp4(utc), DateTime.Now, DateTime.Now);

        var p = Scanner.ReadFile(jpg.Path);
        Assert.Equal(DateSource.Exif, p.DateSource);
        Assert.Equal(new DateTime(2024, 3, 10, 9, 0, 0), p.Taken);
        Assert.Equal("Canon", p.Source);
        Assert.True(p.SourceFromExif);

        var v = Scanner.ReadFile(mp4.Path);
        Assert.Equal(DateSource.VideoMetadata, v.DateSource);
        Assert.Equal(Scanner.UtcToLocal(utc), v.Taken);
    }

    [Fact]
    public void DistinctLowDetailImages_AreNotGroupedAsCopies()
    {
        // Arayüz denemesinde 2000 alakasız desen 35 sahte "kopya" grubu vermişti (iz çakışması + zincirleme).
        var t = new DateTime(2024, 3, 10);
        for (int i = 0; i < 300; i++) Put($"IMG_{i:0000}.JPG", TestImages.PatternJpeg(400, 300, 5000 + i, 85), t, t);
        var (files, dups) = Scan(_src);
        var prints = SimilarityScan.Compute(files, dups, null, new HashCache(Path.Combine(_tmp, "c.json")), new List<string>());
        Assert.Empty(SimilarFinder.Find(files, prints, SameFrameFinder.Find(files, dups)));
    }

    [Fact]
    public void ResizedCopy_EndToEnd_ToSimilarFolder_AndUndo()
    {
        var t = new DateTime(2024, 3, 10, 12, 0, 0);
        var big = Put("IMG_0001.JPG", TestImages.PatternJpeg(1200, 800, 5, 95), t, t);
        var small = Put("WhatsApp Image 2024-03-10.jpeg", TestImages.PatternJpeg(400, 267, 5, 60), t, t);
        var cachePath = Path.Combine(_tmp, "onbellek.json");

        var (files, dups) = Scan(_src);
        var hashes = SimilarityScan.Compute(files, dups, null, new HashCache(cachePath), new List<string>());
        var g = Assert.Single(SimilarFinder.Find(files, hashes, SameFrameFinder.Find(files, dups)));
        Assert.Equal(SimilarKind.ResizedCopy, g.Kind);
        Assert.Equal(big.Path, g.Suggested!.Path);

        var other = g.Members.Single(m => m != g.Suggested);
        var plan = Planner.Build(files, dups, Options(_dest), new Dictionary<MediaFile, MediaFile> { [other] = g.Suggested });
        var journal = Path.Combine(_dest, "_Gunluk", "islem-benzer.jsonl");
        Assert.Equal(2, new Executor(journal, FakeRecycle).Run(plan.Items).Done);
        Assert.Single(Directory.GetFiles(Path.Combine(_dest, "_Benzerler"), "*", SearchOption.AllDirectories));

        // Tekrar çalıştırma: grup bir daha önerilmez, hiçbir şey taşınmaz.
        var (again, dups2) = Scan(_dest);
        var ignore = Path.Combine(_dest, "_Benzerler");
        var hashes2 = SimilarityScan.Compute(again, dups2, ignore, new HashCache(cachePath), new List<string>());
        Assert.Empty(SimilarFinder.Find(again, hashes2, SameFrameFinder.Find(again, dups2, ignore)));
        Assert.All(Planner.Build(again, dups2, Options(_dest)).Items, i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));

        Assert.Equal(2, Executor.Undo(journal, recycle: FakeRecycle).Done);
        Assert.True(File.Exists(big.Path)); Assert.True(File.Exists(small.Path));
    }

    [Fact]
    public void Apply_Rerun_Undo_EndToEnd()
    {
        var old = new DateTime(2024, 6, 1, 12, 0, 0);
        var videoUtc = new DateTime(2024, 5, 31, 22, 30, 0); // Türkiye'de 1 Haziran 01:30
        var canon2 = TestMedia.Jpeg("Canon", new DateTime(2024, 3, 11, 14, 0, 0));
        var files = new List<Original>
        {
            Put(@"canon\IMG_0001.JPG", TestMedia.Jpeg("Canon", new DateTime(2024, 3, 10, 9, 0, 0)), old, old),
            Put(@"canon\IMG_0002.JPG", canon2, old, old),
            Put(@"fuji\DSCF0001.JPG", TestMedia.Jpeg("FUJIFILM", new DateTime(2024, 4, 2, 18, 0, 0)), old, old),
            // Aynı ad, farklı içerik: biri tarih önekiyle adlanmalı.
            Put(@"fuji\IMG_0001.JPG", TestMedia.Jpeg("FUJIFILM", new DateTime(2024, 3, 10, 10, 0, 0)), old, old),
            // Birebir kopya (sonradan kopyalanmış: daha yeni oluşturma tarihi) → _Tekrarlar
            Put(@"yedek\kopya.JPG", canon2, old.AddYears(1), old),
            Put(@"video\MVI_0001.MP4", TestMedia.Mp4(videoUtc), old, old),
        };
        var dupCopy = files[4];

        // --- Tara → Plan → Uygula (kaynaklar "Geri Dönüşüm Kutusu"na) ---
        var (scanned, dups) = Scan(_src);
        var plan = Planner.Build(scanned, dups, Options(_dest));
        Assert.Single(dups);
        Assert.Equal(1, plan.Collisions);
        var journal = Path.Combine(_dest, "_Gunluk", "islem-test.jsonl");
        var r = new Executor(journal, FakeRecycle).Run(plan.Items, recycleSource: true);
        Assert.Equal(0, r.Failed);
        Assert.Equal(files.Count, r.Done);

        Assert.Empty(Directory.GetFiles(_src, "*", SearchOption.AllDirectories));
        Assert.Equal(files.Count, Directory.GetFiles(_bin).Length);
        var placed = Directory.GetFiles(_dest, "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains(@"\_Gunluk\")).ToList();
        Assert.Equal(files.Count, placed.Count);

        string Month(DateTime d) => $@"\{d:yyyy-MM}\{d:yyyy-MM}-";
        string TargetOf(Original o) => plan.Items.Single(i => i.File.Path == o.Path).TargetPath;
        foreach (var o in files)
        {
            var t = TargetOf(o);
            Assert.Equal(o.Bytes, File.ReadAllBytes(t));
            Assert.Equal(o.Modified, File.GetLastWriteTime(t));
            Assert.Equal(o.Created, File.GetCreationTime(t));
        }
        Assert.Contains(Month(new DateTime(2024, 3, 1)) + "Foto", TargetOf(files[0]));
        Assert.Contains(Month(new DateTime(2024, 4, 1)) + "Foto", TargetOf(files[2]));
        Assert.Contains(@"\_Tekrarlar\", TargetOf(dupCopy));
        Assert.Contains(Month(Scanner.UtcToLocal(videoUtc)) + "Video", TargetOf(files[5]));
        Assert.Equal(2, placed.Count(p => Path.GetFileName(p).EndsWith("IMG_0001.JPG")));
        Assert.Contains(placed, p => Path.GetFileName(p).StartsWith("2024-03-10 "));

        // --- Tekrar çalıştırma: düzenli klasörde hiçbir şey değişmemeli ---
        var (again, dups2) = Scan(_dest);
        Assert.All(Planner.Build(again, dups2, Options(_dest)).Items,
            i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));

        // --- Geri Al: her şey eski yerine, aynı içerik ve zamanla ---
        var u = Executor.Undo(journal, recycle: FakeRecycle);
        Assert.Equal(0, u.Failed);
        Assert.Equal(files.Count, u.Done);
        foreach (var o in files)
        {
            Assert.Equal(o.Bytes, File.ReadAllBytes(o.Path));
            Assert.Equal(o.Modified, File.GetLastWriteTime(o.Path));
            Assert.Equal(o.Created, File.GetCreationTime(o.Path));
        }
        Assert.Empty(Directory.GetFiles(_dest, "*", SearchOption.AllDirectories).Where(p => !p.Contains(@"\_Gunluk\")));
        Assert.Empty(Directory.GetFiles(_src, "*.geri_tmp", SearchOption.AllDirectories));
    }
}
