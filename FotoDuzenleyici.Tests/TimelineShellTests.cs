using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class TimelineShellTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_tlsh_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly ManualResetEventSlim _gate = new();

    public TimelineShellTests()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "2022-11"));
        File.WriteAllText(Path.Combine(_tmp, "2022-11", "a.jpg"), "x");
    }

    public void Dispose()
    {
        _gate.Set();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    static TimelineViewModel Open(ShellViewModel shell)
    {
        ((HomeViewModel)shell.CurrentPage).StartTimelineCommand.Execute(null);
        return Assert.IsType<TimelineViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task Home_StartTimeline_OpensTimelinePage_WithStartFolder_AndCounts()
    {
        var vm = Open(new ShellViewModel(_f.Services, _tmp));
        Assert.Equal(_tmp, vm.Folder);
        await vm.RunningTask!;
        Assert.Equal(1, vm.Table!.Total);
    }

    [Fact]
    public void Home_StartTimeline_KeepsStartFolderForLaterHome()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        Open(shell);
        shell.GoHomeCommand.Execute(null);
        Assert.Equal(_tmp, ((HomeViewModel)shell.CurrentPage).SelectedFolder);
    }

    [Fact]
    public async Task GoHome_DuringCount_StopsIt_AndNavigates()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        ((HomeViewModel)shell.CurrentPage).StartTimelineCommand.Execute(null);
        var vm = (TimelineViewModel)shell.CurrentPage;
        var entered = new ManualResetEventSlim();
        vm.BeforeCountForTest = _ => { entered.Set(); _gate.Wait(TimeSpan.FromSeconds(20)); };
        var t = vm.CountAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));

        shell.GoHomeCommand.Execute(null);
        Assert.IsType<HomeViewModel>(shell.CurrentPage);
        _gate.Set();
        await t;
        Assert.True(vm.Stopped);
        Assert.Null(vm.Table);
    }

    [Fact]
    public void RequestClose_DuringCount_ClosesWithoutAsking()
    {
        var shell = new ShellViewModel(_f.Services, _tmp);
        var vm = Open(shell);
        vm.BeforeCountForTest = _ => _gate.Wait(TimeSpan.FromSeconds(20));
        _ = vm.CountAsync();
        Assert.Equal(CloseDecision.Close, shell.RequestClose());
        Assert.Empty(_f.Dialogs.Asked);
        Assert.Null(shell.RunningTask);
    }

    [Fact]
    public void SettingsJson_WithoutLastTimelineFolder_LoadsNull()
    {
        var path = Path.Combine(_tmp, "ayarlar.json");
        File.WriteAllText(path, "{\"Theme\":\"Koyu\"}");
        var s = new JsonSettingsStore(path).Load();
        Assert.Null(s.LastTimelineFolder);
        Assert.Equal("Koyu", s.Theme);
    }
}
