using System.Globalization;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Ay görünümü: yükleme, Izgara günleri, Takvim haftaları, görünüm tercihi.</summary>
public class MonthViewModelTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_mvm_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly Dictionary<string, DateTime?> _dates = new(StringComparer.OrdinalIgnoreCase);

    public MonthViewModelTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    string Month => Path.Combine(_root, "2026-08");

    string Put(string rel, DateTime? taken, int size = 10)
    {
        var p = Path.Combine(Month, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        _dates[p] = taken;
        return p;
    }

    MediaFile Read(string p) => new()
    {
        Path = p, Size = new FileInfo(p).Length, Kind = FileClassifier.Classify(p),
        Taken = _dates[p], DateSource = DateSource.Exif,
    };

    MonthViewModel Vm(KindFilter filter = KindFilter.Photo) =>
        new(_f.Services, _f.Settings.Stored, _root, TimelineMethod.FolderNames, 2026, 8, new[] { Month }, filter, Read);

    async Task<MonthViewModel> Loaded(KindFilter filter = KindFilter.Photo)
    {
        var vm = Vm(filter);
        await vm.LoadAsync();
        Assert.False(vm.IsLoading);
        return vm;
    }

    static readonly DateTime Aug15 = new(2026, 8, 15, 10, 0, 0);

    [Fact]
    public async Task Build_Photo_AttachesRawPair_HidesRaw()
    {
        Put(@"2026-08-Foto\a.jpg", Aug15);
        Put(@"2026-08-Foto\a.cr3", Aug15);

        var vm = await Loaded();

        var p = Assert.Single(vm.Photos);
        Assert.EndsWith("a.jpg", p.File.Path);
        Assert.EndsWith("a.cr3", Assert.Single(p.Raws).Path);
        Assert.True(p.HasRaw);
        Assert.Equal("+RAW", p.RawLabel);
    }

    [Fact]
    public async Task Build_Raw_ShowsAllRaws_NoAttach()
    {
        Put(@"2026-08-Foto\a.jpg", Aug15);
        Put(@"2026-08-Foto\a.cr3", Aug15);
        Put(@"2026-08-Foto\b.cr3", Aug15);

        var vm = await Loaded(KindFilter.Raw);

        Assert.Equal(2, vm.Photos.Count);
        Assert.All(vm.Photos, p => { Assert.Equal(FileKind.Raw, p.File.Kind); Assert.False(p.HasRaw); });
    }

    [Fact]
    public async Task Build_All_LoneRawShown_PairedRawHidden()
    {
        Put(@"2026-08-Foto\a.jpg", Aug15);
        Put(@"2026-08-Foto\a.cr3", Aug15);
        Put(@"2026-08-Foto\b.cr3", Aug15.AddMinutes(1));
        Put(@"2026-08-Video\v.mp4", Aug15.AddMinutes(2));

        var vm = await Loaded(KindFilter.All);

        Assert.Equal(new[] { "a.jpg", "b.cr3", "v.mp4" }, vm.Photos.Select(p => Path.GetFileName(p.File.Path)));
        Assert.True(vm.Photos[0].HasRaw);
    }

    [Fact]
    public async Task Days_GroupedAndHeaded()
    {
        Put("b.jpg", Aug15.AddHours(2));
        Put("a.jpg", Aug15);
        Put("c.jpg", new DateTime(2026, 8, 3, 9, 0, 0));

        var vm = await Loaded();

        Assert.Equal(new[] { "3 Ağustos Pazartesi · 1", "15 Ağustos Cumartesi · 2" }, vm.Days.Select(d => d.Header));
        Assert.Equal(new[] { "a.jpg", "b.jpg" }, vm.Days[1].Photos.Select(p => Path.GetFileName(p.File.Path)));
        Assert.Equal(new DateOnly(2026, 8, 15), vm.Days[1].Day);
    }

    [Fact]
    public async Task OtherMonth_LastGroup_BaskaAydan()
    {
        Put("a.jpg", Aug15);
        Put("eski.jpg", new DateTime(2026, 7, 30));
        Put("tarihsiz.jpg", null);

        var vm = await Loaded();

        var last = vm.Days[^1];
        Assert.Null(last.Day);
        Assert.Equal("Başka aydan · 2", last.Header);
        Assert.All(last.Photos, p => Assert.True(p.OtherMonth));
        Assert.Equal(2, vm.Days.Count);
    }

    [Fact]
    public async Task Weeks_MondayFirst_August2026()
    {
        for (int i = 0; i < 6; i++) Put($"x{i}.jpg", Aug15.AddMinutes(i));
        Put("y.jpg", new DateTime(2026, 8, 3));

        var vm = await Loaded();

        Assert.All(vm.Weeks, w => Assert.Equal(7, w.Count));
        var first = vm.Weeks[0];
        Assert.All(first.Take(5), d => Assert.False(d.InMonth));
        Assert.True(first[5].InMonth);
        Assert.Equal(1, first[5].Day);
        var d15 = vm.Weeks.SelectMany(w => w).Single(d => d.InMonth && d.Day == 15);
        Assert.Equal(6, d15.Count);
        Assert.Equal(4, d15.Preview.Count);
        Assert.Equal(2, d15.More);
        var d3 = vm.Weeks.SelectMany(w => w).Single(d => d.InMonth && d.Day == 3);
        Assert.Equal((1, 0), (d3.Count, d3.More));
        Assert.Equal(31, vm.Weeks.SelectMany(w => w).Count(d => d.InMonth));
    }

    [Fact]
    public async Task OpenDay_SwitchesToGrid_SetsScrollTarget()
    {
        Put("a.jpg", Aug15);
        var vm = await Loaded();
        vm.Mode = MonthViewMode.Calendar;

        vm.OpenDayCommand.Execute(vm.Weeks.SelectMany(w => w).Single(d => d.InMonth && d.Day == 15));

        Assert.Equal(MonthViewMode.Grid, vm.Mode);
        Assert.Equal(new DateOnly(2026, 8, 15), vm.ScrollTarget);
    }

    [Fact]
    public async Task OpenDay_EmptyDay_Ignored()
    {
        Put("a.jpg", Aug15);
        var vm = await Loaded();
        vm.Mode = MonthViewMode.Calendar;

        vm.OpenDayCommand.Execute(vm.Weeks.SelectMany(w => w).Single(d => d.InMonth && d.Day == 2));

        Assert.Equal(MonthViewMode.Calendar, vm.Mode);
    }

    [Fact]
    public void Mode_Remembered_InSettings()
    {
        Assert.Equal(MonthViewMode.Grid, Vm().Mode);

        Vm().Mode = MonthViewMode.Calendar;

        Assert.Equal("Calendar", _f.Settings.Stored.MonthView);
        Assert.True(_f.Settings.Saves > 0);
        Assert.Equal(MonthViewMode.Calendar, Vm().Mode);
    }

    [Fact]
    public async Task Load_ShowsTitleAndSummary()
    {
        Put("a.jpg", Aug15, size: 1000);
        Put("b.jpg", Aug15, size: 1000);
        Put("c.jpg", Aug15, size: 1000);

        var vm = await Loaded();

        Assert.Equal("Ağustos 2026", vm.Title);
        Assert.StartsWith("3 dosya · ", vm.Summary);
        Assert.Equal(new[] { Month }, vm.Folders);
    }

    [Fact]
    public async Task Stop_DuringLoad_Cancels()
    {
        Put("a.jpg", Aug15);
        using var gate = new ManualResetEventSlim();
        var vm = new MonthViewModel(_f.Services, _f.Settings.Stored, _root, TimelineMethod.FolderNames, 2026, 8,
            new[] { Month }, KindFilter.Photo, p => { gate.Wait(TimeSpan.FromSeconds(5)); return Read(p); });

        var load = vm.LoadAsync();
        Assert.True(vm.IsLoading);
        vm.StopCommand.Execute(null);
        gate.Set();
        await load;

        Assert.False(vm.IsLoading);
        Assert.True(vm.Stopped);
        Assert.Empty(vm.Photos);
    }

    [Fact]
    public async Task OpenFolder_OpensInExplorer()
    {
        Put("a.jpg", Aug15);
        var vm = await Loaded();

        vm.OpenFolderCommand.Execute(Month);

        Assert.Equal(Month, Assert.Single(_f.Shell.Opened));
    }
}
