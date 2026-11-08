using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Yinelenenler sekmesi: hangi kopyanın korunacağı (Plan ile aynı kural) ve kullanıcının seçimi.</summary>
public class DuplicateGroupStateTests
{
    static readonly PlanOptions Opt = new() { DestinationRoot = @"D:\Arsiv" };

    static MediaFile F(string path, long size = 2_097_152, int createdDay = 1) =>
        new() { Path = path, Kind = FileKind.Photo, Size = size, Created = new DateTime(2024, 1, createdDay) };

    static DuplicateGroup G(params MediaFile[] m) => new() { Keeper = m[0], Copies = m.Skip(1).ToList() };

    [Fact]
    public void Keeper_PrefersOrganizedCopy_OverOldest()
    {
        var oldest = F(@"E:\DCIM\a.jpg", createdDay: 1);
        var organized = F(@"D:\Arsiv\2024-01\2024-01-Foto\a.jpg", createdDay: 9);
        Assert.Same(organized, DuplicateKeeper.Choose(G(oldest, organized), Opt));
    }

    [Fact]
    public void Keeper_InDuplicatesFolder_IsNotOrganized()
    {
        var oldest = F(@"E:\DCIM\a.jpg");
        var inDup = F(@"D:\Arsiv\_Tekrarlar\2024-01\a.jpg", createdDay: 9);
        Assert.False(DuplicateKeeper.IsOrganized(inDup, Opt));
        Assert.Same(oldest, DuplicateKeeper.Choose(G(oldest, inDup), Opt));
    }

    [Fact]
    public void Keeper_UserChoiceWins()
    {
        var a = F(@"E:\DCIM\a.jpg");
        var b = F(@"D:\Arsiv\2024-01\2024-01-Foto\a.jpg");
        var c = F(@"F:\Yedek\a.jpg");
        Assert.Same(c, DuplicateKeeper.Choose(G(a, b, c), Opt, new HashSet<MediaFile> { c }));
    }

    [Fact]
    public void IsOrganized_EmptyDestination_IsFalse()
    {
        Assert.False(DuplicateKeeper.IsOrganized(F(@"D:\Arsiv\a.jpg"), new PlanOptions { DestinationRoot = "" }));
    }

    [Fact]
    public void Build_SortsByWastedSpace_AndMarksSuggestedKeeper()
    {
        var small = G(F(@"E:\s1.jpg", 1_000), F(@"E:\s2.jpg", 1_000));
        var big = G(F(@"E:\b1.jpg", 5_000_000), F(@"D:\Arsiv\2024-01\b1.jpg", 5_000_000), F(@"F:\b1.jpg", 5_000_000));
        var states = DuplicateGroupState.Build(new[] { small, big }, Opt);

        Assert.Same(big, states[0].Group);
        Assert.Equal(1, states[0].Number);
        Assert.Equal(10_000_000, states[0].WastedBytes);
        Assert.Equal(new[] { false, true, false }, states[0].Rows.Select(r => r.IsKeeper));
        Assert.True(states[0].Rows[1].Organized);
        Assert.Empty(DuplicateGroupState.UserKeepers(states));
    }

    [Fact]
    public void ChoosingRow_MovesKeeper_AndIsReportedToPlanner()
    {
        var states = DuplicateGroupState.Build(new[] { G(F(@"E:\a.jpg"), F(@"F:\a.jpg")) }, Opt);
        var g = states[0];
        g.Rows[1].IsKeeper = true;

        Assert.Equal(new[] { false, true }, g.Rows.Select(r => r.IsKeeper));
        Assert.True(g.UserChosen);
        Assert.Equal(new[] { g.Rows[1].File }, DuplicateGroupState.UserKeepers(states));
    }

    [Fact]
    public void UncheckingKeeper_IsIgnored_GroupAlwaysKeepsOne()
    {
        var g = DuplicateGroupState.Build(new[] { G(F(@"E:\a.jpg"), F(@"F:\a.jpg")) }, Opt)[0];
        g.Rows[0].IsKeeper = false;
        Assert.Equal(new[] { true, false }, g.Rows.Select(r => r.IsKeeper));
        Assert.False(g.UserChosen);
    }

    [Fact]
    public void Reset_ReturnsToSuggestion_AndRefreshUsesNewDestination()
    {
        var a = F(@"E:\a.jpg");
        var b = F(@"X:\Yeni\2024-01\a.jpg");
        var g = DuplicateGroupState.Build(new[] { G(a, b) }, Opt)[0];
        g.Rows[1].IsKeeper = true;
        g.Reset(Opt);
        Assert.False(g.UserChosen);
        Assert.True(g.Rows[0].IsKeeper);

        // Hedef klasör değişti: kullanıcı seçmediyse öneri yeniden hesaplanır.
        g.Refresh(new PlanOptions { DestinationRoot = @"X:\Yeni" });
        Assert.True(g.Rows[1].IsKeeper);
        Assert.True(g.Rows[1].Organized);
    }

    [Fact]
    public void Planner_HonoursUserKeeper()
    {
        var a = F(@"E:\DCIM\a.jpg");
        var b = F(@"F:\Yedek\a.jpg");
        a.Taken = b.Taken = new DateTime(2024, 1, 5);
        var opt = new PlanOptions { DestinationRoot = @"Z:\out_nonexistent" };
        var plan = Planner.Build(new[] { a, b }, new[] { G(a, b) }, opt, keepDuplicates: new HashSet<MediaFile> { b });

        Assert.Equal(PlanAction.Duplicate, plan.Items.Single(i => i.File == a).Action);
        Assert.Same(b, plan.Items.Single(i => i.File == a).DuplicateOf);
        Assert.Equal(PlanAction.Move, plan.Items.Single(i => i.File == b).Action);
    }

    // Temizle: "öncelikli klasör" kuralı dışarıdan verilir.
    [Fact]
    public void Build_WithPreferred_KeepsPreferred()
    {
        var oldest = F(@"E:\DCIM\a.jpg", createdDay: 1);
        var pref = F(@"F:\Onemli\a.jpg", createdDay: 9);
        var s = Assert.Single(DuplicateGroupState.Build(new[] { G(oldest, pref) }, m => m.Path.StartsWith(@"F:\Onemli")));
        var row = s.Rows.Single(r => r.File == pref);
        Assert.True(row.IsKeeper);
        Assert.True(row.Organized);
        Assert.False(s.Rows.Single(r => r.File == oldest).Organized);
    }

    [Fact]
    public void Reset_Parameterless_UsesPreferred()
    {
        var oldest = F(@"E:\DCIM\a.jpg", createdDay: 1);
        var pref = F(@"F:\Onemli\a.jpg", createdDay: 9);
        var s = Assert.Single(DuplicateGroupState.Build(new[] { G(oldest, pref) }, m => m.Path.StartsWith(@"F:\Onemli")));
        s.Rows.Single(r => r.File == oldest).IsKeeper = true;
        Assert.True(s.UserChosen);
        s.Reset();
        Assert.False(s.UserChosen);
        Assert.True(s.Rows.Single(r => r.File == pref).IsKeeper);
        s.Refresh();
        Assert.True(s.Rows.Single(r => r.File == pref).IsKeeper);
    }

    [Fact]
    public void Build_PlanOptions_Unchanged()
    {
        var oldest = F(@"E:\DCIM\a.jpg", createdDay: 1);
        var organized = F(@"D:\Arsiv\2024-01\2024-01-Foto\a.jpg", createdDay: 9);
        var s = Assert.Single(DuplicateGroupState.Build(new[] { G(oldest, organized) }, Opt));
        Assert.True(s.Rows.Single(r => r.File == organized).IsKeeper);
        s.Reset();
        Assert.True(s.Rows.Single(r => r.File == organized).IsKeeper);
    }
}
