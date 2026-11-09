using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class BackupShellTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_bshell_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly ManualResetEventSlim _entered = new(), _gate = new();
    readonly string _arc, _bak;

    public BackupShellTests()
    {
        _arc = Path.Combine(_tmp, "arsiv");
        _bak = Path.Combine(_tmp, "yedek");
        Directory.CreateDirectory(_arc);
        File.WriteAllText(Path.Combine(_arc, "a.jpg"), "aaaa");
        _f.Recycle = _ => { };
    }

    public void Dispose()
    {
        _gate.Set();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    BackupFlowViewModel Open(ShellViewModel shell)
    {
        ((HomeViewModel)shell.CurrentPage).StartBackupCommand.Execute(null);
        var vm = (BackupFlowViewModel)shell.CurrentPage;
        vm.Archive = _arc; vm.Backup = _bak;
        return vm;
    }

    async Task<(ShellViewModel Shell, BackupFlowViewModel Vm)> Running()
    {
        var shell = new ShellViewModel(_f.Services);
        var vm = Open(shell);
        vm.OnProgressForTest = _ => { _entered.Set(); _gate.Wait(TimeSpan.FromSeconds(20)); };
        await vm.ScanCommand.ExecuteAsync(null);
        vm.StartCommand.Execute(null);
        Assert.True(_entered.Wait(TimeSpan.FromSeconds(20)));
        return (shell, vm);
    }

    [Fact]
    public void Home_StartBackup_OpensPage() => Assert.IsType<BackupFlowViewModel>(OpenPage());

    object OpenPage()
    {
        var shell = new ShellViewModel(_f.Services);
        ((HomeViewModel)shell.CurrentPage).StartBackupCommand.Execute(null);
        return shell.CurrentPage;
    }

    [Fact]
    public void Home_LastBackup_Exposed()
    {
        _f.Settings.Stored.LastBackup = new BackupPair(@"C:\a", @"F:\y", false, DateTime.Now);
        Assert.Equal(@"F:\y", new HomeViewModel(_f.Settings.Stored).LastBackup!.Backup);
    }

    [Fact]
    public async Task Shell_BackupRunning_Locks()
    {
        var (shell, vm) = await Running();
        _f.Files.Journal = Path.Combine(_tmp, "islem.jsonl");
        _f.Dialogs.Asked.Clear();

        shell.GoHomeCommand.Execute(null);
        await shell.UndoCommand.ExecuteAsync(null);
        shell.OpenClassicCommand.Execute(null);

        Assert.Same(vm, shell.CurrentPage);
        Assert.Empty(_f.Dialogs.Asked);
        Assert.Equal(0, _f.Shell.Classic);
        Assert.False(string.IsNullOrEmpty(shell.ErrorMessage));
        _gate.Set();
        await vm.RunningTask!;
    }

    [Fact]
    public async Task Shell_BackupRunning_Close_AsksAndWaits()
    {
        var (shell, vm) = await Running();
        _f.Dialogs.Answer = false;
        Assert.Equal(CloseDecision.Stay, shell.RequestClose());
        Assert.Contains("Yedekleme sürüyor. Durdurulsun mu? O anki dosya yarıda kesilir, yedek tutarlı kalır; kalanlar sonra devam ettirilebilir.",
            _f.Dialogs.Asked.Single());

        _f.Dialogs.Answer = true;
        Assert.Equal(CloseDecision.WaitThenClose, shell.RequestClose());
        Assert.Same(vm.RunningTask, shell.RunningTask);
        _gate.Set();
        await shell.RunningTask!;
    }

    [Fact]
    public async Task Shell_ScanRunning_LocksButClosesWithoutAsking()
    {
        var shell = new ShellViewModel(_f.Services);
        var vm = Open(shell);
        vm.BeforeScanForTest = () => { _entered.Set(); _gate.Wait(TimeSpan.FromSeconds(20)); };
        var scan = vm.ScanCommand.ExecuteAsync(null);
        Assert.True(_entered.Wait(TimeSpan.FromSeconds(20)));

        shell.GoHomeCommand.Execute(null);
        Assert.Same(vm, shell.CurrentPage);

        _f.Dialogs.Asked.Clear();
        Assert.Equal(CloseDecision.Close, shell.RequestClose());
        Assert.Empty(_f.Dialogs.Asked);
        _gate.Set();
        await scan;
        Assert.Equal(BackupStep.Select, vm.Step);
    }

    [Fact]
    public async Task Flow_Done_BackupButton_PassesArchive()
    {
        var src = Path.Combine(_tmp, "kaynak");
        Directory.CreateDirectory(src);
        var file = Path.Combine(src, "x.pdf");
        File.WriteAllText(file, "%PDF-1.4 x");
        File.SetLastWriteTime(file, new DateTime(2024, 3, 1));
        var target = Path.Combine(_tmp, "hedef");
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        _f.Settings.Stored.AddRecent(new[] { src }, target, DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        flow.Confirm!.ApplyCommand.Execute(null);
        await flow.ApplyTask!;

        flow.BackupFromDoneCommand.Execute(null);

        var vm = Assert.IsType<BackupFlowViewModel>(shell.CurrentPage);
        Assert.Equal(target, vm.Archive);
    }

    [Fact]
    public async Task ShellUndo_Done_BackupButton_OpensBackup()
    {
        var src = Path.Combine(_tmp, "kaynak");
        Directory.CreateDirectory(src);
        var file = Path.Combine(src, "x.pdf");
        File.WriteAllText(file, "%PDF-1.4 x");
        File.SetLastWriteTime(file, new DateTime(2024, 3, 1));
        var target = Path.Combine(_tmp, "hedef");
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        _f.Settings.Stored.AddRecent(new[] { src }, target, DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        var journal = flow.Confirm!.JournalPath;
        flow.Confirm.ApplyCommand.Execute(null);
        await flow.ApplyTask!;
        shell.GoHomeCommand.Execute(null);

        _f.Files.Journal = journal;
        await shell.UndoCommand.ExecuteAsync(null);
        var undo = (OrganizeFlowViewModel)shell.CurrentPage;
        undo.BackupFromDoneCommand.Execute(null);

        var vm = Assert.IsType<BackupFlowViewModel>(shell.CurrentPage);
        Assert.Equal(target, vm.Archive);
    }
}
