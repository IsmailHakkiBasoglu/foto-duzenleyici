using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class BackupVerifyFlowTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_bver_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly string _arc, _bak;

    public BackupVerifyFlowTests()
    {
        _arc = Path.Combine(_tmp, "arsiv");
        _bak = Path.Combine(_tmp, "yedek");
        Directory.CreateDirectory(_arc);
        File.WriteAllText(Path.Combine(_arc, "a.jpg"), "aaaa");
        File.WriteAllText(Path.Combine(_arc, "b.jpg"), "bbbb");
        new ArchiveBackup().Run(_arc, _bak, new BackupOptions());
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    BackupFlowViewModel Vm() => new(_f.Services, _f.Settings.Stored) { Archive = _arc, Backup = _bak };

    void Corrupt(string name) => File.WriteAllText(Path.Combine(_bak, name), "bozuldu");

    [Fact]
    public async Task Verify_Clean()
    {
        var vm = Vm();
        Assert.True(vm.CanVerify);

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Finished, vm.Step);
        Assert.Contains("0 eksik · 0 bozuk", vm.VerifySummary);
        Assert.False(vm.CanRepair);
    }

    [Fact]
    public async Task Verify_Corrupt_Repair_ThenClean()
    {
        Corrupt("a.jpg");
        var vm = Vm();
        await vm.VerifyCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.ProblemRows);
        Assert.Equal("a.jpg", row.Path);
        Assert.Equal("bozuk", row.Reason);
        Assert.True(vm.CanRepair);

        await vm.RepairCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Repairs!.Repaired);
        Assert.Null(vm.Verification);
        Assert.Empty(vm.ProblemRows);
        await vm.VerifyCommand.ExecuteAsync(null);
        Assert.Contains("0 eksik · 0 bozuk", vm.VerifySummary);
    }

    [Fact]
    public async Task Repair_NotRepairable_ShowsReason()
    {
        Corrupt("a.jpg");
        File.Delete(Path.Combine(_arc, "a.jpg"));
        var vm = Vm();
        await vm.VerifyCommand.ExecuteAsync(null);

        await vm.RepairCommand.ExecuteAsync(null);

        Assert.Contains(vm.RepairIssues, e => e.Contains("arşivde artık yok"));
        Assert.Contains("1 onarılamadı", vm.RepairSummary);
    }

    [Fact]
    public async Task FolderChange_DropsVerification()
    {
        Corrupt("a.jpg");
        var vm = Vm();
        await vm.VerifyCommand.ExecuteAsync(null);

        vm.Archive = Path.Combine(_tmp, "baska");

        Assert.Null(vm.Verification);
        Assert.False(vm.CanRepair);
        Assert.Empty(vm.ProblemRows);
    }

    [Fact]
    public async Task Verify_NoManifest_Explains()
    {
        var empty = Path.Combine(_tmp, "bos");
        Directory.CreateDirectory(empty);
        var vm = Vm();
        vm.Backup = empty;

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.Equal("Bu klasörde bu uygulamayla alınmış bir yedek bulunamadı.", vm.VerifySummary);
    }

    [Fact]
    public async Task Verify_Stopped_KeepsFound_Partial()
    {
        Corrupt("a.jpg");
        var vm = Vm();
        vm.OnProgressForTest = d => { if (d >= 1) vm.StopCommand.Execute(null); };

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.True(vm.VerifyPartial);
        Assert.Contains("yarım", vm.VerifySummary);
        Assert.True(vm.CanRepair);
        Assert.False(vm.CanResume);
    }

    [Fact]
    public async Task Verify_NotInManifest_NotAProblem()
    {
        File.WriteAllText(Path.Combine(_bak, "fazla.jpg"), "x");
        var vm = Vm();

        await vm.VerifyCommand.ExecuteAsync(null);

        Assert.False(vm.CanRepair);
        Assert.Contains("1 listede yok", vm.VerifySummary);
    }

    [Fact]
    public void CanVerify_FalseWhenBackupMissing()
    {
        var vm = Vm();
        vm.Backup = Path.Combine(_tmp, "yok");
        Assert.False(vm.CanVerify);
    }
}
