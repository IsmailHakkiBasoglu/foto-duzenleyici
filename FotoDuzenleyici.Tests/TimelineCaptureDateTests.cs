using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class TimelineCaptureDateTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_tld_" + Guid.NewGuid().ToString("N"));

    public TimelineCaptureDateTests() => Directory.CreateDirectory(_root);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Put(string rel, byte[]? bytes = null)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes ?? new byte[] { 1, 2, 3 });
        return p;
    }

    [Fact]
    public void ByCaptureDate_UsesExifDate_AndContainingFolder()
    {
        Put(@"Tatil\a.jpg", TestMedia.Jpeg("Canon", new DateTime(2021, 7, 4, 10, 0, 0)));

        var r = Timeline.ByCaptureDate(_root);

        var e = Assert.Single(r.Entries);
        Assert.Equal((2021, 7, FileKind.Photo, DateSource.Exif), (e.Year, e.Month, e.Kind, e.Source));
        Assert.Equal(Path.Combine(_root, "Tatil"), e.Folder);
        Assert.Equal(TimelineMethod.CaptureDate, r.Method);
    }

    [Fact]
    public void ByCaptureDate_FileNameAndFileTimeSources_AreKept()
    {
        Put("IMG_20200105_x.jpg");
        var y = Put("y.jpg");
        var t = new DateTime(2019, 3, 10, 12, 0, 0);
        File.SetCreationTime(y, t);
        File.SetLastWriteTime(y, t);

        var r = Timeline.ByCaptureDate(_root);

        var byName = r.Entries.Single(e => e.Source == DateSource.FileName);
        Assert.Equal((2020, 1), (byName.Year, byName.Month));
        var byTime = r.Entries.Single(e => e.Source == DateSource.FileTime);
        Assert.Equal((2019, 3), (byTime.Year, byTime.Month));
    }

    [Fact]
    public void ByCaptureDate_SkipsSpecialFolders_ReportsRootLevelOnes()
    {
        Put(@"_Tekrarlar\b.jpg");
        Put(@"_Ayiklananlar\c.jpg");
        Put(@"Tatil\_Kusurlu\d.jpg");

        var r = Timeline.ByCaptureDate(_root);

        Assert.Empty(r.Entries);
        Assert.Equal(1, r.Special["_Tekrarlar"]);
        Assert.Equal(1, r.Special["_Ayiklananlar"]);
        Assert.False(r.Special.ContainsKey("_Kusurlu"));
    }

    [Fact]
    public void ByCaptureDate_RootItselfNamedSpecial_IsCounted()
    {
        var root = Path.Combine(_root, "Tarihsiz");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "IMG_20200105_a.jpg"), new byte[] { 1 });
        Directory.CreateDirectory(Path.Combine(root, "_Tekrarlar"));
        File.WriteAllBytes(Path.Combine(root, "_Tekrarlar", "b.jpg"), new byte[] { 1 });

        var r = Timeline.ByCaptureDate(root + "\\");

        Assert.Single(r.Entries);
        Assert.Equal(1, r.Special["_Tekrarlar"]);
    }

    [Fact]
    public void ByCaptureDate_IgnoresPdfAndOther()
    {
        Put("x.pdf");
        Put("n.txt");
        int reads = 0;

        var r = Timeline.ByCaptureDate(_root, read: p => { reads++; return Scanner.ReadFile(p); });

        Assert.Empty(r.Entries);
        Assert.Equal(0, reads);
    }

    [Fact]
    public void ByCaptureDate_ReadThrows_CountsAsError_AndContinues()
    {
        Put("bozuk.jpg");
        Put("IMG_20200105_iyi.jpg");

        var r = Timeline.ByCaptureDate(_root, read: p =>
            p.EndsWith("bozuk.jpg") ? throw new IOException("kilitli") : Scanner.ReadFile(p));

        Assert.Single(r.Entries);
        var err = Assert.Single(r.Errors);
        Assert.Contains("bozuk.jpg", err);
    }

    [Fact]
    public void ByCaptureDate_ReportsProgress_ToTotal()
    {
        Put("IMG_20200105_a.jpg");
        Put("IMG_20200106_b.jpg");
        Put("IMG_20200107_c.jpg");
        var seen = new List<(int done, int total)>();

        Timeline.ByCaptureDate(_root, new SyncProgress(v => { lock (seen) seen.Add(v); }));

        Assert.Contains((3, 3), seen);
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void ByCaptureDate_Cancelled_ReturnsCancelledWithoutEntries()
    {
        Put("IMG_20200105_a.jpg");
        var r = Timeline.ByCaptureDate(_root, ct: new CancellationToken(true));
        Assert.True(r.Cancelled);
        Assert.Empty(r.Entries);
    }

    sealed class SyncProgress(Action<(int, int)> a) : IProgress<(int done, int total)>
    {
        public void Report((int done, int total) v) => a(v);
    }
}
