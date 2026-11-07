using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Benzerler toplu düğmeleri: Önerileri uygula, Bulanıkları ayıkla, Hepsini koru (klasikteki davranış).</summary>
public class SimilarBulkTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_bulk_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly ManualScheduler _clock = new();
    readonly List<string> _warnings = new();
    readonly ReviewStore _store;
    int _changed;

    public SimilarBulkTests()
    {
        Directory.CreateDirectory(_tmp);
        _store = new ReviewStore(Path.Combine(_tmp, "k.json"));
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static MediaFile Photo(string name) => new() { Path = @"C:\f\" + name, Kind = FileKind.Photo };

    /// <summary>Grup 0: ★'lı küçültülmüş kopya (a★, b). Grup 1: seri çekim (c en net, d bulanık, e bulanık).</summary>
    SimilarReviewViewModel Vm()
    {
        var a = Photo("a.jpg");
        var groups = SimilarGroupState.FromGroups(new[]
        {
            new SimilarGroup { Members = new() { a, Photo("b.jpg") }, Kind = SimilarKind.ResizedCopy, Suggested = a },
            new SimilarGroup { Members = new() { Photo("c.jpg"), Photo("d.jpg"), Photo("e.jpg") }, Kind = SimilarKind.Burst },
        });
        foreach (var r in groups.SelectMany(g => g.Rows)) r.StoreKey = r.Name;
        var burst = groups[1].Rows;
        burst[0].Sharpest = true;
        burst[1].Flaw = "BULANIK";
        burst[2].Flaw = "BULANIK";
        var saver = new ReviewSaver(_store, _clock.Fn, _warnings.Add);
        var vm = new SimilarReviewViewModel(groups, true, saver, _f.Services, () => _changed++, _warnings.Add);
        foreach (var g in vm.Groups) g.Restore(_store);
        return vm;
    }

    static SimilarRow Row(SimilarReviewViewModel vm, string name) => vm.Groups.SelectMany(g => g.Rows).Single(r => r.Name == name);

    [Fact]
    public void ApplySuggestions_KeepsOnlyStar_BurstUntouched()
    {
        var vm = Vm();
        vm.ApplySuggestionsCommand.Execute(null);
        Assert.True(Row(vm, "a.jpg").Keep);
        Assert.False(Row(vm, "b.jpg").Keep);
        Assert.All(vm.Groups[1].Rows, r => Assert.True(r.Keep));
        Assert.Equal(false, _store.GetKeep("b.jpg"));
        Assert.True(_changed > 0);
    }

    [Fact]
    public void ReleaseSoft_ReleasesBlurryBurstFrames_AndReports()
    {
        var vm = Vm();
        vm.ReleaseSoftCommand.Execute(null);
        Assert.True(Row(vm, "c.jpg").Keep);
        Assert.False(Row(vm, "d.jpg").Keep);
        Assert.False(Row(vm, "e.jpg").Keep);
        Assert.Contains("2 bulanık kare ayıklandı.", _warnings);
        Assert.Equal(2, vm.ReleasedCount);
    }

    [Fact]
    public void ReleaseSoft_ReviewedGroupUntouched_ReportsNone()
    {
        var vm = Vm();
        vm.Groups[1].SetReviewed(_store, true);
        vm.ReleaseSoftCommand.Execute(null);
        Assert.All(vm.Groups[1].Rows, r => Assert.True(r.Keep));
        Assert.Contains("Ayıklanacak bulanık kare yok.", _warnings);
    }

    [Fact]
    public void KeepAll_KeepsEverything()
    {
        var vm = Vm();
        vm.ApplySuggestionsCommand.Execute(null);
        vm.ReleaseSoftCommand.Execute(null);
        vm.KeepAllCommand.Execute(null);
        Assert.All(vm.Groups.SelectMany(g => g.Rows), r => Assert.True(r.Keep));
        Assert.Equal(0, vm.ReleasedCount);
        Assert.Equal(true, _store.GetKeep("b.jpg"));
    }
}
