using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class TimelineViewModelTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_tlvm_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly ManualResetEventSlim _gate = new();

    public TimelineViewModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        _gate.Set();
        try { Directory.Delete(_tmp, true); } catch { }
    }

    string Dir(string name) { var d = Path.Combine(_tmp, name); Directory.CreateDirectory(d); return d; }

    string Put(string root, string rel)
    {
        var p = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    /// <summary>Düzenlenmiş arşiv: 2022-11'de 2 foto 1 video, 2023-01'de 1 foto.</summary>
    string Organized()
    {
        var a = Dir("arsiv");
        Put(a, @"2022-11\2022-11-Foto\a.jpg");
        Put(a, @"2022-11\2022-11-Foto\b.jpg");
        Put(a, @"2022-11\2022-11-Video\v.mp4");
        Put(a, @"2023-01\2023-01-Foto\c.jpg");
        return a;
    }

    /// <summary>Düzenlenmemiş: adlarından tarih çıkan dosyalar iki klasörde.</summary>
    string Loose()
    {
        var d = Dir("dagnik");
        Put(d, @"Tatil\IMG_20210704_a.jpg");
        Put(d, @"Tatil\IMG_20210705_b.jpg");
        Put(d, @"WhatsApp\IMG_20210706_c.jpg");
        Put(d, @"Tatil\IMG_20210801_d.jpg");
        return d;
    }

    TimelineViewModel Vm(string? start = null) => new(_f.Services, _f.Settings.Stored, start);

    static async Task Counted(TimelineViewModel vm)
    {
        await vm.CountAsync();
        Assert.False(vm.IsCounting);
    }

    [Fact]
    public void Ctor_FolderOrder_StartFolder_ThenLastTimeline_ThenLastArchive()
    {
        string a = Dir("a"), b = Dir("b"), c = Dir("c");
        _f.Settings.Stored.LastTimelineFolder = b;
        _f.Settings.Stored.Recent.Add(new RecentPair(new() { "x" }, c, DateTime.Now));
        Assert.Equal(a, Vm(a).Folder);
        Assert.Equal(b, Vm().Folder);
        _f.Settings.Stored.LastTimelineFolder = null;
        Assert.Equal(c, Vm().Folder);
    }

    [Fact]
    public void Ctor_SkipsMissingFolders()
    {
        var c = Dir("c");
        _f.Settings.Stored.LastTimelineFolder = Path.Combine(_tmp, "silindi");
        _f.Settings.Stored.Recent.Add(new RecentPair(new() { "x" }, c, DateTime.Now));
        Assert.Equal(c, Vm(Path.Combine(_tmp, "yok")).Folder);
    }

    [Fact]
    public async Task Ctor_NoCandidate_FolderNull_CountDoesNothing()
    {
        var vm = Vm();
        Assert.Null(vm.Folder);
        await vm.CountAsync();
        Assert.Null(vm.Result);
        Assert.Equal("", vm.MethodReason);
    }

    [Fact]
    public async Task Count_FolderNames_BuildsPhotoTable()
    {
        var vm = Vm(Organized());
        Assert.Equal(TimelineMethod.FolderNames, vm.Method);
        Assert.Contains("bulundu", vm.MethodReason);
        await Counted(vm);
        Assert.Equal(KindFilter.Photo, vm.Filter);
        Assert.Equal(3, vm.Table!.Total);
    }

    [Fact]
    public async Task Count_UnorganizedFolder_DefaultsToCaptureDate_WithReason()
    {
        var vm = Vm(Loose());
        Assert.Equal(TimelineMethod.CaptureDate, vm.Method);
        Assert.Contains("bulunamadı", vm.MethodReason);
        await Counted(vm);
        Assert.Equal(4, vm.Table!.Total);
        Assert.Equal(4, vm.Total);
        Assert.Equal("4 / 4 dosya okundu", vm.ProgressText);
    }

    [Fact]
    public async Task FilterChange_RebuildsTable_WithoutRecount()
    {
        var vm = Vm(Organized());
        await Counted(vm);
        var result = vm.Result;
        vm.Filter = KindFilter.All;
        Assert.Same(result, vm.Result);
        Assert.Equal(4, vm.Table!.Total);
        vm.Filter = KindFilter.Video;
        Assert.Equal(1, vm.Table!.Total);
    }

    [Fact]
    public async Task MethodChange_Recounts()
    {
        var vm = Vm(Organized());
        await Counted(vm);
        vm.Method = TimelineMethod.CaptureDate;
        await vm.RunningTask!;
        Assert.Equal(TimelineMethod.CaptureDate, vm.Result!.Method);
    }

    [Fact]
    public async Task ChangeFolder_SavesLastTimelineFolder_AndCounts()
    {
        var a = Organized();
        var vm = Vm();
        _f.Folders.Next.Enqueue(a);
        vm.ChangeFolderCommand.Execute(null);
        await vm.RunningTask!;
        Assert.Equal(a, vm.Folder);
        Assert.Equal(a, _f.Settings.Stored.LastTimelineFolder);
        Assert.True(_f.Settings.Saves >= 1);
        Assert.Equal(3, vm.Table!.Total);
    }

    [Fact]
    public void ChangeFolder_Cancelled_KeepsFolder()
    {
        var a = Dir("a");
        var vm = Vm(a);
        vm.ChangeFolderCommand.Execute(null);
        Assert.Equal(a, vm.Folder);
        Assert.Equal(0, _f.Settings.Saves);
    }

    [Fact]
    public async Task Stop_CancelsCount_SetsStopped_TableNull()
    {
        var vm = Vm(Organized());
        var entered = new ManualResetEventSlim();
        vm.BeforeCountForTest = _ => { entered.Set(); _gate.Wait(TimeSpan.FromSeconds(20)); };
        var t = vm.CountAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
        Assert.True(vm.IsCounting);
        vm.StopCommand.Execute(null);
        _gate.Set();
        await t;
        Assert.True(vm.Stopped);
        Assert.False(vm.IsCounting);
        Assert.Null(vm.Table);
    }

    [Fact]
    public async Task FolderChangeDuringCount_OldResultDiscarded()
    {
        string a = Organized(), b = Loose();
        var vm = Vm(a);
        var entered = new ManualResetEventSlim();
        vm.BeforeCountForTest = f => { if (f == a) { entered.Set(); _gate.Wait(TimeSpan.FromSeconds(20)); } };
        var first = vm.CountAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(20)));
        vm.Folder = b;
        var second = vm.RunningTask!;
        _gate.Set();
        await Task.WhenAll(first, second);
        Assert.Equal(b, vm.Result!.Root);
        Assert.Equal(4, vm.Table!.Total);
        Assert.False(vm.IsCounting);
    }

    [Fact]
    public async Task OpenCell_FolderNames_OpensMonthView_NotExplorer()
    {
        var a = Organized();
        var vm = Vm(a);
        await Counted(vm);
        var cell = vm.Table!.Rows[0].Months[10];
        vm.OpenCellCommand.Execute(cell);
        Assert.Equal(new[] { Path.Combine(a, "2022-11") }, vm.Month!.Folders);
        Assert.True(vm.HasMonth);
        Assert.Empty(_f.Shell.Opened);
    }

    [Fact]
    public async Task OpenCell_CaptureDate_MultiFolder_MonthGetsAllFolders()
    {
        var d = Loose();
        var vm = Vm(d);
        await Counted(vm);
        var jul = vm.Table!.Rows[0].Months[6];
        vm.OpenCellCommand.Execute(jul);
        Assert.Equal(TimelineMethod.CaptureDate, vm.Month!.Method);
        Assert.Equal(new[] { Path.Combine(d, "Tatil"), Path.Combine(d, "WhatsApp") }, vm.Month.Folders);
        Assert.True(vm.Month.HasManyFolders);
        Assert.Equal("Tatil", vm.ShortPath(jul.Folders[0].Folder));
        Assert.Equal(".", vm.ShortPath(d));

        vm.Month.OpenFolderCommand.Execute(jul.Folders[1].Folder);
        Assert.Equal(Path.Combine(d, "WhatsApp"), Assert.Single(_f.Shell.Opened));
    }

    [Fact]
    public async Task OpenCell_EmptyCell_DoesNothing()
    {
        var vm = Vm(Organized());
        await Counted(vm);
        vm.OpenCellCommand.Execute(vm.Table!.Rows[0].Months[0]);
        Assert.Null(vm.Month);
        Assert.Empty(_f.Shell.Opened);
    }

    [Fact]
    public async Task FooterLines_SpecialOutsideErrorsAndCopyNote()
    {
        var a = Organized();
        Put(a, "gevsek.jpg");
        Put(a, @"Tatil\g.jpg");
        Put(a, @"_Tekrarlar\2022-11\t.jpg");
        var vm = Vm(a);
        await Counted(vm);
        Assert.Contains("2 dosya Yıl-Ay klasörleri dışında", vm.FooterLines);
        Assert.Contains("_Tekrarlar: 1 dosya", vm.FooterLines);
        Assert.True(vm.CanUseCaptureDate);

        vm.UseCaptureDateCommand.Execute(null);
        await vm.RunningTask!;
        Assert.Equal(TimelineMethod.CaptureDate, vm.Method);
        Assert.Contains("Kopyalar ayıklanmaz; aynı fotoğrafın kopyaları ayrı sayılır.", vm.FooterLines);
        Assert.False(vm.CanUseCaptureDate);
    }

    [Fact]
    public async Task FooterLines_CaptureDate_TotalOfGuessedDates_FollowsFilter()
    {
        var d = Loose();
        var vm = Vm(d);
        await Counted(vm);
        Assert.Contains("4 dosyanın tarihi dosya adından / dosya zamanından", vm.FooterLines);
        vm.Filter = KindFilter.Video;
        Assert.DoesNotContain(vm.FooterLines, l => l.Contains("dosya adından"));
    }

    [Fact]
    public void LongRunningPage_NeverLocks_NeverAsks()
    {
        ILongRunningPage p = Vm(Dir("a"));
        Assert.False(p.IsLocked);
        Assert.False(p.MustConfirmClose);
    }

    [Fact]
    public async Task VanishedFolder_DuringCount_NoCrash()
    {
        var a = Organized();
        var vm = Vm(a);
        vm.BeforeCountForTest = _ => Directory.Delete(a, true);
        await Counted(vm);
        Assert.Contains("1 dosya ya da klasör okunamadı", vm.FooterLines);
        Assert.Equal(0, vm.Table!.Total);
    }
}
