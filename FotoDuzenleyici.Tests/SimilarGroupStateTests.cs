using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Benzer grubunun arayüz durumu: en az bir kare kalsın, yalnız bu kalsın, kararların geri yüklenmesi.</summary>
public class SimilarGroupStateTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_state_" + Guid.NewGuid().ToString("N"));
    public SimilarGroupStateTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    ReviewStore Store() => new(Path.Combine(_tmp, "kararlar.json"));

    static MediaFile Photo(string name) => new()
    {
        Path = @"C:\f\" + name, Kind = FileKind.Photo, Size = 1000, Taken = new DateTime(2024, 3, 10, 9, 15, 30),
        DateSource = DateSource.Exif, Make = "Canon", Model = "EOS 250D", Width = 6000, Height = 4000,
    };

    /// <summary>Tek grup; üyelere sırayla "a", "b", "c"… anahtarı verilir.</summary>
    static SimilarGroupState Group(int count, int? suggested = null, SimilarKind kind = SimilarKind.Burst)
    {
        var members = Enumerable.Range(0, count).Select(i => Photo($"IMG_{i:0000}.JPG")).ToList();
        var g = new SimilarGroup { Members = members, Kind = kind, Suggested = suggested is { } s ? members[s] : null };
        var state = Assert.Single(SimilarGroupState.FromGroups(new[] { g }));
        for (int i = 0; i < count; i++) state.Rows[i].StoreKey = ((char)('a' + i)).ToString();
        return state;
    }

    [Fact]
    public void FromGroups_NumbersFromOne_MarksSuggested()
    {
        var a = Photo("1.jpg"); var b = Photo("2.jpg"); var c = Photo("3.jpg"); var d = Photo("4.jpg");
        var states = SimilarGroupState.FromGroups(new[]
        {
            new SimilarGroup { Members = new() { a, b }, Kind = SimilarKind.ResizedCopy, Suggested = b },
            new SimilarGroup { Members = new() { c, d }, Kind = SimilarKind.Burst },
        });
        Assert.Equal(new[] { 1, 2 }, states.Select(s => s.Number));
        Assert.True(states[0].Rows[1].Suggested);
        Assert.False(states[0].Rows[0].Suggested);
        Assert.All(states[1].Rows, r => Assert.Equal(2, r.Group));
    }

    [Fact]
    public void Release_LastKept_IsRefused()
    {
        var g = Group(2);
        g.Rows[0].Keep = false;
        Assert.False(g.Rows[0].Keep);
        g.Rows[1].Keep = false;
        Assert.True(g.Rows[1].Keep);
        Assert.Equal(1, g.KeptCount);
    }

    [Fact]
    public void KeepOnly_ReleasesOthers()
    {
        var g = Group(3);
        g.KeepOnly(g.Rows[1]);
        Assert.Equal(new[] { false, true, false }, g.Rows.Select(r => r.Keep));
    }

    [Fact]
    public void Restore_AppliesSavedKeeps_AndReviewed()
    {
        var store = Store();
        store.SetKeep("a", false);
        store.SetReviewed(ReviewStore.Signature(new[] { "a", "b" }), true);
        var g = Group(2);
        int changed = 0;
        foreach (var r in g.Rows) r.Changed = _ => changed++;

        g.Restore(store);
        Assert.False(g.Rows[0].Keep);
        Assert.True(g.Rows[1].Keep);
        Assert.True(g.Reviewed);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Restore_AllSavedFalse_KeepsSuggestedOrFirst()
    {
        var store = Store();
        store.SetKeep("a", false);
        store.SetKeep("b", false);

        var withStar = Group(2, suggested: 1, kind: SimilarKind.ResizedCopy);
        withStar.Restore(store);
        Assert.Equal(new[] { false, true }, withStar.Rows.Select(r => r.Keep));

        var burst = Group(2);
        burst.Restore(store);
        Assert.Equal(new[] { true, false }, burst.Rows.Select(r => r.Keep));
    }

    [Fact]
    public void Restore_MembersChanged_NotReviewed_ButKeepsMarks()
    {
        var store = Store();
        store.SetKeep("a", false);
        store.SetReviewed(ReviewStore.Signature(new[] { "a", "b" }), true);
        var g = Group(3);
        g.Restore(store);
        Assert.False(g.Reviewed);
        Assert.False(g.Rows[0].Keep);
    }

    [Fact]
    public void Restore_MissingKey_NoSignature_NoCrash()
    {
        var store = Store();
        var g = Group(2);
        g.Rows[1].StoreKey = null;
        g.Restore(store);
        Assert.Null(g.Signature);
        Assert.False(g.Reviewed);
        g.SetReviewed(store, true);
        Assert.False(g.Reviewed);
    }

    [Fact]
    public void SetReviewed_PersistsUnderSignature()
    {
        var store = Store();
        var g = Group(2);
        g.Restore(store);
        g.SetReviewed(store, true);
        Assert.True(g.Reviewed);
        Assert.True(store.IsReviewed(ReviewStore.Signature(new[] { "a", "b" })));
    }

    // --- Netlik: seri çekimde göreli sıralama ---

    static Dictionary<MediaFile, QualityScore> Scores(SimilarGroupState g, params double[] sharp) =>
        g.Rows.Select((r, i) => (r.File, s: new QualityScore(sharp[i], 120, 0, false))).ToDictionary(x => x.File, x => x.s);

    [Fact]
    public void ApplyBurstRanks_SetsPercentStarAndSoft_OnlyForBurst()
    {
        var burst = Group(3);
        burst.ApplyBurstRanks(Scores(burst, 100, 40, 90));
        Assert.Equal(new int?[] { 100, 40, 90 }, burst.Rows.Select(r => r.SharpPercent));
        Assert.Equal(new[] { "◎", "", "" }, burst.Rows.Select(r => r.Star));
        Assert.Equal(new string?[] { null, "BULANIK", null }, burst.Rows.Select(r => r.Flaw));

        var same = Group(2, suggested: 0, kind: SimilarKind.SameFrame);
        same.ApplyBurstRanks(Scores(same, 100, 10));
        Assert.All(same.Rows, r => { Assert.Null(r.SharpPercent); Assert.Null(r.Flaw); Assert.False(r.Sharpest); });
        Assert.Equal("★", same.Rows[0].Star);
    }

    [Fact]
    public void ReleaseSoft_ReleasesOnlySoftFrames_NeverSharpest()
    {
        var g = Group(3);
        g.ApplyBurstRanks(Scores(g, 100, 40, 90));
        var changed = new List<SimilarRow>();
        foreach (var r in g.Rows) r.Changed = changed.Add;
        Assert.Equal(1, g.ReleaseSoft());
        Assert.Equal(new[] { true, false, true }, g.Rows.Select(r => r.Keep));
        Assert.Same(g.Rows[1], Assert.Single(changed));
        Assert.Equal(0, g.ReleaseSoft()); // ikinci kez: zaten ayıklanmış
    }

    [Fact]
    public void ReleaseSoft_KeepsLastFrame_WhenSharpestWasReleasedByHand()
    {
        var g = Group(2);
        g.ApplyBurstRanks(Scores(g, 100, 30));
        g.Rows[1].Keep = true;
        g.Rows[0].Keep = false; // kullanıcı en neti ayıkladı
        Assert.Equal(0, g.ReleaseSoft());
        Assert.True(g.Rows[1].Keep);
    }
}
