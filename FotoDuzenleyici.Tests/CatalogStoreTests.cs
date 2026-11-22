using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Oturum kataloğu deposu: uygulama açık kaldıkça tek katalog; dosya taşıyan işler başlayınca bırakılır.</summary>
public class CatalogStoreTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_cst_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();

    public CatalogStoreTests()
    {
        var p = Path.Combine(_root, "2026-08", "IMG_20260803_a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[3]);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    MediaCatalog Catalog(TimelineMethod m = TimelineMethod.FolderNames) => CatalogReader.Read(_root, m);

    [Fact]
    public void Get_SameRootAndMethod_ReturnsStored()
    {
        var s = new CatalogStore();
        var c = Catalog();
        s.Set(c);
        Assert.Same(c, s.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void Get_OtherRootOrMethod_Null()
    {
        var s = new CatalogStore();
        s.Set(Catalog());
        Assert.Null(s.Get(_root, TimelineMethod.CaptureDate));
        Assert.Null(s.Get(Path.Combine(_root, "2026-08"), TimelineMethod.FolderNames));
    }

    [Fact]
    public void Get_RootWithTrailingSlashOrOtherCase_Matches()
    {
        var s = new CatalogStore();
        var c = Catalog();
        s.Set(c);
        Assert.Same(c, s.Get(_root.ToUpperInvariant() + Path.DirectorySeparatorChar, TimelineMethod.FolderNames));
    }

    [Fact]
    public void Invalidate_ThenGet_Null()
    {
        var s = new CatalogStore();
        s.Set(Catalog());
        s.Invalidate();
        Assert.Null(s.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void Set_Cancelled_NotStored()
    {
        var s = new CatalogStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        s.Set(CatalogReader.Read(_root, TimelineMethod.FolderNames, ct: cts.Token));
        Assert.Null(s.Get(_root, TimelineMethod.FolderNames));
    }

    ShellViewModel ShellWithCatalog()
    {
        var shell = new ShellViewModel(_f.Services);
        shell.Catalogs.Set(Catalog());
        return shell;
    }

    HomeViewModel Home(ShellViewModel s) => Assert.IsType<HomeViewModel>(s.CurrentPage);

    [Fact]
    public void StartOrganize_InvalidatesCatalog()
    {
        var shell = ShellWithCatalog();
        Home(shell).StartOrganizeCommand.Execute(null);
        Assert.Null(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void StartClean_InvalidatesCatalog()
    {
        var shell = ShellWithCatalog();
        Home(shell).StartCleanCommand.Execute(null);
        Assert.Null(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public async Task Undo_InvalidatesCatalog()
    {
        var shell = ShellWithCatalog();
        _f.Files.Journal = Path.Combine(_root, "islem.jsonl");
        File.WriteAllText(_f.Files.Journal, "");
        _f.Dialogs.Answer = true;

        await shell.UndoCommand.ExecuteAsync(null);

        Assert.Null(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void Undo_Declined_KeepsCatalog()
    {
        var shell = ShellWithCatalog();
        _f.Files.Journal = Path.Combine(_root, "islem.jsonl");
        File.WriteAllText(_f.Files.Journal, "");
        _f.Dialogs.Answer = false;

        shell.UndoCommand.Execute(null);

        Assert.NotNull(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void StartBackup_KeepsCatalog()
    {
        var shell = ShellWithCatalog();
        Home(shell).StartBackupCommand.Execute(null);
        Assert.NotNull(shell.Catalogs.Get(_root, TimelineMethod.FolderNames));
    }

    [Fact]
    public void Timeline_ReenteredSameSession_GetsSameStore()
    {
        var shell = new ShellViewModel(_f.Services);
        Home(shell).StartTimelineCommand.Execute(null);
        var a = Assert.IsType<TimelineViewModel>(shell.CurrentPage);
        shell.GoHomeCommand.Execute(null);
        Home(shell).StartTimelineCommand.Execute(null);
        var b = Assert.IsType<TimelineViewModel>(shell.CurrentPage);

        Assert.NotSame(a, b);
        Assert.Same(shell.Catalogs, a.Catalogs);
        Assert.Same(shell.Catalogs, b.Catalogs);
    }
}
