using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Zaman çizelgesinden aya giriş, dönüşte sayıların yerinde güncellenmesi, işaretliyken çıkış soruları.</summary>
public class MonthTimelineTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_mtl_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();

    public MonthTimelineTests()
    {
        // Düzenli arşiv: 2026-08'de 3 foto (adlarından tarih), 2026-09'da 1 foto.
        Put(@"2026-08\2026-08-Foto\IMG_20260803_a.jpg", 100);
        Put(@"2026-08\2026-08-Foto\IMG_20260804_b.jpg", 200);
        Put(@"2026-08\2026-08-Foto\IMG_20260805_c.jpg", 300);
        Put(@"2026-09\2026-09-Foto\IMG_20260901_d.jpg", 400);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    void Put(string rel, int size)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
    }

    async Task<TimelineViewModel> Counted()
    {
        var vm = new TimelineViewModel(_f.Services, _f.Settings.Stored, _root);
        await vm.CountAsync();
        return vm;
    }

    static TimelineCell Aug(TimelineViewModel vm) => vm.Table!.Rows.Single(r => r.Year == 2026).Months[7];

    async Task<MonthViewModel> Enter(TimelineViewModel vm)
    {
        vm.OpenCellCommand.Execute(Aug(vm));
        var m = Assert.IsType<MonthViewModel>(vm.Month);
        await m.LoadAsync();
        return m;
    }

    [Fact]
    public async Task OpenCell_OpensMonth_WithCellFoldersAndFilter()
    {
        var vm = await Counted();
        vm.Filter = KindFilter.All;

        var m = await Enter(vm);

        Assert.Equal((2026, 8, KindFilter.All, TimelineMethod.FolderNames), (m.Year, m.MonthNumber, m.Filter, m.Method));
        Assert.Equal(new[] { Path.Combine(_root, "2026-08") }, m.Folders);
        Assert.Equal(3, m.Photos.Count);
        Assert.Empty(_f.Shell.Opened);
    }

    [Fact]
    public async Task OpenCell_EmptyCell_DoesNothing()
    {
        var vm = await Counted();

        vm.OpenCellCommand.Execute(vm.Table!.Rows.Single(r => r.Year == 2026).Months[0]);

        Assert.Null(vm.Month);
    }

    [Fact]
    public async Task Back_ClosesMonth_TableKept()
    {
        var vm = await Counted();
        var table = vm.Table;
        var m = await Enter(vm);

        m.BackCommand.Execute(null);

        Assert.Null(vm.Month);
        Assert.Same(table, vm.Table);
    }

    [Fact]
    public async Task Discard_UpdatesCellAndFooter_WithoutRecount()
    {
        var vm = await Counted();
        int recounts = 0;
        vm.BeforeCountForTest = _ => recounts++;
        _f.Bin.Can = (_, size) => size < 250; // c.jpg (300) kutuya sığmıyor → _Ayiklananlar
        var m = await Enter(vm);
        m.ToggleMarkCommand.Execute(m.Photos.Single(p => p.Name.EndsWith("a.jpg")));
        m.ToggleMarkCommand.Execute(m.Photos.Single(p => p.Name.EndsWith("c.jpg")));
        m.OpenReviewCommand.Execute(null);
        await m.DiscardCommand.ExecuteAsync(null);

        m.BackCommand.Execute(null);

        Assert.Equal(0, recounts);
        Assert.Equal(1, Aug(vm).Count);
        Assert.Equal(200, Aug(vm).Size);
        Assert.Equal(2, vm.Table!.Total);
        Assert.Contains("_Ayiklananlar: 1 dosya", vm.FooterLines);
    }

    [Fact]
    public async Task ILeaveGuard_ForwardsMonthQuestion()
    {
        var vm = await Counted();
        Assert.Null(((ILeaveGuard)vm).LeaveQuestion);
        var m = await Enter(vm);

        m.ToggleMarkCommand.Execute(m.Photos[0]);

        Assert.Equal("1 dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?", ((ILeaveGuard)vm).LeaveQuestion);
    }

    async Task<(ShellViewModel Shell, TimelineViewModel Vm, MonthViewModel Month)> ShellWithMarks()
    {
        var shell = new ShellViewModel(_f.Services, _root);
        ((HomeViewModel)shell.CurrentPage).StartTimelineCommand.Execute(null);
        var vm = Assert.IsType<TimelineViewModel>(shell.CurrentPage);
        await vm.RunningTask!;
        var m = await Enter(vm);
        m.ToggleMarkCommand.Execute(m.Photos[0]);
        return (shell, vm, m);
    }

    [Fact]
    public async Task GoHome_WithMarks_AsksAndStays()
    {
        var (shell, vm, _) = await ShellWithMarks();
        _f.Dialogs.Answer = false;

        shell.GoHomeCommand.Execute(null);

        Assert.Same(vm, shell.CurrentPage);
        Assert.Contains("Bırakıp çıkılsın mı?", Assert.Single(_f.Dialogs.Asked));
    }

    [Fact]
    public async Task GoHome_WithMarks_Yes_Leaves()
    {
        var (shell, _, _) = await ShellWithMarks();
        _f.Dialogs.Answer = true;

        shell.GoHomeCommand.Execute(null);

        Assert.IsType<HomeViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task Undo_WithMarks_AsksAndStays()
    {
        var (shell, vm, _) = await ShellWithMarks();
        _f.Dialogs.Answer = false;
        _f.Files.Journal = Path.Combine(_root, "yok.jsonl");

        await shell.UndoCommand.ExecuteAsync(null);

        Assert.Same(vm, shell.CurrentPage);
        Assert.Single(_f.Dialogs.Asked);
    }

    [Fact]
    public async Task RequestClose_WithMarks_AsksAndStays()
    {
        var (shell, vm, _) = await ShellWithMarks();
        _f.Dialogs.Answer = false;

        Assert.Equal(CloseDecision.Stay, shell.RequestClose());
        Assert.Same(vm, shell.CurrentPage);

        _f.Dialogs.Answer = true;
        Assert.Equal(CloseDecision.Close, shell.RequestClose());
    }

    [Fact]
    public async Task WhileDiscarding_IsLocked()
    {
        var (shell, vm, m) = await ShellWithMarks();
        using var gate = new ManualResetEventSlim();
        _f.Bin.SendFunc = p => { gate.Wait(TimeSpan.FromSeconds(5)); File.Delete(p); return true; };
        m.OpenReviewCommand.Execute(null);

        var run = m.DiscardCommand.ExecuteAsync(null);
        var page = (ILongRunningPage)vm;
        Assert.True(page.IsLocked);
        Assert.True(page.MustConfirmClose);
        Assert.Same(m.DiscardTask, page.RunningTask);
        shell.GoHomeCommand.Execute(null);
        Assert.Same(vm, shell.CurrentPage);
        gate.Set();
        await run;

        Assert.False(page.IsLocked);
    }
}
