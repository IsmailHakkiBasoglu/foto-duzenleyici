using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Benzer / kusurlu grup kurma: klasik ve yeni pencerenin ortak kullandığı mantık.</summary>
public class ReviewBuilderTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_rb_" + Guid.NewGuid().ToString("N"));
    readonly ReviewStore _store;
    public ReviewBuilderTests()
    {
        Directory.CreateDirectory(_tmp);
        _store = new ReviewStore(Path.Combine(_tmp, "kararlar.json"));
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    const ulong Base = 0x0123_4567_89AB_CDEF;
    static readonly byte[] Gray = Enumerable.Repeat((byte)128, 256).ToArray();
    static readonly IReadOnlyDictionary<MediaFile, QualityScore> NoScores = new Dictionary<MediaFile, QualityScore>();
    static readonly QualityScore Blurry = new(5, 120, 0, false);

    MediaFile File(string rel, FileKind kind = FileKind.Photo, int w = 6000, int h = 4000, int seed = 1)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, Enumerable.Range(0, 500).Select(i => (byte)(i * seed + w)).ToArray());
        return new MediaFile
        {
            Path = p, Kind = kind, Size = w, Width = w, Height = h,
            Taken = new DateTime(2024, 3, 10, 9, 0, 0), DateSource = DateSource.FileTime,
        };
    }

    /// <summary>a büyük, b küçültülmüş kopyası (izler 4 bit farklı).</summary>
    (MediaFile a, MediaFile b, Dictionary<MediaFile, ImagePrint> prints) Pair(string dirB = "")
    {
        var a = File("a.jpg");
        var b = File(Path.Combine(dirB, "b.jpg"), w: 1600, h: 1067, seed: 2);
        var prints = new Dictionary<MediaFile, ImagePrint> { [a] = new(Base, Gray), [b] = new(Base ^ 0xF, Gray) };
        return (a, b, prints);
    }

    List<SimilarGroupState> Similar(List<MediaFile> files, Dictionary<MediaFile, ImagePrint> prints) =>
        ReviewBuilder.BuildSimilar(files, new List<DuplicateGroup>(), prints,
            ReviewBuilder.IgnoreDirs(new PlanOptions { DestinationRoot = _tmp }), NoScores, _store);

    [Fact]
    public void IgnoreDirs_AreParkedFoldersUnderArchive()
    {
        var dirs = ReviewBuilder.IgnoreDirs(new PlanOptions { DestinationRoot = _tmp });
        Assert.Equal(new[] { Path.Combine(_tmp, "_Benzerler"), Path.Combine(_tmp, "_Kusurlu") }, dirs);
    }

    [Fact]
    public void BuildSimilar_ResizedCopy_OneGroupWithKeys()
    {
        var (a, b, prints) = Pair();
        var g = Assert.Single(Similar(new() { a, b }, prints));
        Assert.Equal(SimilarKind.ResizedCopy, g.Kind);
        Assert.All(g.Rows, r => Assert.NotNull(r.StoreKey));
        Assert.NotNull(g.Signature);
    }

    [Fact]
    public void BuildSimilar_RestoresStoredKeep()
    {
        var (a, b, prints) = Pair();
        _store.SetKeep(HashCache.Key(b.Path), false);
        var g = Assert.Single(Similar(new() { a, b }, prints));
        Assert.False(g.Rows.Single(r => r.File == b).Keep);
        Assert.True(g.Rows.Single(r => r.File == a).Keep);
    }

    [Fact]
    public void BuildSimilar_IgnoresParkedFolders()
    {
        // Aynı EXIF'li iki kare (aynı kare adayı); biri önceki turda _Benzerler'e ayrılmış. İzi SimilarityScan
        // zaten hesaplamaz; aynı kare araması da onu dışlamalı.
        var a = File("a.jpg");
        var b = File(@"_Benzerler\2024-03\tatil.jpg", seed: 2);
        foreach (var f in new[] { a, b }) { f.Make = "Canon"; f.Model = "EOS 250D"; f.DateSource = DateSource.Exif; }
        var prints = new Dictionary<MediaFile, ImagePrint> { [a] = new(Base, Gray) };
        Assert.Empty(Similar(new() { a, b }, prints));

        // Karşı sınama: park klasöründe olmasa aynı kare grubu olurdu.
        var c = File(@"baska\tatil.jpg", seed: 2);
        c.Make = "Canon"; c.Model = "EOS 250D"; c.DateSource = DateSource.Exif;
        Assert.Single(Similar(new() { a, c }, prints));
    }

    [Fact]
    public void BuildSimilar_HasRaw()
    {
        var (a, b, prints) = Pair();
        var raw = File("a.CR3", FileKind.Raw, seed: 3);
        var g = Assert.Single(Similar(new() { a, b, raw }, prints));
        Assert.True(g.Rows.Single(r => r.File == a).HasRaw);
        Assert.False(g.Rows.Single(r => r.File == b).HasRaw);
    }

    [Fact]
    public void BuildFlawed_ExcludesSimilarMembers()
    {
        var (a, b, prints) = Pair();
        var lone = File("c.jpg", seed: 5);
        var files = new List<MediaFile> { a, b, lone };
        var similar = Similar(files, prints);
        var scores = new Dictionary<MediaFile, QualityScore> { [b] = Blurry, [lone] = Blurry };

        var flawed = ReviewBuilder.BuildFlawed(files, scores, similar, _store);
        var g = Assert.Single(flawed);
        var row = Assert.Single(g.Rows);
        Assert.Equal(lone, row.File);
        Assert.Contains("BULANIK", row.Flaw);
        Assert.NotNull(row.StoreKey);
        Assert.Equal(similar.Count + 1, g.Number);
    }

    [Fact]
    public void SimilarMap_PrefersSuggestedKeeper()
    {
        var (a, b, prints) = Pair();
        var g = Assert.Single(Similar(new() { a, b }, prints));
        var suggested = g.Rows.Single(r => r.Suggested);
        var other = g.Rows.Single(r => !r.Suggested);
        other.Keep = false;
        var map = ReviewBuilder.SimilarMap(new[] { g });
        Assert.Equal(suggested.File, map[other.File]);
        Assert.Single(map);
    }

    [Fact]
    public void FlawedSet_OnlyReleased()
    {
        var c = File("c.jpg", seed: 5); var d = File("d.jpg", seed: 6);
        var scores = new Dictionary<MediaFile, QualityScore> { [c] = Blurry, [d] = Blurry };
        var flawed = ReviewBuilder.BuildFlawed(new List<MediaFile> { c, d }, scores, new List<SimilarGroupState>(), _store);
        flawed.Single(g => g.Rows[0].File == c).Rows[0].Keep = false;
        Assert.Equal(new[] { c }, ReviewBuilder.FlawedSet(flawed));
    }

    [Fact]
    public void RawQuestion_ClassicText()
    {
        Assert.Equal("RAW eşleri", ReviewBuilder.RawQuestionTitle);
        Assert.StartsWith("Benzerler'de ve Kusurlu'da ayıkladığın 3 karenin RAW eşi var.", ReviewBuilder.RawQuestion(3));
        Assert.Contains("Evet: RAW'lar da JPG'leriyle birlikte taşınsın (_Benzerler / _Kusurlu).", ReviewBuilder.RawQuestion(3));
    }

    [Fact]
    public void IncludeRaws_SimilarRawFollowsKeeper_FlawedRawToFlawed()
    {
        var (a, b, _) = Pair();
        var bRaw = File("b.CR3", FileKind.Raw, seed: 7);
        var c = File("c.jpg", seed: 5);
        var cRaw = File("c.CR3", FileKind.Raw, seed: 8);
        var files = new List<MediaFile> { a, b, bRaw, c, cRaw };
        var similarOf = new Dictionary<MediaFile, MediaFile> { [b] = a };
        var flawed = new HashSet<MediaFile> { c };

        var raws = ReviewBuilder.PendingRaws(files, similarOf, flawed);
        Assert.Equal(2, raws.Count);
        ReviewBuilder.IncludeRaws(raws, similarOf, flawed);
        Assert.Equal(a, similarOf[bRaw]);
        Assert.Contains(cRaw, flawed);
    }
}
