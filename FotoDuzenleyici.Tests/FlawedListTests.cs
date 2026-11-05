using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Arşiv genelinde kusurlu kareler: tek kareli gruplar, Benzerler'dekiler hariç, kararlar kalıcı.</summary>
public class FlawedListTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_flaw_" + Guid.NewGuid().ToString("N"));
    public FlawedListTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static MediaFile Photo(string name, int day) => new()
    {
        Path = @"C:\f\" + name, Kind = FileKind.Photo, Taken = new DateTime(2024, 3, day, 9, 0, 0), DateSource = DateSource.Exif,
    };

    static readonly QualityScore Sharp = new(400, 120, 0, false);
    static readonly QualityScore Blurry = new(5, 120, 0, false);
    static readonly QualityScore Dark = new(400, 10, 0.9, false);

    [Fact]
    public void Build_ListsBlurryAndDark_SortedByDate_ExcludingSimilarMembers()
    {
        var a = Photo("a.jpg", 5); var b = Photo("b.jpg", 2); var c = Photo("c.jpg", 3); var d = Photo("d.jpg", 1);
        var inBurst = Photo("e.jpg", 4);
        var burst = SimilarGroupState.FromGroups(new[]
        {
            new SimilarGroup { Members = new() { inBurst, Photo("f.jpg", 4) }, Kind = SimilarKind.Burst },
        });
        var scores = new Dictionary<MediaFile, QualityScore> { [a] = Blurry, [b] = Dark, [c] = Sharp, [d] = Blurry, [inBurst] = Blurry };

        var list = FlawedList.Build(scores, burst, firstNumber: 2);
        Assert.Equal(new[] { "d.jpg", "b.jpg", "a.jpg" }, list.Select(g => Assert.Single(g.Rows).Name));
        Assert.Equal(new[] { 2, 3, 4 }, list.Select(g => g.Number));
        Assert.All(list, g => Assert.Equal(SimilarKind.Flawed, g.Kind));
        Assert.Equal("Kusurlu", list[0].KindText);
    }

    [Fact]
    public void Build_FlawText_OnRow()
    {
        var a = Photo("a.jpg", 1); var b = Photo("b.jpg", 2);
        var list = FlawedList.Build(new Dictionary<MediaFile, QualityScore> { [a] = Blurry, [b] = Dark },
            Array.Empty<SimilarGroupState>(), 1);
        Assert.Equal(new[] { "BULANIK", "KARANLIK" }, list.Select(g => g.Rows[0].Flaw));
    }

    // Gözden geçirme odağı: tek kareli grupta "en az bir kare kalsın" kuralı ayıklamayı engellememeli.
    [Fact]
    public void Build_GroupsAllowRelease_AndStayReleasedAfterRestore()
    {
        var store = new ReviewStore(Path.Combine(_tmp, "kararlar.json"));
        var a = Photo("a.jpg", 1);
        var scores = new Dictionary<MediaFile, QualityScore> { [a] = Blurry };

        var g = Assert.Single(FlawedList.Build(scores, Array.Empty<SimilarGroupState>(), 1));
        g.Rows[0].StoreKey = "k";
        g.Rows[0].Changed = r => store.SetKeep(r.StoreKey!, r.Keep);
        g.Rows[0].Keep = false;
        Assert.False(g.Rows[0].Keep);

        var again = Assert.Single(FlawedList.Build(scores, Array.Empty<SimilarGroupState>(), 1));
        again.Rows[0].StoreKey = "k";
        again.Restore(store);
        Assert.False(again.Rows[0].Keep);
    }
}
