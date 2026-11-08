using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>2. parça son gözden geçirme bulguları (her biri önce kırmızı).</summary>
public class ReviewFinalFixTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_rfix_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly FakeCompare _compare = new();
    readonly ManualScheduler _clock = new();
    readonly List<string> _warnings = new();
    public ReviewFinalFixTests() { Directory.CreateDirectory(_tmp); _f.Schedule = _clock.Fn; }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static PlanItem Item(PlanAction a, long size) => new()
    {
        File = new MediaFile { Path = @"C:\k\" + Guid.NewGuid().ToString("N") + ".jpg", Size = size, Kind = FileKind.Photo },
        Action = a, TargetDir = @"D:\Arsiv\x", TargetName = "x.jpg",
    };

    // Önemli 1: _Benzerler ve _Kusurlu'ya gidenler de kopyalanır; yer hesabına girmeli.
    [Fact]
    public void Confirm_NeedBytes_IncludesSimilarAndFlawed()
    {
        var plan = new Plan();
        plan.Items.Add(Item(PlanAction.Move, 100));
        plan.Items.Add(Item(PlanAction.Duplicate, 10));
        plan.Items.Add(Item(PlanAction.Similar, 1000));
        plan.Items.Add(Item(PlanAction.Flawed, 5000));
        var opt = new PlanOptions { DestinationRoot = _tmp };
        var c = new ConfirmStepViewModel(plan, new OrganizeJob(new OrganizeSession(new[] { _tmp }, opt)), _f.Services, _f.Settings.Stored, DateTime.Now);
        Assert.Equal(6110, c.NeedBytes);
    }

    static MediaFile Photo(string name, int day) => new()
    {
        Path = @"C:\f\" + name, Kind = FileKind.Photo, Taken = new DateTime(2024, 3, day), DateSource = DateSource.Exif,
    };

    FlawedReviewViewModel Flawed(out int[] changed)
    {
        var scores = new Dictionary<MediaFile, QualityScore>
        {
            [Photo("a.jpg", 1)] = new(5, 120, 0, false), [Photo("b.jpg", 2)] = new(400, 10, 0.9, false),
        };
        var list = FlawedList.Build(scores, Array.Empty<SimilarGroupState>(), 1);
        foreach (var r in list.SelectMany(g => g.Rows)) r.StoreKey = r.Name;
        var box = new int[1];
        changed = box;
        var saver = new ReviewSaver(new ReviewStore(Path.Combine(_tmp, "k.json")), _clock.Fn, _warnings.Add);
        return new FlawedReviewViewModel(list, true, saver, _f.Services with { Compare = _compare }, () => box[0]++, _warnings.Add);
    }

    // Önemli 2: süzgeç değişmedikçe görünen liste aynı nesne kalmalı (yoksa ızgara başa atlar).
    [Fact]
    public void Flawed_Visible_SameInstanceUntilFilterChanges()
    {
        var vm = Flawed(out _);
        var v1 = vm.Visible;
        Assert.Same(v1, vm.Visible);
        vm.ToggleCommand.Execute(vm.Groups[0].Rows[0]);
        Assert.Same(v1, vm.Visible);
        vm.ShowDark = false;
        Assert.NotSame(v1, vm.Visible);
    }

    [Fact]
    public void Flawed_AfterCompare_SelectedIsCurrent_ListNotRebuilt()
    {
        var vm = Flawed(out _);
        var v1 = vm.Visible;
        _compare.ReturnIndex = 1;
        vm.OpenCompareCommand.Execute(vm.Groups[0]);
        Assert.Same(vm.Groups[1], vm.Selected);
        Assert.Same(v1, vm.Visible);
    }

    // Küçük 2 → önemli: karşılaştırmada yalnız "incelendi" demek planı eskitmemeli; kalsın/ayıkla eskitmeli.
    [Fact]
    public void Compare_OnlyReviewed_DoesNotMarkStale_KeepChangeDoes()
    {
        var vm = Flawed(out var changed);
        _compare.During = (groups, store, cb) => { groups[0].SetReviewed(store, true); cb(); };
        vm.OpenCompareCommand.Execute(vm.Groups[0]);
        Assert.Equal(0, changed[0]);

        _compare.During = (groups, store, cb) => { groups[0].Rows[0].Keep = false; cb(); };
        vm.OpenCompareCommand.Execute(vm.Groups[0]);
        Assert.Equal(1, changed[0]);
    }

    static readonly DateTime T = new(2024, 3, 10, 9, 0, 0);
    string Put(string rel, byte[] bytes)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
        File.SetCreationTime(p, T); File.SetLastWriteTime(p, T);
        return p;
    }

    async Task<(ShellViewModel Shell, OrganizeFlowViewModel Flow, string Small)> ShellScanned()
    {
        Put(@"kaynak\IMG_0001.JPG", TestImages.PatternJpeg(1200, 800, 5, 95));
        var small = Put(@"kaynak\WhatsApp Image 2024-03-10.jpeg", TestImages.PatternJpeg(400, 267, 5, 60));
        Directory.CreateDirectory(Path.Combine(_tmp, "arsiv"));
        _f.Settings.Stored.AddRecent(new[] { Path.Combine(_tmp, "kaynak") }, Path.Combine(_tmp, "arsiv"), DateTime.Now);
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        return (shell, flow, small);
    }

    // Küçük 1 → önemli: Klasik görünüm karar dosyasını açılışta okur; bekleyen karar önce yazılmalı.
    [Fact]
    public async Task OpenClassic_FlushesPendingDecisions()
    {
        var (shell, flow, small) = await ShellScanned();
        var row = flow.Review!.Similar.Groups.SelectMany(g => g.Rows).Single(r => r.Path == small);
        flow.Review.Similar.ToggleCommand.Execute(row);

        shell.OpenClassicCommand.Execute(null);

        Assert.Equal(1, _f.Shell.Classic);
        Assert.Equal(false, new ReviewStore(_f.Caches.Reviews).GetKeep(HashCache.Key(small)));
    }

    // Küçük 3 → önemli: plan eskiyken CSV eski planı yazmamalı.
    [Fact]
    public async Task SaveCsv_WhenStale_WritesRebuiltPlan()
    {
        var (_, flow, small) = await ShellScanned();
        var row = flow.Review!.Similar.Groups.SelectMany(g => g.Rows).Single(r => r.Path == small);
        flow.Review.Similar.ToggleCommand.Execute(row);
        var csv = Path.Combine(_tmp, "plan.csv");
        _f.Files.Csv = csv;

        flow.Review.SaveCsvCommand.Execute(null);

        Assert.False(flow.Review.PlanStale);
        Assert.Contains(File.ReadAllLines(csv), l => l.Contains("WhatsApp Image") && l.StartsWith("Similar"));
    }
}
