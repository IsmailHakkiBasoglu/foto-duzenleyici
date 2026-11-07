using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Temizle: kopyada kalan konum (öncelikli klasör → en eski) ve _Ayiklananlar'ın taranmaması.</summary>
public class CleanKeeperTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cleankeep_" + Guid.NewGuid().ToString("N"));
    public CleanKeeperTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static MediaFile F(string path, int day) =>
        new() { Path = path, Kind = FileKind.Photo, Size = 10, Created = new DateTime(2024, 1, day) };

    static DuplicateGroup G(params MediaFile[] m) => new() { Keeper = m[0], Copies = m.Skip(1).ToList() };

    static readonly string[] Priority = { @"D:\Aile" };
    static bool Pref(MediaFile f) => DuplicateKeeper.InAny(f, Priority);

    [Fact]
    public void PriorityFolder_BeatsOldest()
    {
        var oldest = F(@"E:\Indirilenler\a.jpg", 1);
        var fam = F(@"D:\Aile\2024\a.jpg", 9);
        Assert.Same(fam, DuplicateKeeper.Choose(G(oldest, fam), Pref));
    }

    [Fact]
    public void NoPriorityMatch_OldestKept()
    {
        var oldest = F(@"E:\x\a.jpg", 1);
        var other = F(@"F:\y\a.jpg", 9);
        Assert.Same(oldest, DuplicateKeeper.Choose(G(oldest, other), Pref));
    }

    [Fact]
    public void UserChoice_BeatsPriority()
    {
        var fam = F(@"D:\Aile\a.jpg", 1);
        var other = F(@"F:\y\a.jpg", 9);
        Assert.Same(other, DuplicateKeeper.Choose(G(fam, other), Pref, new HashSet<MediaFile> { other }));
    }

    [Fact]
    public void InAny_DoesNotConfuseSiblingPrefix()
    {
        Assert.False(DuplicateKeeper.InAny(F(@"D:\Aile2\a.jpg", 1), Priority));
        Assert.True(DuplicateKeeper.InAny(F(@"d:\aile\a.jpg", 1), Priority));
        Assert.True(DuplicateKeeper.InAny(F(@"E:\a.jpg", 1), new[] { @"E:\" }));
    }

    [Fact]
    public void Scanner_SkipsRejectsFolder_AtAnyDepth()
    {
        void Make(string rel) { var p = Path.Combine(_tmp, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, rel); }
        Make(@"kok\_Ayiklananlar\a.pdf");
        Make(@"kok\alt\_Ayiklananlar\Tekrarlar\b.pdf");
        Make(@"kok\c.pdf");
        var files = new Scanner(new ScanOptions { Roots = { Path.Combine(_tmp, "kok") }, IncludedKinds = { FileKind.Pdf } }).Scan();
        Assert.Equal(new[] { "c.pdf" }, files.Select(f => f.FileName));
    }
}
