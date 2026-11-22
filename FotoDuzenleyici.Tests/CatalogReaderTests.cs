using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Oturum kataloğu: her dosya bir kez okunur; tablo bundan türetilir ve bugünkü sayımla aynı çıkar.</summary>
public class CatalogReaderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_cat_" + Guid.NewGuid().ToString("N"));

    public CatalogReaderTests()
    {
        Put(@"2026-08\2026-08-Foto\IMG_20260803_a.jpg", 100);
        Put(@"2026-08\2026-08-Foto\IMG_20260804_b.jpg", 200);
        Put(@"2026-08\2026-08-Video\VID_20260805_c.mp4", 300);
        Put(@"2026-09\2026-09-Foto\IMG_20260901_d.jpg", 400);
        Put(@"IMG_20250101_disarida.jpg", 10);
        Put(@"_Tekrarlar\IMG_20260803_a.jpg", 100);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Put(string rel, int size)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    static List<(int, int, FileKind, int)> Counts(IEnumerable<TimelineEntry> e) => e
        .GroupBy(x => (x.Year, x.Month, x.Kind))
        .Select(g => (g.Key.Year, g.Key.Month, g.Key.Kind, g.Count()))
        .OrderBy(x => x).ToList();

    [Fact]
    public void Read_FolderNames_MatchesTimelineByFolders()
    {
        var c = CatalogReader.Read(_root, TimelineMethod.FolderNames);
        var t = Timeline.ByFolders(_root);

        Assert.Equal(Counts(t.Entries), Counts(c.ToResult().Entries));
        Assert.Equal(t.Outside, c.Outside);
        Assert.Equal(t.Special, c.Special);
        Assert.Equal(4, c.Items.Count);
    }

    [Fact]
    public void Read_CaptureDate_MatchesTimelineByCaptureDate()
    {
        var c = CatalogReader.Read(_root, TimelineMethod.CaptureDate);
        var t = Timeline.ByCaptureDate(_root);

        Assert.Equal(Counts(t.Entries), Counts(c.ToResult().Entries));
        Assert.Equal(t.Special, c.Special);
        Assert.Equal(TimelineMethod.CaptureDate, c.ToResult().Method);
    }

    [Fact]
    public void Read_FolderNames_UnreadableFile_StillCounted_ErrorReported()
    {
        var bad = Path.Combine(_root, @"2026-08\2026-08-Foto\IMG_20260804_b.jpg");
        var c = CatalogReader.Read(_root, TimelineMethod.FolderNames,
            read: p => p == bad ? throw new IOException("kilitli") : Scanner.ReadFile(p));

        Assert.Equal(4, c.Items.Count);
        Assert.Contains("kilitli", Assert.Single(c.Errors));
        var item = c.Items.Single(i => i.File.Path == bad);
        Assert.True(item.OtherMonth);
        Assert.Equal(200, item.File.Size);
        Assert.Equal((2026, 8), (item.Year, item.Month));
    }

    [Fact]
    public void Read_FolderNames_DateInOtherMonth_FlaggedOtherMonth()
    {
        var p = Put(@"2026-08\2026-08-Foto\IMG_20260915_x.jpg", 5);

        var c = CatalogReader.Read(_root, TimelineMethod.FolderNames);

        var item = c.Items.Single(i => i.File.Path == p);
        Assert.Equal((2026, 8, true), (item.Year, item.Month, item.OtherMonth));
        Assert.Equal(Path.Combine(_root, "2026-08"), item.Folder);
    }

    [Fact]
    public void Read_KeepsFullMediaFile()
    {
        var c = CatalogReader.Read(_root, TimelineMethod.CaptureDate, read: p =>
        {
            var m = Scanner.ReadFile(p);
            m.Orientation = 6;
            m.Width = 4000;
            return m;
        });

        Assert.All(c.Items, i => Assert.Equal((6, 4000), (i.File.Orientation, i.File.Width)));
    }

    [Fact]
    public void Read_MissingRoot_ReportsError_NoItems()
    {
        var c = CatalogReader.Read(Path.Combine(_root, "yok"), TimelineMethod.CaptureDate);

        Assert.Empty(c.Items);
        Assert.Single(c.Errors);
    }

    [Fact]
    public void Read_Cancelled_EmptyAndFlagged()
    {
        using var cts = new CancellationTokenSource();
        var c = CatalogReader.Read(_root, TimelineMethod.CaptureDate, new SyncProgress(_ => cts.Cancel()), cts.Token);

        Assert.True(c.Cancelled);
        Assert.Empty(c.Items);
    }

    [Fact]
    public void FilesOf_ReturnsOnlyThatMonth()
    {
        var c = CatalogReader.Read(_root, TimelineMethod.FolderNames);

        Assert.Equal(3, c.FilesOf(2026, 8).Count);
        Assert.Single(c.FilesOf(2026, 9));
        Assert.Empty(c.FilesOf(2020, 1));
    }

    [Fact]
    public void Remove_DropsItems_ResultCountsFollow()
    {
        var c = CatalogReader.Read(_root, TimelineMethod.FolderNames);
        var a = Path.Combine(_root, @"2026-08\2026-08-Foto\IMG_20260803_a.jpg");

        Assert.Equal(1, c.Remove(new[] { a.ToUpperInvariant() }));
        Assert.Equal(2, c.FilesOf(2026, 8).Count);
        Assert.Equal(3, c.ToResult().Entries.Count);
    }

    [Fact]
    public void Memory_100kItems_Under100MB()
    {
        long before = GC.GetTotalMemory(true);
        var items = new List<CatalogItem>(100_000);
        for (int i = 0; i < 100_000; i++)
        {
            var path = $@"D:\Arsiv\Fotograflar\2026-08\2026-08-Foto\2026-08-Canon\IMG_{i:D6}_bir_uzun_dosya_adi_ornegi.jpg";
            var m = new MediaFile
            {
                Path = path, Size = 4_000_000, Kind = FileKind.Photo, Created = DateTime.Now, Modified = DateTime.Now,
                Taken = DateTime.Now, DateSource = DateSource.Exif, Make = "Canon", Model = "EOS 250D",
                Width = 6000, Height = 4000, Orientation = 1,
            };
            items.Add(new CatalogItem(m, 2026, 8, @"D:\Arsiv\Fotograflar\2026-08", false));
        }
        long bytes = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(items);

        Assert.True(bytes < 100L * 1024 * 1024, $"{bytes / 1024 / 1024} MB");
        Console.WriteLine($"catalog 100k: {bytes / 1024 / 1024} MB");
    }

    sealed class SyncProgress(Action<(int, int)> a) : IProgress<(int done, int total)>
    {
        public void Report((int done, int total) v) => a(v);
    }
}
