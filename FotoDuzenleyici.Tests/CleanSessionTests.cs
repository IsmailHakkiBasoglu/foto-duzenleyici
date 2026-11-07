using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Temizle oturumu: ayıklananlar kendi kökünün _Ayiklananlar klasörüne, asıl göreli yoluyla.</summary>
public class CleanSessionTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_clean_" + Guid.NewGuid().ToString("N"));
    public CleanSessionTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Make(string rel, string content, DateTime created)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        File.SetCreationTime(p, created);
        File.SetLastWriteTime(p, created);
        return p;
    }

    string T(string rel) => Path.Combine(_tmp, rel);

    CleanSession Session(string[] roots, string[]? priority = null) =>
        new(roots.Select(T), priority?.Select(T), new ScanOptions { IncludedKinds = { FileKind.Pdf } });

    static readonly DateTime Old = new(2020, 1, 1), New = new(2024, 1, 1);

    [Fact]
    public void DuplicateCopy_GoesToRejectsWithRelativePath_KeeperHasNoRow()
    {
        var keep = Make(@"kok\a\x.pdf", "AYNI", Old);
        Make(@"kok\b\c\x2.pdf", "AYNI", New);
        Make(@"kok\tek.pdf", "TEK", New);
        var s = Session(new[] { "kok" });
        s.Scan();
        var item = Assert.Single(s.BuildPlan().Items);
        Assert.Equal(T(@"kok\b\c\x2.pdf"), item.File.Path);
        Assert.Equal(T(@"kok\_Ayiklananlar\Tekrarlar\b\c\x2.pdf"), item.TargetPath);
        Assert.Equal(PlanAction.Duplicate, item.Action);
        Assert.Equal(keep, item.DuplicateOf!.Path);
    }

    [Fact]
    public void PriorityFolder_DecidesKeeper()
    {
        Make(@"kok\a\x.pdf", "AYNI", Old);
        var fam = Make(@"kok\Aile\x.pdf", "AYNI", New);
        var s = Session(new[] { "kok" }, new[] { @"kok\Aile" });
        s.Scan();
        var item = Assert.Single(s.BuildPlan().Items);
        Assert.Equal(T(@"kok\a\x.pdf"), item.File.Path);
        Assert.Equal(fam, item.DuplicateOf!.Path);
        Assert.Same(item.DuplicateOf, s.Keeper(s.Duplicates[0]));
    }

    [Fact]
    public void TwoRoots_EachRejectGoesToOwnRoot_KeeperMayBeInOtherRoot()
    {
        var keep = Make(@"r1\x.pdf", "AYNI", Old);
        Make(@"r2\alt\y.pdf", "AYNI", New);
        var s = Session(new[] { "r1", "r2" });
        s.Scan();
        var item = Assert.Single(s.BuildPlan().Items);
        Assert.Equal(T(@"r2\_Ayiklananlar\Tekrarlar\alt\y.pdf"), item.TargetPath);
        Assert.Equal(keep, item.DuplicateOf!.Path);
    }

    MediaFile Fake(string rel) => new() { Path = T(rel), Kind = FileKind.Photo, Size = 10, Created = Old };

    [Fact]
    public void SimilarAndFlawed_GoToTheirFolders_DuplicateWinsOverSimilar()
    {
        var f1 = Fake(@"kok\a.jpg"); var f2 = Fake(@"kok\b.jpg"); var f3 = Fake(@"kok\c.jpg"); var f4 = Fake(@"kok\d\e.jpg");
        var s = Session(new[] { "kok" });
        s.Files = new() { f1, f2, f3, f4 };
        s.Duplicates = new() { new DuplicateGroup { Keeper = f1, Copies = new() { f2 } } };

        var plan = s.BuildPlan(similarOf: new Dictionary<MediaFile, MediaFile> { [f2] = f1, [f3] = f1 },
            flawed: new HashSet<MediaFile> { f4 });

        var by = plan.Items.ToDictionary(i => i.File);
        Assert.Equal(3, plan.Items.Count);
        Assert.Equal(PlanAction.Duplicate, by[f2].Action);
        Assert.Equal(T(@"kok\_Ayiklananlar\Tekrarlar\b.jpg"), by[f2].TargetPath);
        Assert.Equal(PlanAction.Similar, by[f3].Action);
        Assert.Equal(T(@"kok\_Ayiklananlar\Benzerler\c.jpg"), by[f3].TargetPath);
        Assert.Contains("a.jpg", by[f3].Note);
        Assert.Equal(PlanAction.Flawed, by[f4].Action);
        Assert.Equal(T(@"kok\_Ayiklananlar\Kusurlu\d\e.jpg"), by[f4].TargetPath);
    }

    [Fact]
    public void NameClash_OnDiskAndWithinPlan_GetSuffix()
    {
        Make(@"kok\_Ayiklananlar\Kusurlu\a.jpg", "onceki", Old);
        var a = Fake(@"kok\a.jpg"); var a2 = Fake(@"kok\a (2).jpg");
        var s = Session(new[] { "kok" });
        s.Files = new() { a, a2 };
        var plan = s.BuildPlan(flawed: new HashSet<MediaFile> { a, a2 });
        var by = plan.Items.ToDictionary(i => i.File);
        // Yol sırasıyla işlenir: "a (2).jpg" önce gelir ve kendi adını alır; "a.jpg" diskteki ve plandaki
        // adlarla çakışır, " (3)" alır.
        Assert.Equal("a (2).jpg", by[a2].TargetName);
        Assert.Equal("a (3).jpg", by[a].TargetName);
    }

    [Fact]
    public void DriveRootLikePaths_BuildCorrectTargets()
    {
        var keep = new MediaFile { Path = @"Q:\Arsiv\a.jpg", Kind = FileKind.Photo, Created = Old };
        var copy = new MediaFile { Path = @"Q:\DCIM\a.jpg", Kind = FileKind.Photo, Created = New };
        var s = new CleanSession(new[] { @"Q:\" });
        s.Files = new() { keep, copy };
        s.Duplicates = new() { new DuplicateGroup { Keeper = keep, Copies = new() { copy } } };
        Assert.Equal(@"Q:\_Ayiklananlar\Tekrarlar\DCIM\a.jpg", Assert.Single(s.BuildPlan().Items).TargetPath);
    }

    [Fact]
    public void NewJournalPath_IsUnderFirstRootRejects()
    {
        var s = Session(new[] { "r1", "r2" });
        var j = s.NewJournalPath();
        Assert.StartsWith(T(@"r1\_Ayiklananlar\_Gunluk\temizlik-"), j);
        Assert.EndsWith(".jsonl", j);
    }
}
