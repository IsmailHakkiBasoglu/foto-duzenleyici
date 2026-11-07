using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Düzenle akışı yalnız ViewModel komutlarıyla, gerçek Core ve geçici klasörlerle.</summary>
public class OrganizeFlowEndToEndTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_flowe2e_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly string _bin;
    Action? _onRecycle;

    public OrganizeFlowEndToEndTests()
    {
        _bin = Path.Combine(_tmp, "_cop");
        Directory.CreateDirectory(_bin);
        _f.Recycle = p =>
        {
            File.Move(p, Path.Combine(_bin, Guid.NewGuid().ToString("N") + Path.GetExtension(p)));
            _onRecycle?.Invoke();
        };
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Src => Path.Combine(_tmp, "kaynak");
    string Arc => Path.Combine(_tmp, "arsiv");

    string Make(string name, string content, DateTime t)
    {
        var p = Path.Combine(Src, name);
        Directory.CreateDirectory(Src);
        File.WriteAllText(p, content);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
        return p;
    }

    OrganizeFlowViewModel NewFlow()
    {
        Directory.CreateDirectory(Arc);
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        _f.Folders.Next.Enqueue(Src);
        vm.Folders.AddSourceCommand.Execute(null);
        _f.Folders.Next.Enqueue(Arc);
        vm.Folders.PickArchiveCommand.Execute(null);
        return vm;
    }

    [Fact]
    public async Task EndToEnd_PickScanConfirmApplyUndo()
    {
        var a = Make("a.pdf", "A", new DateTime(2024, 3, 12));
        var b = Make("b.pdf", "B", new DateTime(2023, 12, 25));
        var vm = NewFlow();

        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        Assert.True(vm.Confirm!.CanApply);
        vm.Confirm.ApplyCommand.Execute(null);
        await vm.ApplyTask!;

        Assert.Equal(FlowStep.Apply, vm.Step);
        Assert.Equal(2, vm.Apply!.Result!.Done);
        Assert.False(File.Exists(a));
        Assert.Single(Directory.GetFiles(Path.Combine(Arc, "2024-03"), "a.pdf", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(Path.Combine(Arc, "2023-12"), "b.pdf", SearchOption.AllDirectories));

        _f.Dialogs.Answer = true;
        await vm.Apply.UndoCommand.ExecuteAsync(null);

        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public async Task Apply_NavigationLockedWhileRunning()
    {
        Make("a.pdf", "A", new DateTime(2024, 3, 12));
        var vm = NewFlow();
        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        using var gate = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        _onRecycle = () => { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); };

        vm.Confirm!.ApplyCommand.Execute(null);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(vm.NavigationLocked);
        vm.BackCommand.Execute(null);
        Assert.Equal(FlowStep.Apply, vm.Step);

        gate.Set();
        await vm.ApplyTask!;
        Assert.False(vm.NavigationLocked);
    }

    [Fact]
    public async Task AfterFinish_ConfirmUnavailable()
    {
        Make("a.pdf", "A", new DateTime(2024, 3, 12));
        var vm = NewFlow();
        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        vm.Confirm!.ApplyCommand.Execute(null);
        await vm.ApplyTask!;

        Assert.Null(vm.Confirm);
        Assert.Null(vm.Review);
        Assert.Equal(FlowStep.Apply, vm.Step);
        vm.GoToConfirmCommand.Execute(null);
        Assert.Equal(FlowStep.Apply, vm.Step);

        vm.BackCommand.Execute(null);
        Assert.Equal(FlowStep.Folders, vm.Step);
        Assert.Null(vm.Apply);
    }

    [Fact]
    public async Task EndToEnd_StopThenResume()
    {
        Make("a.pdf", "A", new DateTime(2024, 3, 12));
        Make("b.pdf", "B", new DateTime(2024, 3, 13));
        Make("c.pdf", "C", new DateTime(2024, 3, 14));
        var vm = NewFlow();
        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        _onRecycle = () => { vm.Apply?.CancelCommand.Execute(null); _onRecycle = null; };
        vm.Confirm!.ApplyCommand.Execute(null);
        await vm.ApplyTask!;
        Assert.True(vm.Apply!.Stopped);
        Assert.Equal(2, vm.Apply.PendingAfterStop);

        // Yeni tarama → Onayla'da yarım işlem → Devam et
        vm.BackCommand.Execute(null);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        Assert.NotNull(vm.Confirm!.Unfinished);
        Assert.False(vm.Confirm.CanApply);
        vm.Confirm.ResumeCommand.Execute(null);
        await vm.ApplyTask!;

        Assert.Equal(ApplyMode.Resume, vm.Apply!.Mode);
        Assert.Equal(2, vm.Apply.Result!.Done);
        Assert.Empty(Directory.GetFiles(Src));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(Arc, "2024-03"), "*.pdf", SearchOption.AllDirectories).Length);
        Assert.Empty(Executor.FindUnfinished(Arc));
    }
}
