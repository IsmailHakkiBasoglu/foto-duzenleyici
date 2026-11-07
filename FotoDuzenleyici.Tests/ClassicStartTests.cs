using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>"Klasik görünümde incele": eski pencere boş değil, seçili klasör ve seçeneklerle açılır.</summary>
public class ClassicStartTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_classic_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public ClassicStartTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Dir(string n) { var p = Path.Combine(_tmp, n); Directory.CreateDirectory(p); return p; }

    [Fact]
    public void OpenClassic_FromFlow_PassesFoldersAndOptions()
    {
        var a = Dir("a"); var b = Dir("b"); var ar = Dir("arsiv");
        _f.Settings.Stored.AddRecent(new[] { a, b }, ar, DateTime.Now);
        _f.Settings.Stored.RenameWithDate = true;
        _f.Settings.Stored.SeparateRawFolder = false;
        _f.Settings.Stored.RecycleSource = false;
        var vm = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)vm.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);

        vm.OpenClassicCommand.Execute(null);

        var start = Assert.Single(_f.Shell.Starts);
        Assert.NotNull(start);
        Assert.Equal(new[] { a, b }, start!.Sources);
        Assert.Equal(ar, start.Archive);
        Assert.True(start.RenameWithDate);
        Assert.False(start.SeparateRawFolder);
        Assert.False(start.RecycleSource);
    }

    [Fact]
    public void OpenClassic_FromHome_OpensEmpty()
    {
        var vm = new ShellViewModel(_f.Services);
        vm.OpenClassicCommand.Execute(null);
        Assert.Null(Assert.Single(_f.Shell.Starts));
    }

    [Fact]
    public void OpenClassic_FlowWithoutArchive_OpensEmpty()
    {
        var vm = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)vm.CurrentPage;
        home.StartOrganizeCommand.Execute(null);
        vm.OpenClassicCommand.Execute(null);
        Assert.Null(Assert.Single(_f.Shell.Starts));
    }
}
