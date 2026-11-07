using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class FoldersStepTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_folders_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public FoldersStepTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Dir(string name) { var p = Path.Combine(_tmp, name); Directory.CreateDirectory(p); return p; }
    FoldersStepViewModel Vm(AppSettings? s = null) => new(_f.Services, s ?? _f.Settings.Stored);

    [Fact]
    public void AddSource_FromPicker_AddsOnce()
    {
        var a = Dir("a");
        var vm = Vm();
        _f.Folders.Next.Enqueue(a);
        _f.Folders.Next.Enqueue(a + "\\");
        vm.AddSourceCommand.Execute(null);
        vm.AddSourceCommand.Execute(null);
        var e = Assert.Single(vm.Sources);
        Assert.Equal(a, e.Path);
        Assert.True(e.Exists);
    }

    [Fact]
    public void MissingSource_NotExisting_ExcludedFromScan()
    {
        var vm = Vm();
        var gone = Path.Combine(_tmp, "yok");
        var a = Dir("a");
        vm.Load(new RecentPair(new() { gone, a }, Dir("arsiv"), DateTime.Now));
        Assert.Equal(2, vm.Sources.Count);
        Assert.False(vm.Sources[0].Exists);
        Assert.Equal(new[] { a }, vm.ExistingSources);
        Assert.True(vm.CanScan);
    }

    [Fact]
    public void CanScan_RequiresExistingSourceAndArchive()
    {
        var vm = Vm();
        Assert.False(vm.CanScan);
        _f.Folders.Next.Enqueue(Dir("a"));
        vm.AddSourceCommand.Execute(null);
        Assert.False(vm.CanScan);              // arşiv yok
        vm.Archive = Dir("arsiv");
        Assert.True(vm.CanScan);
        vm.RemoveSourceCommand.Execute(vm.Sources[0]);
        Assert.False(vm.CanScan);              // kaynak yok
        vm.Load(new RecentPair(new() { Path.Combine(_tmp, "yok") }, vm.Archive!, DateTime.Now));
        Assert.False(vm.CanScan);              // tek kaynak var olmayan
    }

    [Fact]
    public void CanScan_PickerCancelled_NoChange()
    {
        var vm = Vm();
        vm.AddSourceCommand.Execute(null);     // kuyruk boş → null
        vm.PickArchiveCommand.Execute(null);
        Assert.Empty(vm.Sources);
        Assert.Null(vm.Archive);
    }

    [Fact]
    public void BuildPlanOptions_UsesSettingsAndArchive()
    {
        var s = new AppSettings { RenameWithDate = true, SeparateRawFolder = false, SourceFolder = SourceFolderMode.SubFolder, AppendSourceToName = true };
        var vm = Vm(s);
        vm.Archive = Dir("arsiv") + "\\";
        var o = vm.BuildPlanOptions();
        Assert.Equal(Path.Combine(_tmp, "arsiv"), o.DestinationRoot);
        Assert.True(o.RenameWithDate);
        Assert.False(o.SeparateRawFolder);
        Assert.True(o.AppendSourceToName);
        Assert.Equal(SourceFolderMode.SubFolder, o.SourceFolder);
        Assert.Contains("tarih", vm.OptionsSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RecentPair_FillsBoth()
    {
        var vm = Vm();
        var a = Dir("a"); var b = Dir("b"); var ar = Dir("arsiv");
        vm.Load(new RecentPair(new() { a, b }, ar, DateTime.Now));
        Assert.Equal(new[] { a, b }, vm.Sources.Select(x => x.Path));
        Assert.Equal(ar, vm.Archive);
    }

    [Fact]
    public void RememberCurrent_SavesRecent()
    {
        var vm = Vm();
        var a = Dir("a"); var ar = Dir("arsiv");
        vm.Load(new RecentPair(new() { a }, ar, DateTime.Now));
        vm.RememberCurrent(new DateTime(2026, 11, 7));
        Assert.Equal(1, _f.Settings.Saves);
        var r = Assert.Single(_f.Settings.Stored.Recent);
        Assert.Equal(ar, r.Archive);
        Assert.Equal(new[] { a }, r.Sources);
    }
}
