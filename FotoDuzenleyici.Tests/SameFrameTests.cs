using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Aynı EXIF parmak izi: adı ya da baytları farklı ama büyük olasılıkla aynı kare olan fotoğraflar.</summary>
public class SameFrameTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_same_" + Guid.NewGuid().ToString("N"));
    public SameFrameTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static readonly DateTime Shot = new(2024, 3, 10, 9, 15, 30);

    static MediaFile Photo(string path, DateTime? taken = null, string make = "Canon", string model = "EOS 250D",
        string? subSec = null, int w = 6000, int h = 4000, string? serial = null, long size = 5_000_000,
        FileKind kind = FileKind.Photo, DateTime? modified = null) => new()
    {
        Path = path, Kind = kind, Size = size, Created = new DateTime(2024, 3, 10), Modified = modified ?? new DateTime(2024, 3, 10),
        Taken = taken ?? Shot, DateSource = DateSource.Exif, Make = make, Model = model,
        SubSec = subSec, Width = w, Height = h, Serial = serial, Source = "Canon", SourceFromExif = true,
    };

    static List<SameFrameGroup> Find(params MediaFile[] files) => SameFrameFinder.Find(files, new List<DuplicateGroup>());

    [Fact]
    public void SameCameraAndMoment_DifferentNameAndSize_AreGrouped()
    {
        var orig = Photo(@"C:\a\IMG_0001.JPG");
        var export = Photo(@"C:\b\duzenlenmis.jpg", w: 2048, h: 1365, size: 900_000);
        var other = Photo(@"C:\a\IMG_0002.JPG", taken: Shot.AddSeconds(1));
        var g = Assert.Single(Find(orig, export, other));
        Assert.Equal(2, g.Members.Count);
        Assert.Same(orig, g.Suggested); // en yüksek çözünürlük
    }

    [Fact]
    public void SameResolution_SuggestsTheOriginal_NotTheLargerResave()
    {
        // Etiket eklenip yeniden kaydedilen kopya genelde daha büyüktür ama sonradan yazılmıştır.
        var orig = Photo(@"C:\a\x.jpg", size: 4_000_000, modified: new DateTime(2024, 3, 10));
        var resaved = Photo(@"C:\b\x (1).jpg", size: 5_000_000, modified: new DateTime(2024, 4, 9));
        Assert.Same(orig, Assert.Single(Find(resaved, orig)).Suggested);
    }

    [Fact]
    public void BurstShots_WithDifferentSubSeconds_AreNotGrouped() =>
        Assert.Empty(Find(Photo(@"C:\a\1.jpg", subSec: "10"), Photo(@"C:\a\2.jpg", subSec: "45")));

    [Fact]
    public void SubSecondMissingOnOneSide_StillGrouped() =>
        Assert.Single(Find(Photo(@"C:\a\1.jpg", subSec: "10"), Photo(@"C:\b\1.jpg")));

    [Fact]
    public void TwoBodiesOfSameModel_AreNotGrouped() =>
        Assert.Empty(Find(Photo(@"C:\a\1.jpg", serial: "111"), Photo(@"C:\b\1.jpg", serial: "222")));

    [Fact]
    public void DifferentModel_IsNotGrouped() =>
        Assert.Empty(Find(Photo(@"C:\a\1.jpg"), Photo(@"C:\b\1.jpg", model: "EOS R6")));

    [Fact]
    public void RawJpgPair_AndVideo_AreNotGrouped()
    {
        Assert.Empty(Find(Photo(@"C:\a\IMG_1.JPG"), Photo(@"C:\a\IMG_1.CR3", kind: FileKind.Raw)));
        Assert.Empty(Find(Photo(@"C:\a\1.mp4", kind: FileKind.Video), Photo(@"C:\a\2.mp4", kind: FileKind.Video)));
    }

    [Fact]
    public void DateFromFileTime_IsNeverGrouped()
    {
        var a = Photo(@"C:\a\1.jpg"); a.DateSource = DateSource.FileTime;
        var b = Photo(@"C:\b\1.jpg"); b.DateSource = DateSource.FileTime;
        Assert.Empty(Find(a, b));
    }

    [Fact]
    public void ExactDuplicateCopies_AreLeftToDuplicateFinder()
    {
        var keeper = Photo(@"C:\a\1.jpg");
        var copy = Photo(@"C:\b\1.jpg");
        var dups = new List<DuplicateGroup> { new() { Keeper = keeper, Copies = new() { copy } } };
        Assert.Empty(SameFrameFinder.Find(new[] { keeper, copy }, dups));
    }

    [Fact]
    public void FilesAlreadyInSimilarFolder_AreIgnored()
    {
        var kept = Photo(@"Z:\out\2024-03\2024-03-Foto\1.jpg");
        var set = Photo(@"Z:\out\_Benzerler\2024-03\1.jpg");
        Assert.Empty(SameFrameFinder.Find(new[] { kept, set }, new List<DuplicateGroup>(), @"Z:\out\_Benzerler"));
    }

    [Fact]
    public void Scanner_ReadsSubSecondSizeAndSerial()
    {
        var p = Path.Combine(_tmp, "IMG_0001.JPG");
        File.WriteAllBytes(p, TestMedia.Jpeg("Canon", Shot, model: "Canon EOS 250D", subSec: "42",
            width: 6000, height: 4000, serial: "123456"));
        var f = Scanner.ReadFile(p);
        Assert.Equal("Canon EOS 250D", f.Model);
        Assert.Equal("42", f.SubSec);
        Assert.Equal(6000, f.Width);
        Assert.Equal(4000, f.Height);
        Assert.Equal("123456", f.Serial);
    }

    // --- Plan: işareti kaldırılan _Benzerler'e gider, tekrar çalıştırmada orada kalır ---

    [Fact]
    public void Planner_SendsChosenSimilarToSimilarFolder()
    {
        var keep = Photo(@"C:\a\IMG_0001.JPG");
        var other = Photo(@"C:\b\duzenlenmis.jpg");
        var plan = Planner.Build(new[] { keep, other }, new List<DuplicateGroup>(),
            new PlanOptions { DestinationRoot = @"Z:\out" }, new Dictionary<MediaFile, MediaFile> { [other] = keep });
        var item = plan.Items.Single(i => i.File == other);
        Assert.Equal(PlanAction.Similar, item.Action);
        Assert.Equal(@"Z:\out\_Benzerler\2024-03", item.TargetDir);
        Assert.Equal(@"Z:\out\2024-03\2024-03-Foto", plan.Items.Single(i => i.File == keep).TargetDir);
    }

    [Fact]
    public void Planner_LeavesFilesInSimilarFolderInPlace()
    {
        var f = Photo(@"Z:\out\_Benzerler\2024-03\duzenlenmis.jpg");
        var plan = Planner.Build(new[] { f }, new List<DuplicateGroup>(), new PlanOptions { DestinationRoot = @"Z:\out" });
        Assert.Equal(PlanAction.AlreadyInPlace, plan.Items[0].Action);
    }

    [Fact]
    public void EndToEnd_SimilarGoesToFolder_AndUndoBringsItBack()
    {
        var src = Path.Combine(_tmp, "src"); var dest = Path.Combine(_tmp, "out"); var bin = Path.Combine(_tmp, "bin");
        Directory.CreateDirectory(src); Directory.CreateDirectory(bin);
        var a = Path.Combine(src, "IMG_0001.JPG");
        var b = Path.Combine(src, "IMG_0001 (1).JPG");
        File.WriteAllBytes(a, TestMedia.Jpeg("Canon", Shot, model: "EOS 250D", width: 6000, height: 4000));
        File.WriteAllBytes(b, TestMedia.Jpeg("Canon", Shot, model: "EOS 250D", width: 6000, height: 4000, salt: 1));

        var files = new Scanner(new ScanOptions { Roots = { src } }).Scan();
        var dups = DuplicateFinder.Find(files);
        Assert.Empty(dups); // baytlar farklı
        var g = Assert.Single(SameFrameFinder.Find(files, dups));
        var other = g.Members.Single(m => m != g.Suggested);
        var plan = Planner.Build(files, dups, new PlanOptions { DestinationRoot = dest },
            new Dictionary<MediaFile, MediaFile> { [other] = g.Suggested });

        var journal = Path.Combine(dest, "_Gunluk", "j.jsonl");
        void Bin(string p) => File.Move(p, Path.Combine(bin, Guid.NewGuid().ToString("N")));
        Assert.Equal(2, new Executor(journal, Bin).Run(plan.Items).Done);
        Assert.Single(Directory.GetFiles(Path.Combine(dest, "_Benzerler"), "*", SearchOption.AllDirectories));

        // Tekrar çalıştırma: hiçbir şey değişmez, grup bir daha önerilmez.
        var again = new Scanner(new ScanOptions { Roots = { dest } }).Scan();
        Assert.Empty(SameFrameFinder.Find(again, new List<DuplicateGroup>(), Path.Combine(dest, "_Benzerler")));
        Assert.All(Planner.Build(again, new List<DuplicateGroup>(), new PlanOptions { DestinationRoot = dest }).Items,
            i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));

        Assert.Equal(2, Executor.Undo(journal, recycle: Bin).Done);
        Assert.True(File.Exists(a)); Assert.True(File.Exists(b));
    }
}
