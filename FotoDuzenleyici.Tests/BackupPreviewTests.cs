using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Kopyalamadan önceki özet: Run ile aynı sayılar, hiçbir yere yazmaz.</summary>
public class BackupPreviewTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_preview_" + Guid.NewGuid().ToString("N"));
    readonly string _arc, _bak;

    public BackupPreviewTests()
    {
        _arc = Path.Combine(_tmp, "arsiv");
        _bak = Path.Combine(_tmp, "yedek");
        Directory.CreateDirectory(_arc);
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Arc(string rel, string content)
    {
        var p = Path.Combine(_arc, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    static readonly BackupOptions Opt = new();

    [Fact]
    public void Preview_MatchesRun()
    {
        var changed = Arc(@"a\degisen.jpg", "eski");
        Arc(@"a\ayni.jpg", "ayni");
        var gone = Arc(@"a\silinen.jpg", "silinen");
        new ArchiveBackup().Run(_arc, _bak, Opt);
        File.WriteAllText(changed, "yepyeni icerik");
        File.Delete(gone);
        Arc(@"b\yeni.jpg", "yeni dosya");

        var p = new ArchiveBackup().Preview(_arc, _bak, Opt);
        var r = new ArchiveBackup().Run(_arc, _bak, Opt);

        Assert.Null(p.Blocked);
        Assert.Equal(1, p.New);
        Assert.Equal(1, p.Changed);
        Assert.Equal(1, p.Unchanged);
        Assert.Equal(1, p.MissingInArchive);
        Assert.Equal((r.New, r.Changed, r.Unchanged, r.MissingInArchive), (p.New, p.Changed, p.Unchanged, p.MissingInArchive));
        Assert.Equal(r.BytesCopied, p.BytesNeeded);
    }

    [Fact]
    public void Preview_WritesNothing()
    {
        Arc("a.jpg", "aaaa");

        var first = new ArchiveBackup().Preview(_arc, _bak, Opt);
        Assert.Null(first.Blocked);
        Assert.Equal(1, first.New);
        Assert.False(Directory.Exists(_bak));

        new ArchiveBackup().Run(_arc, _bak, Opt);
        var part = Path.Combine(_bak, ArchiveBackup.TempFolder, "x.part");
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        File.WriteAllText(part, "yarim");
        var manifest = Path.Combine(_bak, BackupManifest.FileName);
        var before = File.ReadAllBytes(manifest);
        var stamp = File.GetLastWriteTimeUtc(manifest);

        new ArchiveBackup().Preview(_arc, _bak, Opt);

        Assert.True(File.Exists(part));
        Assert.Equal(before, File.ReadAllBytes(manifest));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(manifest));
    }

    [Theory]
    [InlineData("arsiv-yok")]
    [InlineData("ic-ice")]
    [InlineData("bozuk-liste")]
    [InlineData("yer-yok")]
    public void Preview_Blocked(string kind)
    {
        Arc("a.jpg", "aaaa");
        var arc = _arc; var bak = _bak;
        var b = new ArchiveBackup();
        switch (kind)
        {
            case "arsiv-yok": arc = Path.Combine(_tmp, "yok"); break;
            case "ic-ice": bak = Path.Combine(_arc, "yedek"); break;
            case "bozuk-liste":
                Directory.CreateDirectory(_bak);
                File.WriteAllText(Path.Combine(_bak, BackupManifest.FileName), "{bozuk");
                break;
            case "yer-yok": b.FreeSpace = _ => 1; break;
        }

        var p = b.Preview(arc, bak, Opt);

        Assert.False(string.IsNullOrEmpty(p.Blocked));
    }

    [Fact]
    public void Preview_ReportsFreeBytes()
    {
        Arc("a.jpg", "aaaa");
        var b = new ArchiveBackup { FreeSpace = _ => 123456 };

        var p = b.Preview(_arc, _bak, Opt);

        Assert.Equal(123456, p.FreeBytes);
        Assert.Equal(4, p.BytesNeeded);
    }

    [Fact]
    public void Preview_SkipsDuplicatesByDefault()
    {
        Arc("a.jpg", "aaaa");
        Arc(@"_Tekrarlar\a.jpg", "aaaa");

        Assert.Equal(1, new ArchiveBackup().Preview(_arc, _bak, Opt).New);
        Assert.Equal(2, new ArchiveBackup().Preview(_arc, _bak, new BackupOptions { IncludeDuplicates = true }).New);
    }

    [Fact]
    public void Preview_Cancelled()
    {
        Arc("a.jpg", "aaaa");
        var p = new ArchiveBackup().Preview(_arc, _bak, Opt, new CancellationToken(true));
        Assert.True(p.Cancelled);
    }

    [Fact]
    public void Run_CancelledBeforeScan_CopiesNothing()
    {
        Arc("a.jpg", "aaaa");

        var r = new ArchiveBackup().Run(_arc, _bak, Opt, null, new CancellationToken(true));

        Assert.True(r.Cancelled);
        Assert.Equal(0, r.BytesCopied);
        Assert.False(File.Exists(Path.Combine(_bak, "a.jpg")));
    }

    [Fact]
    public void Repair_Cancelled_Reports()
    {
        Arc("a.jpg", "aaaa");
        var b = new ArchiveBackup();
        b.Run(_arc, _bak, Opt);
        File.WriteAllText(Path.Combine(_bak, "a.jpg"), "bozuk");
        var v = b.Verify(_bak);

        var r = b.Repair(_arc, _bak, v, null, new CancellationToken(true));

        Assert.True(r.Cancelled);
        Assert.Equal(0, r.Repaired);
    }
}
