using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Benzer gruplarında verilen kararların (kalsın / ayıkla, incelendi) kalıcı saklanması.</summary>
public class ReviewStoreTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_review_" + Guid.NewGuid().ToString("N"));
    readonly string _path;

    public ReviewStoreTests()
    {
        Directory.CreateDirectory(_tmp);
        _path = Path.Combine(_tmp, "kararlar", "benzer-kararlar.json");
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var sig = ReviewStore.Signature(new[] { "a", "b" });
        var s = new ReviewStore(_path);
        s.SetKeep("a", false);
        s.SetKeep("b", true);
        s.SetReviewed(sig, true);
        s.Save();

        var again = new ReviewStore(_path);
        Assert.False(again.LoadFailed);
        Assert.False(again.GetKeep("a"));
        Assert.True(again.GetKeep("b"));
        Assert.Null(again.GetKeep("c"));
        Assert.True(again.IsReviewed(sig));
    }

    [Fact]
    public void Load_MissingFile_StartsEmpty()
    {
        var s = new ReviewStore(_path);
        Assert.False(s.LoadFailed);
        Assert.Null(s.GetKeep("a"));
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ yarım");
        var s = new ReviewStore(_path);
        Assert.True(s.LoadFailed);
        Assert.Null(s.GetKeep("a"));

        s.SetKeep("a", false);
        s.Save();
        Assert.False(new ReviewStore(_path).GetKeep("a"));
    }

    [Fact]
    public void Signature_IndependentOfOrder_ChangesWithMembers()
    {
        Assert.Equal(ReviewStore.Signature(new[] { "a", "b" }), ReviewStore.Signature(new[] { "b", "a" }));
        Assert.NotEqual(ReviewStore.Signature(new[] { "a", "b" }), ReviewStore.Signature(new[] { "a", "b", "c" }));
    }

    [Fact]
    public void Key_SurvivesMoveAndRename()
    {
        var src = Path.Combine(_tmp, "a", "IMG_0001.JPG");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "goruntu");
        File.SetLastWriteTime(src, new DateTime(2024, 3, 10, 9, 15, 30));
        var s = new ReviewStore(_path);
        s.SetKeep(HashCache.Key(src), false);

        var moved = Path.Combine(_tmp, "b", "2024-03-10 IMG_0001 Canon.JPG");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(src, moved);
        Assert.False(s.GetKeep(HashCache.Key(moved)));
    }

    [Fact]
    public void SetReviewedFalse_Removes()
    {
        var s = new ReviewStore(_path);
        s.SetReviewed("x", true);
        s.SetReviewed("x", false);
        Assert.False(s.IsReviewed("x"));
    }
}
