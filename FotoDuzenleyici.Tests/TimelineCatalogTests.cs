using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Zaman çizelgesi oturum kataloğunu kullanır: bir kez okur, Yeniden tara ile tazeler, atılanları düşer.</summary>
public class TimelineCatalogTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_tlc_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    int _reads;

    public TimelineCatalogTests()
    {
        Put(@"2026-08\2026-08-Foto\IMG_20260803_a.jpg", 100);
        Put(@"2026-08\2026-08-Foto\IMG_20260804_b.jpg", 200);
        Put(@"2026-08\2026-08-Foto\IMG_20260805_c.jpg", 300);
        Put(@"2026-09\2026-09-Foto\IMG_20260901_d.jpg", 400);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    void Put(string rel, int size)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    TimelineViewModel New(TimelineViewKind view, CatalogStore? store = null)
    {
        _f.Settings.Stored.TimelineView = view.ToString();
        var vm = new TimelineViewModel(_f.Services, _f.Settings.Stored, _root, store);
        vm.ReadForTest = p => { Interlocked.Increment(ref _reads); return Scanner.ReadFile(p); };
        return vm;
    }

    static TimelineCell Aug(TimelineViewModel vm) => vm.Table!.Rows.Single(r => r.Year == 2026).Months[7];

    [Fact]
    public async Task CaptureDate_ReadsOnce_SecondCountUsesStore()
    {
        var store = new CatalogStore();
        var vm = New(TimelineViewKind.Table, store);
        vm.Method = TimelineMethod.CaptureDate;
        await vm.RunningTask!;
        Assert.Equal(4, _reads);

        var again = New(TimelineViewKind.Table, store);
        again.Method = TimelineMethod.CaptureDate;
        await again.CountAsync();

        Assert.Equal(4, _reads);
        Assert.Equal(4, again.Table!.Total);
        Assert.NotNull(again.Catalog);
    }

    [Fact]
    public async Task FolderNames_TableView_NoCatalogRead()
    {
        var vm = New(TimelineViewKind.Table);
        await vm.CountAsync();

        Assert.Equal(0, _reads);
        Assert.Null(vm.Catalog);
        Assert.Equal(4, vm.Table!.Total);
    }

    [Fact]
    public async Task GalleryView_ReadsCatalog_TableMatches()
    {
        var vm = New(TimelineViewKind.Gallery);
        vm.Filter = KindFilter.All;
        await vm.CountAsync();

        Assert.Equal(4, _reads);
        Assert.Equal(vm.Catalog!.Items.Count, vm.Table!.Total);
    }

    [Fact]
    public async Task SwitchToGallery_ReadsCatalog()
    {
        var vm = New(TimelineViewKind.Table);
        await vm.CountAsync();
        Assert.Null(vm.Catalog);

        vm.View = TimelineViewKind.Gallery;
        await vm.RunningTask!;

        Assert.NotNull(vm.Catalog);
        Assert.Equal(4, _reads);
    }

    [Fact]
    public async Task Recount_InvalidatesAndRereads()
    {
        var store = new CatalogStore();
        var vm = New(TimelineViewKind.Gallery, store);
        await vm.CountAsync();
        Put(@"2026-09\2026-09-Foto\IMG_20260902_e.jpg", 50);

        vm.RecountCommand.Execute(null);
        await vm.RunningTask!;

        Assert.Equal(9, _reads);
        Assert.Equal(5, vm.Table!.Total);
        Assert.Same(vm.Catalog, store.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public async Task OpenCell_WithCatalog_MonthDoesNotReread()
    {
        var vm = New(TimelineViewKind.Gallery);
        await vm.CountAsync();

        vm.OpenCellCommand.Execute(Aug(vm));
        await vm.Month!.LoadAsync();

        Assert.Equal(4, _reads);
        Assert.Equal(3, vm.Month.Photos.Count);
    }

    [Fact]
    public async Task OpenMonth_ByNumber_OpensThatMonth()
    {
        var vm = New(TimelineViewKind.Gallery);
        await vm.CountAsync();

        vm.OpenMonth(2026, 9);

        Assert.Equal((2026, 9), (vm.Month!.Year, vm.Month.MonthNumber));
    }

    [Fact]
    public async Task Discard_InMonth_RemovedFromCatalogAndGallery()
    {
        var vm = New(TimelineViewKind.Gallery);
        await vm.CountAsync();
        int changed = 0;
        vm.CatalogChanged += () => changed++;
        vm.OpenCellCommand.Execute(Aug(vm));
        var m = vm.Month!;
        await m.LoadAsync();
        m.ToggleMarkCommand.Execute(m.Photos.Single(p => p.Name.EndsWith("a.jpg")));
        m.OpenReviewCommand.Execute(null);
        await m.DiscardCommand.ExecuteAsync(null);

        m.BackCommand.Execute(null);

        Assert.Equal(2, vm.Catalog!.FilesOf(2026, 8).Count);
        Assert.Equal(2, Aug(vm).Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task ChangeFolder_WhileReading_OldCatalogNotStored()
    {
        var store = new CatalogStore();
        var vm = New(TimelineViewKind.Gallery, store);
        var other = Path.Combine(_root, "2026-09");
        vm.BeforeCountForTest = f => { if (f == _root) vm.Folder = other; };
        _ = vm.CountAsync();
        // Folder değişimi yeni sayımı başlatır; ikisinin de bitmesi beklenir.
        await Task.Delay(50);
        await vm.RunningTask!;
        await Task.Delay(50);

        Assert.Null(store.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void View_Persisted()
    {
        var vm = New(TimelineViewKind.Gallery);
        vm.View = TimelineViewKind.Table;
        Assert.Equal("Table", _f.Settings.Stored.TimelineView);
    }
}
