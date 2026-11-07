using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class ShellTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_shell_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public ShellTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Dir(string n) { var p = Path.Combine(_tmp, n); Directory.CreateDirectory(p); return p; }

    [Fact]
    public void Start_ShowsHome()
    {
        var vm = new ShellViewModel(_f.Services);
        var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
        Assert.Null(home.LastOrganize);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void StartOrganize_WithRecent_PrefillsFolders()
    {
        var a = Dir("a"); var ar = Dir("arsiv");
        _f.Settings.Stored.AddRecent(new[] { a }, ar, DateTime.Now);
        var vm = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)vm.CurrentPage;
        Assert.NotNull(home.LastOrganize);

        home.StartOrganizeCommand.Execute(home.LastOrganize);

        var flow = Assert.IsType<OrganizeFlowViewModel>(vm.CurrentPage);
        Assert.Equal(ar, flow.Folders.Archive);
        Assert.Equal(new[] { a }, flow.Folders.ExistingSources);
    }

    [Fact]
    public void StartOrganize_Empty_OpensBlankFlow()
    {
        var vm = new ShellViewModel(_f.Services);
        ((HomeViewModel)vm.CurrentPage).StartOrganizeCommand.Execute(null);
        var flow = Assert.IsType<OrganizeFlowViewModel>(vm.CurrentPage);
        Assert.Empty(flow.Folders.Sources);
        vm.GoHomeCommand.Execute(null);
        Assert.IsType<HomeViewModel>(vm.CurrentPage);
    }

    [Fact]
    public async Task GoHome_BlockedWhileApplying_AndCanCloseAsksAndCancels()
    {
        var src = Dir("kaynak"); var ar = Dir("arsiv");
        File.WriteAllText(Path.Combine(src, "a.pdf"), "A");
        File.WriteAllText(Path.Combine(src, "b.pdf"), "B");
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var bin = Dir("_cop");
        _f.Recycle = p => { File.Move(p, Path.Combine(bin, Guid.NewGuid().ToString("N"))); entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };
        _f.Settings.Stored.AddRecent(new[] { src }, ar, DateTime.Now);
        var vm = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)vm.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        var flow = (OrganizeFlowViewModel)vm.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.GoToConfirmCommand.Execute(null);
        flow.Confirm!.ApplyCommand.Execute(null);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        vm.GoHomeCommand.Execute(null);
        Assert.Same(flow, vm.CurrentPage);

        _f.Dialogs.Answer = true;
        Assert.False(vm.CanClose());
        Assert.Single(_f.Dialogs.Asked);
        gate.Set();
        await flow.ApplyTask!;
        Assert.True(flow.Apply!.Stopped);
        Assert.True(vm.CanClose());
    }

    [Fact]
    public void CanClose_Idle_DoesNotAsk()
    {
        var vm = new ShellViewModel(_f.Services);
        Assert.True(vm.CanClose());
        Assert.Empty(_f.Dialogs.Asked);
    }

    [Fact]
    public void CorruptSettings_ShowsWarningOnce()
    {
        _f.Settings.LoadFailed = true;
        var vm = new ShellViewModel(_f.Services);
        Assert.NotNull(vm.ErrorMessage);
        vm.DismissErrorCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Undo_PickerCancelled_NoChange()
    {
        var vm = new ShellViewModel(_f.Services);
        _f.Files.Journal = null;
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.IsType<HomeViewModel>(vm.CurrentPage);
        Assert.Empty(_f.Dialogs.Asked);
    }

    [Fact]
    public async Task Undo_Picked_RunsInApplyStep()
    {
        var ar = Dir("arsiv");
        var src = Path.Combine(_tmp, "k", "a.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(src)!);
        File.WriteAllText(src, "A");
        var bin = Dir("_cop");
        _f.Recycle = p => File.Move(p, Path.Combine(bin, Guid.NewGuid().ToString("N")));
        var journal = Path.Combine(ar, "_Gunluk", "islem-1.jsonl");
        var item = new PlanItem
        {
            File = new MediaFile { Path = src, Size = 1, Kind = FileKind.Pdf },
            Action = PlanAction.Move, TargetDir = Path.Combine(ar, "2024-03"), TargetName = "a.pdf",
        };
        new Executor(journal, _f.Recycle).Run(new[] { item });
        Assert.False(File.Exists(src));

        var vm = new ShellViewModel(_f.Services);
        _f.Files.Journal = journal;
        _f.Dialogs.Answer = true;
        await vm.UndoCommand.ExecuteAsync(null);

        var flow = Assert.IsType<OrganizeFlowViewModel>(vm.CurrentPage);
        Assert.Equal(FlowStep.Apply, flow.Step);
        Assert.Equal(ApplyMode.Undo, flow.Apply!.Mode);
        Assert.Equal(ar, flow.Apply.Archive);
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void OpenClassic_UsesShell()
    {
        var vm = new ShellViewModel(_f.Services);
        vm.OpenClassicCommand.Execute(null);
        Assert.Equal(1, _f.Shell.Classic);
    }

    [Fact]
    public void Theme_Persists()
    {
        var vm = new ShellViewModel(_f.Services);
        Assert.Equal("Sistem", vm.Theme);
        vm.Theme = "Koyu";
        Assert.Equal("Koyu", _f.Settings.Stored.Theme);
        Assert.True(_f.Settings.Saves > 0);
    }
}
