using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class BackupFlowTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_bflow_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly string _arc, _bak;

    public BackupFlowTests()
    {
        _arc = Path.Combine(_tmp, "arsiv");
        _bak = Path.Combine(_tmp, "yedek");
        Directory.CreateDirectory(_arc);
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string Arc(string rel, string content = "icerik")
    {
        var p = Path.Combine(_arc, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    BackupFlowViewModel Vm(string? archive = null)
    {
        var vm = new BackupFlowViewModel(_f.Services, _f.Settings.Stored, archive);
        vm.Archive = archive ?? _arc;
        vm.Backup = _bak;
        return vm;
    }

    [Fact]
    public void Prefill_Order()
    {
        var s = _f.Settings.Stored;
        s.AddRecent(new[] { @"C:\kaynak" }, @"C:\son-arsiv", DateTime.Now);
        Assert.Equal(@"C:\son-arsiv", new BackupFlowViewModel(_f.Services, s).Archive);
        Assert.Null(new BackupFlowViewModel(_f.Services, s).Backup);

        s.LastBackup = new BackupPair(@"C:\a", @"F:\y", true, DateTime.Now);
        var fromLast = new BackupFlowViewModel(_f.Services, s);
        Assert.Equal(@"C:\a", fromLast.Archive);
        Assert.Equal(@"F:\y", fromLast.Backup);
        Assert.True(fromLast.IncludeDuplicates);

        var explicitArchive = new BackupFlowViewModel(_f.Services, s, @"C:\bu-is");
        Assert.Equal(@"C:\bu-is", explicitArchive.Archive);
        Assert.Equal(@"F:\y", explicitArchive.Backup);
    }

    [Fact]
    public async Task Scan_ShowsPreview_NoCopy()
    {
        Arc("a.jpg"); Arc(@"b\c.jpg");
        var vm = Vm();
        Assert.True(vm.CanScan);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Preview, vm.Step);
        Assert.Equal(2, vm.Preview!.New);
        Assert.True(vm.CanStart);
        Assert.False(Directory.Exists(_bak));
    }

    [Fact]
    public async Task Start_Copies_SavesLastBackup()
    {
        Arc("a.jpg");
        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Finished, vm.Step);
        Assert.Equal(1, vm.Result!.New);
        Assert.True(File.Exists(Path.Combine(_bak, "a.jpg")));
        Assert.Equal(_bak, _f.Settings.Stored.LastBackup!.Backup);
        Assert.Contains("1 yeni", vm.FinishedSummary);
    }

    [Fact]
    public async Task Blocked_DisablesStart()
    {
        Arc("a.jpg");
        _f.Free = 1;
        var vm = Vm();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Preview, vm.Step);
        Assert.False(vm.CanStart);
        Assert.Contains("yer yetmiyor", vm.Preview!.Blocked);
    }

    [Fact]
    public async Task NothingToCopy_DisablesStart()
    {
        Arc("a.jpg");
        var first = Vm();
        await first.ScanCommand.ExecuteAsync(null);
        await first.StartCommand.ExecuteAsync(null);

        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.NothingToCopy);
        Assert.False(vm.CanStart);
    }

    [Fact]
    public async Task Stop_ThenResume_GoesToPreview()
    {
        Arc("a.jpg"); Arc("b.jpg"); Arc("c.jpg");
        var vm = Vm();
        vm.OnProgressForTest = done => { if (done >= 1) vm.StopCommand.Execute(null); };
        await vm.ScanCommand.ExecuteAsync(null);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.True(vm.Stopped);
        Assert.True(vm.CanResume);
        Assert.Equal(1, vm.Result!.New);
        vm.OnProgressForTest = null;

        await vm.ResumeCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Preview, vm.Step);
        Assert.Equal(2, vm.Preview!.New);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.False(vm.Stopped);
        Assert.True(File.Exists(Path.Combine(_bak, "c.jpg")));
    }

    [Fact]
    public async Task ScanStop_ReturnsToSelect()
    {
        Arc("a.jpg");
        var vm = Vm();
        vm.CancelBeforeStartForTest = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Select, vm.Step);
        Assert.Null(vm.Preview);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task FolderChange_DropsPreview()
    {
        Arc("a.jpg");
        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);

        vm.Backup = Path.Combine(_tmp, "baska");

        Assert.Null(vm.Preview);
        Assert.Equal(BackupStep.Select, vm.Step);
    }

    [Fact]
    public async Task Start_Twice_RunsOnce()
    {
        Arc("a.jpg"); Arc("b.jpg");
        var vm = Vm();
        Task? first = null, afterSecond = null;
        vm.OnProgressForTest = _ =>
        {
            if (afterSecond is not null) return;
            first = vm.RunningTask;
            vm.StartCommand.Execute(null);
            afterSecond = vm.RunningTask;
        };
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.StartCommand.ExecuteAsync(null);

        Assert.NotNull(first);
        Assert.Same(first, afterSecond);
        Assert.Equal(2, vm.Result!.New);
    }

    [Fact]
    public async Task Run_DiskGone_ShowsErrors()
    {
        // Motorun dosyaya özgü hata yolu: kaynak kilitliyken kopya hata satırı üretir, akış çökmez.
        var locked = Arc("kilitli.jpg"); Arc("b.jpg");
        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Finished, vm.Step);
        Assert.Contains(vm.FinishedErrors, e => e.Contains("kilitli.jpg"));
        Assert.True(File.Exists(Path.Combine(_bak, "b.jpg")));
    }

    [Fact]
    public async Task FreeText_Unmeasurable()
    {
        Arc("a.jpg");
        _f.Free = long.MaxValue;
        var vm = Vm();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal("ölçülemedi", vm.FreeText);
        Assert.Null(vm.Preview!.Blocked);
    }

    [Fact]
    public async Task ChangeDuringScan_DiscardsResult()
    {
        Arc("a.jpg"); Arc(@"_Tekrarlar\a.jpg");
        var vm = Vm();
        vm.BeforeScanForTest = () => vm.IncludeDuplicates = true;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Null(vm.Preview);
        Assert.Equal(BackupStep.Select, vm.Step);
    }

    [Fact]
    public async Task Pick_WhileScanning_Ignored()
    {
        Arc("a.jpg");
        var vm = Vm();
        _f.Folders.Next.Enqueue(Path.Combine(_tmp, "baska"));
        _f.Folders.Next.Enqueue(Path.Combine(_tmp, "baska2"));
        vm.BeforeScanForTest = () => { vm.PickArchiveCommand.Execute(null); vm.PickBackupCommand.Execute(null); };

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(_arc, vm.Archive);
        Assert.Equal(_bak, vm.Backup);
        Assert.Equal(BackupStep.Preview, vm.Step);
    }

    [Theory]
    [InlineData("arg")]
    [InlineData("io")]
    public async Task FreeSpace_Throws_ShowsUnmeasurable(string kind)
    {
        Arc("a.jpg");
        _f.FreeFn = _ => throw (kind == "arg" ? new ArgumentException("UNC") : new IOException("hazir degil"));
        var vm = Vm();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Preview, vm.Step);
        Assert.Equal("ölçülemedi", vm.FreeText);
        Assert.Null(vm.Preview!.Blocked);
    }

    [Fact]
    public async Task Resume_ScansOnSelectPage()
    {
        Arc("a.jpg"); Arc("b.jpg");
        var vm = Vm();
        vm.OnProgressForTest = d => { if (d >= 1) vm.StopCommand.Execute(null); };
        await vm.ScanCommand.ExecuteAsync(null);
        await vm.StartCommand.ExecuteAsync(null);
        BackupStep? during = null;
        vm.BeforeScanForTest = () => during = vm.Step;

        await vm.ResumeCommand.ExecuteAsync(null);

        Assert.Equal(BackupStep.Select, during);
    }

    [Fact]
    public async Task UnreadableFiles_NotReportedAsUpToDate()
    {
        Arc("a.jpg");
        var first = Vm();
        await first.ScanCommand.ExecuteAsync(null);
        await first.StartCommand.ExecuteAsync(null);
        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.UpToDate);

        vm.Preview!.Errors.Add("x: okunamadi");
        Assert.False(vm.UpToDate);
    }

    [Fact]
    public async Task Title_PerOutcome()
    {
        Arc("a.jpg"); Arc("b.jpg");
        var vm = Vm();
        await vm.ScanCommand.ExecuteAsync(null);
        await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal("Bitti", vm.FinishedTitle);

        vm.Result = new BackupResult { Blocked = "Yedek diskinde yer yetmiyor" };
        Assert.Equal("Yedekleme başlatılamadı", vm.FinishedTitle);

        vm.Result = new BackupResult { Aborted = true };
        Assert.Equal("Yedekleme yarıda kaldı", vm.FinishedTitle);
        Assert.True(vm.CanResume);

        vm.Result = new BackupResult { Cancelled = true };
        vm.Stopped = true;
        Assert.Equal("Durduruldu", vm.FinishedTitle);
    }

    [Fact]
    public async Task EarlyBlocked_FreeAndNeededShowDash()
    {
        var vm = Vm();
        vm.Archive = Path.Combine(_tmp, "baska-arsiv");
        Directory.CreateDirectory(vm.Archive);
        vm.Backup = Path.Combine(vm.Archive, "yedek");

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Contains("iç içe", vm.Preview!.Blocked);
        Assert.Equal("—", vm.FreeText);
        Assert.Equal("—", vm.NeededText);
        Assert.False(vm.Surveyed);
    }

    [Fact]
    public async Task Running_BeforeFirstProgress_ShowsRescan()
    {
        Arc("a.jpg");
        var vm = Vm();
        using var gate = new ManualResetEventSlim();
        vm.OnProgressForTest = _ => gate.Wait(TimeSpan.FromSeconds(20));
        await vm.ScanCommand.ExecuteAsync(null);

        var run = vm.StartCommand.ExecuteAsync(null);

        Assert.True(vm.IsPreparing);
        Assert.Equal("Arşiv yeniden taranıyor…", vm.ProgressText);
        gate.Set();
        await run;
        Assert.False(vm.IsPreparing);
    }

    [Fact]
    public void Eta_Rules()
    {
        const long Mb = 1L << 20;
        Assert.Null(BackupFlowViewModel.Eta(15 * Mb, 100 * Mb, TimeSpan.FromSeconds(1)));
        Assert.Null(BackupFlowViewModel.Eta(100 * Mb, 100 * Mb, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(10), BackupFlowViewModel.Eta(100 * Mb, 200 * Mb, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Settings_CorruptLastBackup_Ignored()
    {
        Directory.CreateDirectory(_tmp);
        var path = Path.Combine(_tmp, "ayarlar.json");
        File.WriteAllText(path, "{\"LastBackup\":{\"Archive\":null,\"Backup\":null}}");

        var store = new JsonSettingsStore(path);

        Assert.Null(store.Load().LastBackup);
        Assert.False(store.LoadFailed);
    }
}
