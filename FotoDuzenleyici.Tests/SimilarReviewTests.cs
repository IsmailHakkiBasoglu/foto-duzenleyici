using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

sealed class FakeCompare : ICompare
{
    public int Opened;
    public IReadOnlyList<SimilarGroupState>? Groups;
    public int Start;
    /// <summary>Pencere açıkken yapılacak (karar ver, geri çağır).</summary>
    public Action<IReadOnlyList<SimilarGroupState>, ReviewStore, Action>? During;
    public int ReturnIndex = -1;
    public bool Finished;

    public (SimilarGroupState Current, bool FinishedAll) Open(IReadOnlyList<SimilarGroupState> groups, int start, ReviewStore store, Action changed)
    {
        Opened++; Groups = groups; Start = start;
        During?.Invoke(groups, store, changed);
        return (groups[ReturnIndex < 0 ? start : ReturnIndex], Finished);
    }
}

public class SimilarReviewTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_simrev_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly FakeCompare _compare = new();
    readonly ManualScheduler _clock = new();
    readonly List<string> _warnings = new();
    readonly ReviewStore _store;
    int _changed;

    public SimilarReviewTests()
    {
        Directory.CreateDirectory(_tmp);
        _store = new ReviewStore(Path.Combine(_tmp, "k.json"));
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static MediaFile Photo(string name) => new() { Path = @"C:\f\" + name, Kind = FileKind.Photo };

    /// <summary>İki grup: (a,b) ve (c,d); her satırın karar anahtarı adı.</summary>
    static List<SimilarGroupState> Groups()
    {
        var groups = SimilarGroupState.FromGroups(new[]
        {
            new SimilarGroup { Members = new() { Photo("a.jpg"), Photo("b.jpg") }, Kind = SimilarKind.Burst },
            new SimilarGroup { Members = new() { Photo("c.jpg"), Photo("d.jpg") }, Kind = SimilarKind.Burst },
        });
        foreach (var r in groups.SelectMany(g => g.Rows)) r.StoreKey = r.Name;
        return groups;
    }

    SimilarReviewViewModel Vm(List<SimilarGroupState>? groups = null, bool done = true)
    {
        var services = _f.Services with { Compare = _compare };
        var saver = new ReviewSaver(_store, _clock.Fn, _warnings.Add);
        var vm = new SimilarReviewViewModel(groups ?? Groups(), done, saver, services, () => _changed++, _warnings.Add);
        foreach (var g in vm.Groups) g.Restore(_store);
        return vm;
    }

    [Fact]
    public void Toggle_ReleasesAndStoresKeep()
    {
        var vm = Vm();
        var b = vm.Groups[0].Rows[1];

        vm.ToggleCommand.Execute(b);

        Assert.False(b.Keep);
        Assert.Equal(false, _store.GetKeep("b.jpg"));
        Assert.Equal(1, _changed);
        Assert.Equal(1, vm.ReleasedCount);
        Assert.Equal(1, _clock.Scheduled);
    }

    [Fact]
    public void Toggle_LastKept_BlockedWithWarning()
    {
        var vm = Vm();
        vm.ToggleCommand.Execute(vm.Groups[0].Rows[1]);
        vm.ToggleCommand.Execute(vm.Groups[0].Rows[0]);

        Assert.True(vm.Groups[0].Rows[0].Keep);
        Assert.Equal(new[] { "Her grupta en az bir dosya kalmalı." }, _warnings);
        Assert.Equal(1, vm.ReleasedCount);
    }

    [Fact]
    public void OnlyUnreviewed_FiltersReviewed()
    {
        var vm = Vm();
        Assert.Equal("İncelenen: 0 / 2", vm.CounterText);
        vm.Groups[0].SetReviewed(_store, true);
        Assert.Equal("İncelenen: 1 / 2", vm.CounterText);
        Assert.Equal(2, vm.Visible.Count);

        vm.OnlyUnreviewed = true;

        Assert.Equal(new[] { vm.Groups[1] }, vm.Visible);
    }

    [Fact]
    public void Compare_ChangedCallback_MarksStale()
    {
        var vm = Vm();
        _compare.During = (_, _, changed) => changed();

        vm.OpenCompareCommand.Execute(vm.Groups[1]);

        Assert.Equal(1, _compare.Opened);
        Assert.Equal(1, _compare.Start);
        Assert.Equal(1, _changed);
        Assert.Equal(1, _clock.Scheduled);
    }

    [Fact]
    public void Compare_SetsSelectedToCurrent_AndRefreshesFilter()
    {
        var vm = Vm();
        vm.OnlyUnreviewed = true;
        _compare.ReturnIndex = 1;
        _compare.Finished = true;
        _compare.During = (groups, store, _) => groups[0].SetReviewed(store, true);

        vm.OpenCompareCommand.Execute(vm.Groups[0]);

        Assert.Same(vm.Groups[1], vm.Selected);
        Assert.Equal(new[] { vm.Groups[1] }, vm.Visible);
        Assert.Contains("Bütün gruplar gözden geçirildi.", _warnings);
    }

    [Fact]
    public void Toggle_DoesNotRebuildVisibleList()
    {
        var vm = Vm();
        vm.OnlyUnreviewed = true; // her okumada yeni liste: değişiklik bildirimi listeyi baştan kurar, kaydırma başa döner
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.ToggleCommand.Execute(vm.Groups[0].Rows[1]);
        Assert.DoesNotContain(nameof(vm.Visible), raised);
        Assert.Contains(nameof(vm.ReleasedCount), raised);
    }

    [Fact]
    public void Detach_StopsCallbacks()
    {
        var vm = Vm();
        vm.Detach();
        vm.Groups[0].Rows[1].Keep = false;
        Assert.Equal(0, _changed);
        Assert.Null(_store.GetKeep("b.jpg"));
    }

    [Fact]
    public void NotDone_EmptyState()
    {
        var vm = Vm(new List<SimilarGroupState>(), done: false);
        Assert.False(vm.Done);
        Assert.Equal("Benzerlik araması yapılmadı.", vm.EmptyText);
    }
}
