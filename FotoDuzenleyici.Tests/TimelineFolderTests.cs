using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class TimelineFolderTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_tl_" + Guid.NewGuid().ToString("N"));

    public TimelineFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var d in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
                File.SetAttributes(d, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch { }
    }

    string Put(string rel, int size = 1)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, new string('x', size));
        return p;
    }

    [Fact]
    public void IsMonthFolderName_AcceptsOnlyYearDashMonth()
    {
        Assert.True(Timeline.IsMonthFolderName("2022-11"));
        Assert.False(Timeline.IsMonthFolderName("2022-13"));
        Assert.False(Timeline.IsMonthFolderName("2022-00"));
        Assert.False(Timeline.IsMonthFolderName("2022-11-Foto"));
        Assert.False(Timeline.IsMonthFolderName("Kasım"));
    }

    [Fact]
    public void DefaultMethod_FolderNamesWhenMonthFolderExists_ElseCaptureDate()
    {
        Put(@"Tatil\a.jpg");
        Assert.Equal(TimelineMethod.CaptureDate, Timeline.DefaultMethod(_root));
        Directory.CreateDirectory(Path.Combine(_root, "2022-11"));
        Assert.Equal(TimelineMethod.FolderNames, Timeline.DefaultMethod(_root));
    }

    [Fact]
    public void ByFolders_CountsFilesUnderMonthFolders_WithKindAndSize()
    {
        Put(@"2022-11\2022-11-Foto\a.jpg", 3);
        Put(@"2022-11\2022-11-Foto\b.jpg", 5);
        Put(@"2022-11\2022-11-Raw\a.cr3", 7);
        Put(@"2022-11\2022-11-Video\v.mp4");
        Put(@"2022-11\2022-11-Pdf\x.pdf");

        var r = Timeline.ByFolders(_root);

        Assert.Equal(TimelineMethod.FolderNames, r.Method);
        Assert.Equal(4, r.Entries.Count);
        Assert.All(r.Entries, e => { Assert.Equal(2022, e.Year); Assert.Equal(11, e.Month); });
        Assert.All(r.Entries, e => Assert.Equal(Path.Combine(_root, "2022-11"), e.Folder));
        Assert.Equal(2, r.Entries.Count(e => e.Kind == FileKind.Photo));
        Assert.Equal(1, r.Entries.Count(e => e.Kind == FileKind.Raw));
        Assert.Equal(1, r.Entries.Count(e => e.Kind == FileKind.Video));
        Assert.Equal(8, r.Entries.Where(e => e.Kind == FileKind.Photo).Sum(e => e.Size));
    }

    [Fact]
    public void ByFolders_SpecialFoldersReportedNotCounted()
    {
        Put(@"_Tekrarlar\2022-11\c.jpg");
        Put(@"Tarihsiz\d.jpg");
        Put(@"2022-11\_Tekrarlar\e.jpg");
        Put(@"2022-11\2022-11-Foto\f.jpg");

        var r = Timeline.ByFolders(_root);

        Assert.Single(r.Entries);
        Assert.Equal(1, r.Special["_Tekrarlar"]);
        Assert.Equal(1, r.Special["Tarihsiz"]);
        Assert.Equal(0, r.Outside);
    }

    [Fact]
    public void ByFolders_FilesOutsideMonthFolders_CountedAsOutside()
    {
        Put("f.jpg");
        Put(@"Tatil\g.jpg");
        Put(@"Tatil\notlar.txt");

        var r = Timeline.ByFolders(_root);

        Assert.Empty(r.Entries);
        Assert.Equal(2, r.Outside);
    }

    [Fact]
    public void ByFolders_SkipsHiddenSystemFolders()
    {
        Put(@"2022-11\gizli\h.jpg");
        Put(@"2022-11\i.jpg");
        File.SetAttributes(Path.Combine(_root, "2022-11", "gizli"), FileAttributes.Hidden | FileAttributes.System | FileAttributes.Directory);

        var r = Timeline.ByFolders(_root);

        Assert.Single(r.Entries);
    }

    [Fact]
    public void ByFolders_RootMissing_ReturnsErrorNotThrow()
    {
        var r = Timeline.ByFolders(Path.Combine(_root, "yok"));
        Assert.Empty(r.Entries);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void ByFolders_Cancelled_ReturnsCancelledWithoutEntries()
    {
        Put(@"2022-11\a.jpg");
        var r = Timeline.ByFolders(_root, new CancellationToken(true));
        Assert.True(r.Cancelled);
        Assert.Empty(r.Entries);
    }
}
