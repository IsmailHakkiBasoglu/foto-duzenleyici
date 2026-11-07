using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class FlawedReviewTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_flawrev_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly FakeCompare _compare = new();
    readonly ManualScheduler _clock = new();
    readonly List<string> _warnings = new();
    readonly ReviewStore _store;
    int _changed;

    public FlawedReviewTests()
    {
        Directory.CreateDirectory(_tmp);
        _store = new ReviewStore(Path.Combine(_tmp, "k.json"));
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static MediaFile Photo(string name, int day) => new()
    {
        Path = @"C:\f\" + name, Kind = FileKind.Photo, Taken = new DateTime(2024, 3, day), DateSource = DateSource.Exif,
    };

    static readonly QualityScore Blurry = new(5, 120, 0, false);
    static readonly QualityScore Dark = new(400, 10, 0.9, false);
    static readonly QualityScore Both = new(5, 10, 0.9, false);

    FlawedReviewViewModel Vm(bool done = true)
    {
        var scores = new Dictionary<MediaFile, QualityScore>
        {
            [Photo("bulanik.jpg", 1)] = Blurry, [Photo("karanlik.jpg", 2)] = Dark, [Photo("ikisi.jpg", 3)] = Both,
        };
        var list = done ? FlawedList.Build(scores, Array.Empty<SimilarGroupState>(), 1) : new List<SimilarGroupState>();
        foreach (var r in list.SelectMany(g => g.Rows)) r.StoreKey = r.Name;
        var saver = new ReviewSaver(_store, _clock.Fn, _warnings.Add);
        return new FlawedReviewViewModel(list, done, saver, _f.Services with { Compare = _compare }, () => _changed++, _warnings.Add);
    }

    [Fact]
    public void Toggle_ReleasesSingleFrame()
    {
        var vm = Vm();
        var row = vm.Groups[0].Rows[0];
        vm.ToggleCommand.Execute(row);
        Assert.False(row.Keep);
        Assert.Empty(_warnings);
        Assert.Equal(false, _store.GetKeep(row.Name));
        Assert.Equal(1, _changed);
        Assert.Equal(1, vm.ReleasedCount);
    }

    [Fact]
    public void Toggle_DoesNotRebuildVisibleList()
    {
        var vm = Vm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.ToggleCommand.Execute(vm.Groups[0].Rows[0]);
        Assert.DoesNotContain(nameof(vm.Visible), raised);
        Assert.Contains(nameof(vm.CounterText), raised);
    }

    [Fact]
    public void Filters_BlurryDark()
    {
        var vm = Vm();
        Assert.Equal(3, vm.Visible.Count);
        vm.ShowDark = false;
        Assert.Equal(new[] { "bulanik.jpg", "ikisi.jpg" }, vm.Visible.Select(g => g.Rows[0].Name));
        vm.ShowDark = true; vm.ShowBlurry = false;
        Assert.Equal(new[] { "karanlik.jpg", "ikisi.jpg" }, vm.Visible.Select(g => g.Rows[0].Name));
    }

    [Fact]
    public void Counter_Text()
    {
        var vm = Vm();
        Assert.Equal("Bulanık: 2  ·  Karanlık: 2  ·  Ayıklanacak: 0 / 3", vm.CounterText);
        vm.ToggleCommand.Execute(vm.Groups[1].Rows[0]);
        Assert.Equal("Bulanık: 2  ·  Karanlık: 2  ·  Ayıklanacak: 1 / 3", vm.CounterText);
        Assert.Equal("Kusurlu kare yok.", new FlawedReviewViewModel(new(), true,
            new ReviewSaver(_store, _clock.Fn, _warnings.Add), _f.Services, () => { }, _warnings.Add).CounterText);
    }

    [Fact]
    public void Compare_ChangedCallback_MarksStale()
    {
        var vm = Vm();
        _compare.During = (_, _, changed) => changed();
        _compare.Finished = true;
        vm.OpenCompareCommand.Execute(vm.Groups[2]);
        Assert.Equal(2, _compare.Start);
        Assert.Equal(1, _changed);
        Assert.Contains("Bütün kusurlu kareler gözden geçirildi.", _warnings);
    }

    [Fact]
    public void NotDone_EmptyState()
    {
        var vm = Vm(done: false);
        Assert.Equal("Netlik ölçülmedi.", vm.EmptyText);
    }
}
