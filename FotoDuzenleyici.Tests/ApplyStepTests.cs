using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class ApplyStepTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_apply_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly string _bin;

    public ApplyStepTests()
    {
        _bin = Path.Combine(_tmp, "_cop");
        Directory.CreateDirectory(_bin);
        _f.Recycle = p => File.Move(p, Path.Combine(_bin, Guid.NewGuid().ToString("N") + Path.GetExtension(p)));
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Make(string rel, string content)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    PlanItem Item(string src, string targetDir) => new()
    {
        File = new MediaFile { Path = src, Size = new FileInfo(src).Length, Kind = FileKind.Pdf },
        Action = PlanAction.Move, TargetDir = targetDir, TargetName = Path.GetFileName(src),
    };

    string Journal => Path.Combine(_tmp, "arsiv", "_Gunluk", "islem-1.jsonl");

    [Fact]
    public void Eta_NullUntil20_ThenLinear()
    {
        Assert.Null(ApplyStepViewModel.Eta(19, 100, TimeSpan.FromSeconds(19)));
        Assert.Equal(TimeSpan.FromSeconds(80), ApplyStepViewModel.Eta(20, 100, TimeSpan.FromSeconds(20)));
        Assert.Null(ApplyStepViewModel.Eta(0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Run_MovesFiles_ReportsResult()
    {
        var a = Make(@"k\a.pdf", "A");
        var b = Make(@"k\b.pdf", "B");
        var dest = Path.Combine(_tmp, "arsiv", "2024-03");
        var vm = new ApplyStepViewModel(_f.Services, Path.Combine(_tmp, "arsiv"));

        await vm.RunAsync(new[] { Item(a, dest), Item(b, dest) }, Journal, recycle: true);

        Assert.False(vm.IsRunning);
        Assert.False(vm.Stopped);
        Assert.Equal(2, vm.Result!.Done);
        Assert.Equal(0, vm.Result.Failed);
        Assert.True(File.Exists(Path.Combine(dest, "a.pdf")));
        Assert.False(File.Exists(a));
        Assert.Equal(Journal, vm.Journal);
    }

    [Fact]
    public async Task Cancel_BeforeStart_Stopped_JournalHasPending()
    {
        var a = Make(@"k\a.pdf", "A");
        var vm = new ApplyStepViewModel(_f.Services, Path.Combine(_tmp, "arsiv"));
        vm.CancelBeforeStartForTest = true;

        await vm.RunAsync(new[] { Item(a, Path.Combine(_tmp, "arsiv", "x")) }, Journal, recycle: true);

        Assert.True(vm.Stopped);
        Assert.Equal(1, vm.PendingAfterStop);
        Assert.Single(Executor.Pending(Journal));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public async Task Undo_Confirmed_RestoresFiles()
    {
        var a = Make(@"k\a.pdf", "A");
        var dest = Path.Combine(_tmp, "arsiv", "2024-03");
        var vm = new ApplyStepViewModel(_f.Services, Path.Combine(_tmp, "arsiv"));
        await vm.RunAsync(new[] { Item(a, dest) }, Journal, recycle: true);

        _f.Dialogs.Answer = true;
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(ApplyMode.Undo, vm.Mode);
        Assert.Equal("A", File.ReadAllText(a));
        Assert.False(File.Exists(Path.Combine(dest, "a.pdf")));
    }

    [Fact]
    public async Task Undo_Declined_DoesNothing()
    {
        var a = Make(@"k\a.pdf", "A");
        var dest = Path.Combine(_tmp, "arsiv", "2024-03");
        var vm = new ApplyStepViewModel(_f.Services, Path.Combine(_tmp, "arsiv"));
        await vm.RunAsync(new[] { Item(a, dest) }, Journal, recycle: true);

        _f.Dialogs.Answer = false;
        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(ApplyMode.Run, vm.Mode);
        Assert.True(File.Exists(Path.Combine(dest, "a.pdf")));
    }

    [Fact]
    public void OpenArchive_UsesShell()
    {
        var vm = new ApplyStepViewModel(_f.Services, @"D:\Arsiv");
        vm.OpenArchiveCommand.Execute(null);
        Assert.Equal(new[] { @"D:\Arsiv" }, _f.Shell.Opened);
    }
}
