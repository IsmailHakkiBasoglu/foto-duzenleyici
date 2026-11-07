using System.Text.Json;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class ConfirmStepTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_confirm_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public ConfirmStepTests() => Directory.CreateDirectory(Archive);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }
    string Archive => Path.Combine(_tmp, "arsiv");

    static PlanItem Item(PlanAction a, long size, string src = @"C:\k\x.jpg") => new()
    {
        File = new MediaFile { Path = src, Size = size, Kind = FileKind.Photo },
        Action = a, TargetDir = @"C:\hedef", TargetName = "x.jpg",
    };

    Plan Plan(params PlanItem[] items) { var p = new Plan(); p.Items.AddRange(items); return p; }

    ConfirmStepViewModel Vm(Plan? plan = null, IEnumerable<string>? sources = null, string? archive = null) =>
        new(plan ?? Plan(Item(PlanAction.Move, 100), Item(PlanAction.Duplicate, 20), Item(PlanAction.AlreadyInPlace, 5)),
            new PlanOptions { DestinationRoot = archive ?? Archive }, sources ?? new[] { _tmp }, _f.Services, _f.Settings.Stored,
            new DateTime(2026, 11, 7, 14, 32, 5));

    void WriteUnfinishedJournal()
    {
        var j = Path.Combine(Archive, "_Gunluk", "islem-1.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(j)!);
        File.WriteAllText(j, JsonSerializer.Serialize(new JournalEntry { Source = @"C:\k\a.jpg", Target = Path.Combine(Archive, "a.jpg"), State = "Planned" }) + "\n");
    }

    [Fact]
    public void Space_Insufficient_BlocksApply()
    {
        _f.Free = 1;
        var vm = Vm();
        Assert.Equal(120, vm.NeedBytes);
        Assert.False(vm.SpaceOk);
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void Space_Enough_AllowsApply()
    {
        _f.Free = 120;
        var vm = Vm();
        Assert.True(vm.SpaceOk);
        Assert.True(vm.CanApply);
        Assert.Equal(2, vm.Summary.ApplyCount);
    }

    [Fact]
    public void Unfinished_Shown_BlocksApply()
    {
        WriteUnfinishedJournal();
        var vm = Vm();
        Assert.NotNull(vm.Unfinished);
        Assert.Equal(1, vm.Unfinished!.Value.Remaining);
        Assert.Equal(1, vm.UnfinishedRemaining);   // ekran için özellik (tuple alanına bağlanılamaz)
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void Abandon_AfterConfirm_ClearsAndAllowsApply()
    {
        WriteUnfinishedJournal();
        var vm = Vm();
        _f.Dialogs.Answer = true;
        vm.AbandonCommand.Execute(null);
        Assert.Null(vm.Unfinished);
        Assert.True(vm.CanApply);
        Assert.Empty(Executor.FindUnfinished(Archive));
    }

    [Fact]
    public void Abandon_Declined_KeepsBlocked()
    {
        WriteUnfinishedJournal();
        var vm = Vm();
        _f.Dialogs.Answer = false;
        vm.AbandonCommand.Execute(null);
        Assert.NotNull(vm.Unfinished);
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void Resume_RaisesEventWithJournal()
    {
        WriteUnfinishedJournal();
        var vm = Vm();
        string? got = null;
        vm.ResumeRequested += j => got = j;
        vm.ResumeCommand.Execute(null);
        Assert.Equal(vm.Unfinished!.Value.Journal, got);
    }

    [Fact]
    public void OtherDrive_ListsSourcesOnDifferentRoot()
    {
        var vm = Vm(sources: new[] { @"E:\DCIM", @"D:\a", @"e:\Baska" }, archive: @"D:\Arsiv");
        Assert.Equal(new[] { @"E:\DCIM", @"e:\Baska" }, vm.OtherDriveSources);
    }

    [Fact]
    public void JournalPath_UsesArchiveAndTime()
    {
        var vm = Vm();
        Assert.Equal(Path.Combine(Archive, "_Gunluk", "islem-20261107-143205.jsonl"), vm.JournalPath);
    }

    [Fact]
    public void RecycleSource_PersistsToSettings()
    {
        var vm = Vm();
        Assert.True(vm.RecycleSource);
        vm.RecycleSource = false;
        Assert.False(_f.Settings.Stored.RecycleSource);
        Assert.True(_f.Settings.Saves > 0);
    }

    [Fact]
    public void Apply_RaisesOnlyWhenAllowed()
    {
        _f.Free = 1;
        var vm = Vm();
        int n = 0;
        vm.ApplyRequested += () => n++;
        vm.ApplyCommand.Execute(null);
        Assert.Equal(0, n);
    }
}
