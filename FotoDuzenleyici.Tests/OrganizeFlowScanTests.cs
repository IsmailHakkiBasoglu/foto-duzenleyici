using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class OrganizeFlowScanTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_flowscan_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public OrganizeFlowScanTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Make(string rel, string content)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        var t = new DateTime(2024, 3, 12, 10, 0, 0);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
        return p;
    }

    OrganizeFlowViewModel Flow(string[] sources, string archive)
    {
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        Directory.CreateDirectory(Path.Combine(_tmp, archive));
        vm.Folders.Load(new RecentPair(sources.Select(s => Path.Combine(_tmp, s)).ToList(), Path.Combine(_tmp, archive), DateTime.Now));
        return vm;
    }

    [Fact]
    public async Task Scan_BuildsPlan_GoesToReview()
    {
        Make(@"kaynak\a.pdf", "AYNI");
        Make(@"kaynak\alt\a.pdf", "AYNI");
        Make(@"kaynak\b.pdf", "FARKLI");
        var vm = Flow(new[] { "kaynak" }, "arsiv");

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(FlowStep.Review, vm.Step);
        Assert.NotNull(vm.Review);
        Assert.Equal(1, vm.Review!.Summary.DupCount);
        Assert.Equal(1, vm.Review.DuplicateGroups);
        Assert.Equal(2, vm.Review.Summary.MoveCount);
        Assert.Single(_f.Settings.Stored.Recent);
    }

    [Fact]
    public async Task Scan_ArchiveAlsoSource_NoDoubleCount()
    {
        Make(@"arsiv\gelen\a.pdf", "A");
        Make(@"arsiv\gelen\b.pdf", "B");
        var vm = Flow(new[] { @"arsiv\gelen", "arsiv" }, "arsiv");

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Review!.Rows.Count);
        Assert.Equal(0, vm.Review.Summary.DupCount);
    }

    [Fact]
    public async Task Scan_Cancelled_StaysOnFolders()
    {
        Make(@"kaynak\a.pdf", "A");
        var vm = Flow(new[] { "kaynak" }, "arsiv");
        vm.CancelBeforeStartForTest = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(FlowStep.Folders, vm.Step);
        Assert.Null(vm.Review);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Review_FilterAndSearch()
    {
        Make(@"kaynak\a.pdf", "AYNI");
        Make(@"kaynak\alt\a.pdf", "AYNI");
        Make(@"kaynak\bbb.pdf", "FARKLI");
        var vm = Flow(new[] { "kaynak" }, "arsiv");
        await vm.ScanCommand.ExecuteAsync(null);
        var r = vm.Review!;

        r.Filter = PlanFilter.Duplicate;
        Assert.Single(r.Rows);
        Assert.Equal(PlanAction.Duplicate, r.Rows[0].Action);

        r.Filter = PlanFilter.All;
        r.Search = "BBB.PDF"; // klasör adındaki GUID "bbb" içerebilir: dosya adıyla ara
        Assert.Single(r.Rows);
        Assert.EndsWith("bbb.pdf", r.Rows[0].File.Path);
    }

    [Fact]
    public async Task Back_FromReview_ToFolders()
    {
        Make(@"kaynak\a.pdf", "A");
        var vm = Flow(new[] { "kaynak" }, "arsiv");
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(FlowStep.Review, vm.Step);

        vm.BackCommand.Execute(null);

        Assert.Equal(FlowStep.Folders, vm.Step);
    }

    [Fact]
    public async Task GoToConfirm_ThenBack_ToReview()
    {
        Make(@"kaynak\a.pdf", "A");
        var vm = Flow(new[] { "kaynak" }, "arsiv");
        await vm.ScanCommand.ExecuteAsync(null);

        vm.GoToConfirmCommand.Execute(null);
        Assert.Equal(FlowStep.Confirm, vm.Step);
        Assert.NotNull(vm.Confirm);
        Assert.Equal(1, vm.Confirm!.Summary.MoveCount);

        vm.BackCommand.Execute(null);
        Assert.Equal(FlowStep.Review, vm.Step);
    }

    [Fact]
    public async Task Review_SaveCsv_WritesToPickedPath()
    {
        Make(@"kaynak\a.pdf", "A");
        var vm = Flow(new[] { "kaynak" }, "arsiv");
        await vm.ScanCommand.ExecuteAsync(null);
        _f.Files.Csv = Path.Combine(_tmp, "plan.csv");

        vm.Review!.SaveCsvCommand.Execute(null);

        Assert.True(File.Exists(_f.Files.Csv));
    }
}
