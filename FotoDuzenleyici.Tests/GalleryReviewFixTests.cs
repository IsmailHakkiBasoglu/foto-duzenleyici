using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Son incelemenin bulguları: atılan dosyalar her çıkış yolunda katalogdan düşer; galeri boş kalmaz.</summary>
public class GalleryReviewFixTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_grf_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();

    public GalleryReviewFixTests()
    {
        Put(@"2026-08\2026-08-Foto\IMG_20260803_a.jpg", 100);
        Put(@"2026-08\2026-08-Foto\IMG_20260804_b.jpg", 200);
        Put(@"2026-09\2026-09-Foto\IMG_20260901_d.jpg", 400);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    void Put(string rel, int size)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    static async Task DiscardA(TimelineViewModel vm)
    {
        vm.OpenMonth(2026, 8);
        var m = vm.Month!;
        await m.LoadAsync();
        m.ToggleMarkCommand.Execute(m.Photos.Single(p => p.Name.EndsWith("a.jpg")));
        m.OpenReviewCommand.Execute(null);
        await m.DiscardCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Discard_ThenGoHome_ReenteredGalleryDoesNotShowDiscarded()
    {
        var shell = new ShellViewModel(_f.Services, _root);
        ((HomeViewModel)shell.CurrentPage).StartTimelineCommand.Execute(null);
        var vm = (TimelineViewModel)shell.CurrentPage;
        await vm.RunningTask!;
        await DiscardA(vm);

        shell.GoHomeCommand.Execute(null);
        ((HomeViewModel)shell.CurrentPage).StartTimelineCommand.Execute(null);
        var again = (TimelineViewModel)shell.CurrentPage;
        await again.RunningTask!;

        Assert.Single(again.Catalog!.FilesOf(2026, 8));
        Assert.DoesNotContain(again.Gallery!.Months.SelectMany(x => x.Photos), p => p.Name.EndsWith("a.jpg"));
    }

    [Fact]
    public async Task Discard_WithoutCatalog_InvalidatesStoredCatalog()
    {
        var store = new CatalogStore();
        store.Set(CatalogReader.Read(_root, TimelineMethod.CaptureDate));
        _f.Settings.Stored.TimelineView = "Table";
        var vm = new TimelineViewModel(_f.Services, _f.Settings.Stored, _root, store);
        await vm.CountAsync(); // klasör adları, tablo: katalog yok
        Assert.Null(vm.Catalog);
        await DiscardA(vm);

        vm.Month!.BackCommand.Execute(null);

        Assert.Null(store.Get(_root, TimelineMethod.CaptureDate));
    }

    [Fact]
    public async Task SwitchToGallery_DuringQuickCount_ReadsCatalog()
    {
        _f.Settings.Stored.TimelineView = "Table";
        var vm = new TimelineViewModel(_f.Services, _f.Settings.Stored, _root);
        var gate = new ManualResetEventSlim();
        vm.BeforeCountForTest = _ => gate.Wait(TimeSpan.FromSeconds(5));
        _ = vm.CountAsync();
        Assert.True(vm.IsCounting);

        vm.View = TimelineViewKind.Gallery;
        gate.Set();
        vm.BeforeCountForTest = null;
        await vm.RunningTask!;

        Assert.NotNull(vm.Catalog);
        Assert.NotNull(vm.Gallery);
    }

    [Fact]
    public void Store_RootUnreadable_NotKept()
    {
        var s = new CatalogStore();
        s.Set(CatalogReader.Read(Path.Combine(_root, "yok"), TimelineMethod.CaptureDate));
        Assert.Null(s.Get(Path.Combine(_root, "yok"), TimelineMethod.CaptureDate));
    }

    [Fact]
    public async Task Gallery_RootMissing_ShowsErrorLine()
    {
        var vm = new TimelineViewModel(_f.Services, _f.Settings.Stored, _root);
        await vm.CountAsync();
        Directory.Delete(_root, true);

        vm.RecountCommand.Execute(null);
        await vm.RunningTask!;

        Assert.Contains(vm.FooterLines, l => l.Contains("okunamadı"));
        Assert.True(vm.Gallery!.IsEmpty);
    }

    [Fact]
    public void OpenClassic_InvalidatesCatalog()
    {
        var shell = new ShellViewModel(_f.Services);
        shell.Catalogs.Set(CatalogReader.Read(_root, TimelineMethod.FolderNames));

        shell.OpenClassicCommand.Execute(null);

        Assert.Null(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }
}
