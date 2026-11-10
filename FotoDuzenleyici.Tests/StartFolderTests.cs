using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Sağ tık "Foto Düzenleyici ile aç": klasör argümanı ana ekranda seçili gelir.</summary>
public class StartFolderTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_start_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();

    public StartFolderTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static readonly Func<string, bool> Dir = Directory.Exists;

    [Fact]
    public void Args_FirstExistingFolder()
    {
        Assert.Equal(_tmp, StartArgs.Folder(new[] { "--klasik", Path.Combine(_tmp, "yok"), _tmp }, Dir));
    }

    [Fact]
    public void Args_NoFolder_Null()
    {
        Assert.Null(StartArgs.Folder(Array.Empty<string>(), Dir));
        Assert.Null(StartArgs.Folder(new[] { "--klasik" }, Dir));
        var file = Path.Combine(_tmp, "a.jpg");
        File.WriteAllText(file, "x");
        Assert.Null(StartArgs.Folder(new[] { file }, Dir));
    }

    [Fact]
    public void Args_DriveRootWithStrayQuote()
    {
        // Explorer "%1" sürücü kökünde: "D:\" → argüman D:" olur.
        var root = Path.GetPathRoot(_tmp)!; // ör. C:\
        var broken = root.TrimEnd('\\') + "\"";
        Assert.Equal(root, StartArgs.Folder(new[] { broken }, Dir));
    }

    HomeViewModel Home(ShellViewModel s) => (HomeViewModel)s.CurrentPage;

    [Fact]
    public void Home_ShowsSelectedFolder()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        Assert.Equal(_tmp, Home(shell).SelectedFolder);
        Assert.True(Home(shell).HasSelectedFolder);
        Assert.False(Home(new ShellViewModel(_f.Services)).HasSelectedFolder);
    }

    [Fact]
    public void OrganizeCard_UsesSelectedFolder_AndLastArchive()
    {
        _f.Settings.Stored.Recent.Add(new RecentPair(new() { @"X:\eski" }, @"X:\arsiv", DateTime.Now));
        var shell = new ShellViewModel(_f.Services, _tmp);

        Home(shell).OrganizeCardCommand.Execute(null);

        var flow = Assert.IsType<OrganizeFlowViewModel>(shell.CurrentPage);
        Assert.Equal(FlowKind.Organize, flow.Kind);
        Assert.Equal(new[] { _tmp }, flow.Folders.Sources.Select(s => s.Path));
        Assert.Equal(@"X:\arsiv", flow.Folders.Archive);
    }

    [Fact]
    public void CleanCard_UsesSelectedFolder()
    {
        _f.Settings.Stored.RecentClean.Add(new RecentPair(new() { @"X:\temiz" }, "", DateTime.Now));
        var shell = new ShellViewModel(_f.Services, _tmp);

        Home(shell).CleanCardCommand.Execute(null);

        var flow = Assert.IsType<OrganizeFlowViewModel>(shell.CurrentPage);
        Assert.Equal(FlowKind.Clean, flow.Kind);
        Assert.Equal(new[] { _tmp }, flow.Folders.Sources.Select(s => s.Path));
    }

    [Fact]
    public void Cards_WithoutSelection_UseLast()
    {
        _f.Settings.Stored.Recent.Add(new RecentPair(new() { @"X:\eski" }, @"X:\arsiv", DateTime.Now));
        var shell = new ShellViewModel(_f.Services);

        Home(shell).OrganizeCardCommand.Execute(null);

        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        Assert.Equal(new[] { @"X:\eski" }, flow.Folders.Sources.Select(s => s.Path));
    }

    [Fact]
    public void RecentOpen_IgnoresSelection()
    {
        var pair = new RecentPair(new() { @"X:\eski" }, @"X:\arsiv", DateTime.Now);
        _f.Settings.Stored.Recent.Add(pair);
        var shell = new ShellViewModel(_f.Services, _tmp);

        Home(shell).StartOrganizeCommand.Execute(pair);

        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        Assert.Equal(new[] { @"X:\eski" }, flow.Folders.Sources.Select(s => s.Path));
    }

    [Fact]
    public void Clear_HidesAndForgets()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        Home(shell).ClearSelectedFolderCommand.Execute(null);

        Assert.False(Home(shell).HasSelectedFolder);
        shell.GoHomeCommand.Execute(null);
        Assert.False(Home(shell).HasSelectedFolder);
    }

    [Fact]
    public void AfterFlowStarted_HomeForgetsSelection()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        Home(shell).CleanCardCommand.Execute(null);

        shell.GoHomeCommand.Execute(null);

        Assert.False(Home(shell).HasSelectedFolder);
    }
}
