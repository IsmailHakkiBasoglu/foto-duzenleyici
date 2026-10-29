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
