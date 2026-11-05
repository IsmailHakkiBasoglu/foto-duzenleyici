using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>RAW + JPG eşleri: adları hep eş kalır, ayıklanan karenin RAW'ı istenirse onunla gider.</summary>
public class RawJpgPairTests
{
    static readonly DateTime T = new(2026, 1, 3, 10, 15, 0);

    static MediaFile F(string path, DateTime? taken = null) => new()
    {
        Path = path,
        Kind = FileClassifier.Classify(path),
        Taken = taken ?? T, DateSource = DateSource.Exif, Source = "Fuji", SourceFromExif = true,
    };

    static Plan Build(IReadOnlyList<MediaFile> files, PlanOptions? opt = null,
        IReadOnlyDictionary<MediaFile, MediaFile>? similarOf = null, List<DuplicateGroup>? dups = null) =>
        Planner.Build(files, dups ?? new List<DuplicateGroup>(), opt ?? new PlanOptions { DestinationRoot = @"Z:\out" }, similarOf);

    static PlanItem Of(Plan p, MediaFile f) => p.Items.Single(i => i.File == f);

    [Fact]
    public void Groups_FindsRawAndJpgWithSameStemInSameFolder()
    {
        var jpg = F(@"C:\x\DSCF0001.JPG"); var raf = F(@"C:\x\dscf0001.RAF");
        var other = F(@"C:\y\DSCF0001.RAF"); var lone = F(@"C:\x\DSCF0002.JPG");
        var groups = RawJpgPairs.Groups(new[] { raf, other, lone, jpg });
        var g = Assert.Single(groups);
        Assert.Equal(new[] { jpg, raf }, g); // önce fotoğraf
    }

    [Theory]
    [InlineData(@"Z:\out\2026-01\2026-01-Raw\", @"Z:\out\2026-01\2026-01-Foto\")]
    [InlineData(@"Z:\out\2026-01\2026-01-Raw\2026-01-Fuji\", @"Z:\out\2026-01\2026-01-Foto\2026-01-Fuji\")]
    public void Groups_InOrganizedArchive_PairsRawFolderWithSiblingFotoFolder(string rawDir, string fotoDir)
    {
        var jpg = F(fotoDir + "DSCF0001.JPG"); var raf = F(rawDir + "DSCF0001.RAF");
        var g = Assert.Single(RawJpgPairs.Groups(new[] { raf, jpg }));
        Assert.Equal(new[] { jpg, raf }, g);
    }

    [Fact]
    public void OrganizedPair_InSeparateFolders_Rerun_StaysInPlace()
    {
        var jpg = F(@"Z:\out\2026-01\2026-01-Foto\2026-01-03 DSCF0001.JPG");
        var raf = F(@"Z:\out\2026-01\2026-01-Raw\2026-01-03 DSCF0001.RAF");
        var p = Build(new[] { jpg, raf }, new PlanOptions { DestinationRoot = @"Z:\out", RenameWithDate = true });
        Assert.All(p.Items, i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));
    }

    [Fact]
    public void JpgCollides_RawFollowsSameName()
    {
        var first = F(@"C:\a\DSCF0001.JPG");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var p = Build(new[] { first, jpg, raf });
        Assert.Equal("DSCF0001.JPG", Of(p, first).TargetName);
        Assert.Equal("2026-01-03 DSCF0001.JPG", Of(p, jpg).TargetName);
        Assert.Equal("2026-01-03 DSCF0001.RAF", Of(p, raf).TargetName);
    }

    [Fact]
    public void RawCollides_JpgFollowsSameName()
    {
        var first = F(@"C:\a\DSCF0001.RAF");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var p = Build(new[] { first, jpg, raf });
        Assert.Equal("2026-01-03 DSCF0001.JPG", Of(p, jpg).TargetName);
        Assert.Equal("2026-01-03 DSCF0001.RAF", Of(p, raf).TargetName);
    }

    [Fact]
    public void SeparateRawFolder_DifferentFolders_SameName()
    {
        var first = F(@"C:\a\DSCF0001.RAF");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var p = Build(new[] { first, jpg, raf }, new PlanOptions { DestinationRoot = @"Z:\out", SeparateRawFolder = true });
        Assert.Equal(@"Z:\out\2026-01\2026-01-Foto", Of(p, jpg).TargetDir);
        Assert.Equal(@"Z:\out\2026-01\2026-01-Raw", Of(p, raf).TargetDir);
        Assert.Equal("2026-01-03 DSCF0001.JPG", Of(p, jpg).TargetName);
        Assert.Equal("2026-01-03 DSCF0001.RAF", Of(p, raf).TargetName);
    }

    [Fact]
    public void PairWithSlightlyDifferentDates_StaysInSameMonth()
    {
        // JPG ayın son saniyesinde, RAW bir sonraki ayın ilk saniyesinde yazılmış olsa da ayrılmaz.
        var jpg = F(@"C:\b\DSCF0001.JPG", new DateTime(2026, 1, 31, 23, 59, 59));
        var raf = F(@"C:\b\DSCF0001.RAF", new DateTime(2026, 2, 1, 0, 0, 0));
        var p = Build(new[] { jpg, raf });
        static string Month(PlanItem i) => Path.GetRelativePath(@"Z:\out", i.TargetDir).Split('\\')[0];
        Assert.Equal("2026-01", Month(Of(p, jpg)));
        Assert.Equal("2026-01", Month(Of(p, raf)));
    }

    [Fact]
    public void OrganizedPair_Rerun_StaysInPlace()
    {
        var dir = @"Z:\out\2026-01\2026-01-Foto\";
        var jpg = F(dir + "2026-01-03 DSCF0001 Fuji.JPG"); var raf = F(dir + "2026-01-03 DSCF0001 Fuji.RAF");
        var p = Build(new[] { jpg, raf },
            new PlanOptions { DestinationRoot = @"Z:\out", RenameWithDate = true, AppendSourceToName = true, SeparateRawFolder = false });
        Assert.All(p.Items, i => Assert.Equal(PlanAction.AlreadyInPlace, i.Action));
    }

    [Fact]
    public void ReleasedPair_BothGoToSimilar_WithSameName()
    {
        var keep = F(@"C:\b\DSCF0002.JPG");
        var first = F(@"C:\a\DSCF0001.JPG");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var p = Build(new[] { keep, first, jpg, raf }, new PlanOptions { DestinationRoot = @"Z:\out" },
            new Dictionary<MediaFile, MediaFile> { [jpg] = keep, [raf] = keep, [first] = keep });
        Assert.Equal(PlanAction.Similar, Of(p, jpg).Action);
        Assert.Equal(PlanAction.Similar, Of(p, raf).Action);
        Assert.Contains("_Benzerler", Of(p, raf).TargetDir);
        Assert.Equal(Path.GetFileNameWithoutExtension(Of(p, jpg).TargetName), Path.GetFileNameWithoutExtension(Of(p, raf).TargetName));
        Assert.NotEqual("DSCF0001.JPG", Of(p, jpg).TargetName); // first ile çakıştı
    }

    [Fact]
    public void ReleasedJpgOnly_RawStaysAsMove()
    {
        var keep = F(@"C:\b\DSCF0002.JPG");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var p = Build(new[] { keep, jpg, raf }, similarOf: new Dictionary<MediaFile, MediaFile> { [jpg] = keep });
        Assert.Equal(PlanAction.Similar, Of(p, jpg).Action);
        Assert.Equal(PlanAction.Move, Of(p, raf).Action);
    }

    [Fact]
    public void DuplicateJpg_DoesNotDragItsRaw()
    {
        var orig = F(@"C:\a\DSCF0001.JPG");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var dups = new List<DuplicateGroup> { new() { Keeper = orig, Copies = new() { jpg } } };
        var p = Build(new[] { orig, jpg, raf }, dups: dups);
        Assert.Equal(PlanAction.Duplicate, Of(p, jpg).Action);
        Assert.Equal(PlanAction.Move, Of(p, raf).Action);
        Assert.Equal("DSCF0001.RAF", Of(p, raf).TargetName);
    }

    [Fact]
    public void UnreleasedRaws_ListsRawOfReleasedPhotos_WithTheirKeeper()
    {
        var keep = F(@"C:\b\DSCF0002.JPG");
        var jpg = F(@"C:\b\DSCF0001.JPG"); var raf = F(@"C:\b\DSCF0001.RAF");
        var lone = F(@"C:\b\DSCF0003.JPG");
        var files = new[] { keep, jpg, raf, lone };

        var extra = RawJpgPairs.UnreleasedRaws(files, new Dictionary<MediaFile, MediaFile> { [jpg] = keep, [lone] = keep });
        var (raw, keeper) = Assert.Single(extra);
        Assert.Same(raf, raw);
        Assert.Same(keep, keeper);

        Assert.Empty(RawJpgPairs.UnreleasedRaws(files, new Dictionary<MediaFile, MediaFile> { [jpg] = keep, [raf] = keep }));
        Assert.Empty(RawJpgPairs.UnreleasedRaws(files, new Dictionary<MediaFile, MediaFile>()));
    }
}
