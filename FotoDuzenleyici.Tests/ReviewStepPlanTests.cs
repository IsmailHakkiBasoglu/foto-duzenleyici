using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>İnceleme kararları plana yansır: eskiyen plan, Onayla'ya geçişte yeniden kurma, RAW sorusu.</summary>
public class ReviewStepPlanTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_rsplan_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly ManualScheduler _clock = new();
    public ReviewStepPlanTests()
    {
        Directory.CreateDirectory(_tmp);
        _f.Schedule = _clock.Fn;
    }
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static readonly DateTime T = new(2024, 3, 10, 9, 0, 0);

    string Put(string rel, byte[] bytes)
    {
        var p = Path.Combine(_tmp, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
        File.SetCreationTime(p, T); File.SetLastWriteTime(p, T);
        return p;
    }

    string Big = "", Small = "", Night = "", NightRaw = "", DupA = "", DupB = "";

    /// <summary>Küçültülmüş kopya çifti, RAW eşli karanlık kare, iki konumlu birebir kopya.</summary>
    void Samples()
    {
        Big = Put(@"kaynak\IMG_0001.JPG", TestImages.PatternJpeg(1200, 800, 5, 95));
        Small = Put(@"kaynak\WhatsApp Image 2024-03-10.jpeg", TestImages.PatternJpeg(400, 267, 5, 60));
        Night = Put(@"kaynak\gece.jpg", TestImages.DarkPatternJpeg(1600, 1200, 9, 0.08));
        NightRaw = Put(@"kaynak\gece.CR3", Enumerable.Range(0, 4000).Select(i => (byte)(i % 251)).ToArray());
        DupA = Put(@"kaynak\belge\a.pdf", "%PDF-1.4 ayni"u8.ToArray());
        DupB = Put(@"kaynak\yedek\a.pdf", "%PDF-1.4 ayni"u8.ToArray());
    }

    async Task<OrganizeFlowViewModel> Scanned()
    {
        Samples();
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        Directory.CreateDirectory(Path.Combine(_tmp, "arsiv"));
        vm.Folders.Load(new RecentPair(new List<string> { Path.Combine(_tmp, "kaynak") }, Path.Combine(_tmp, "arsiv"), DateTime.Now));
        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(FlowStep.Review, vm.Step);
        return vm;
    }

    static PlanItem ItemOf(Plan p, string path) => p.Items.Single(i => i.File.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
    static SimilarRow RowOf(FrameReviewViewModel s, string path) =>
        s.Groups.SelectMany(g => g.Rows).Single(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task ReleaseSimilar_MarksStale_AndPlanHasSimilar()
    {
        var vm = await Scanned();
        var r = vm.Review!;
        Assert.False(r.PlanStale);

        r.Similar.ToggleCommand.Execute(RowOf(r.Similar, Small));

        Assert.True(r.PlanStale);
        Assert.True(r.PrepareForConfirm());
        Assert.False(r.PlanStale);
        Assert.Equal(PlanAction.Similar, ItemOf(r.Plan, Small).Action);
        Assert.Equal(1, r.Summary.SimilarCount);
        Assert.Contains(r.Rows, i => i.Action == PlanAction.Similar);
    }

    [Fact]
    public async Task RawAnswer_No_RawMovesNormally()
    {
        var vm = await Scanned();
        var r = vm.Review!;
        _f.Dialogs.NextAsk = Answer.No;
        r.Flawed.ToggleCommand.Execute(RowOf(r.Flawed, Night));

        Assert.True(r.PrepareForConfirm());

        Assert.Equal(1, _f.Dialogs.AskedCount);
        Assert.Equal(PlanAction.Flawed, ItemOf(r.Plan, Night).Action);
        Assert.Equal(PlanAction.Move, ItemOf(r.Plan, NightRaw).Action);
    }

    [Fact]
    public async Task RawAnswer_Yes_RawFollows()
    {
        var vm = await Scanned();
        var r = vm.Review!;
        _f.Dialogs.NextAsk = Answer.Yes;
        r.Flawed.ToggleCommand.Execute(RowOf(r.Flawed, Night));

        Assert.True(r.PrepareForConfirm());

        Assert.Equal(PlanAction.Flawed, ItemOf(r.Plan, NightRaw).Action);
        Assert.Equal(2, r.Summary.FlawedCount);
    }

    [Fact]
    public async Task RawAnswer_Cancel_StaysInReview()
    {
        var vm = await Scanned();
        _f.Dialogs.NextAsk = Answer.Cancel;
        vm.Review!.Flawed.ToggleCommand.Execute(RowOf(vm.Review.Flawed, Night));

        vm.GoToConfirmCommand.Execute(null);

        Assert.Equal(FlowStep.Review, vm.Step);
        Assert.Null(vm.Confirm);
        Assert.True(vm.Review.PlanStale);
    }

    [Fact]
    public async Task Confirm_NotStale_DoesNotAskAgain()
    {
        var vm = await Scanned();
        vm.Review!.Flawed.ToggleCommand.Execute(RowOf(vm.Review.Flawed, Night));

        vm.GoToConfirmCommand.Execute(null);
        Assert.Equal(FlowStep.Confirm, vm.Step);
        Assert.Equal(2, vm.Confirm!.Summary.FlawedCount); // öntanımlı cevap Evet: RAW eşi de ayrılır
        vm.BackCommand.Execute(null);
        vm.GoToConfirmCommand.Execute(null);

        Assert.Equal(FlowStep.Confirm, vm.Step);
        Assert.Equal(1, _f.Dialogs.AskedCount);
    }

    [Fact]
    public async Task DuplicateManualChoice_GoesToPlan()
    {
        var vm = await Scanned();
        var r = vm.Review!;
        var g = Assert.Single(r.Duplicates.Groups);
        var notKept = g.Rows.Single(x => !x.IsKeeper);
        Assert.Equal(PlanAction.Duplicate, ItemOf(r.Plan, notKept.File.Path).Action);

        notKept.IsKeeper = true;

        Assert.True(r.PlanStale);
        Assert.True(r.PrepareForConfirm());
        Assert.NotEqual(PlanAction.Duplicate, ItemOf(r.Plan, notKept.File.Path).Action);
        Assert.Equal(PlanAction.Duplicate, ItemOf(r.Plan, g.Rows.Single(x => x != notKept).File.Path).Action);
    }

    [Fact]
    public async Task RestoredRelease_StartsStale()
    {
        var store = new ReviewStore(_f.Caches.Reviews);
        Samples();
        store.SetKeep(HashCache.Key(Small), false);
        store.Save();
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        Directory.CreateDirectory(Path.Combine(_tmp, "arsiv"));
        vm.Folders.Load(new RecentPair(new List<string> { Path.Combine(_tmp, "kaynak") }, Path.Combine(_tmp, "arsiv"), DateTime.Now));
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.Review!.PlanStale);
        Assert.True(vm.Review.PrepareForConfirm());
        Assert.Equal(PlanAction.Similar, ItemOf(vm.Review.Plan, Small).Action);
    }

    [Fact]
    public async Task Rescan_OldGroupsDoNotTouchNewReview()
    {
        var vm = await Scanned();
        var old = vm.Review!;
        var oldRow = RowOf(old.Similar, Small);

        await vm.RescanCommand.ExecuteAsync(null);

        Assert.NotSame(old, vm.Review);
        oldRow.Keep = false;
        Assert.False(vm.Review!.PlanStale);
        Assert.True(RowOf(vm.Review.Similar, Small).Keep);
    }

    [Fact]
    public async Task Sections_ShowSectionCommand()
    {
        var vm = await Scanned();
        Assert.Equal(ReviewSection.Summary, vm.Review!.Section);
        vm.Review.ShowSectionCommand.Execute(ReviewSection.Flawed);
        Assert.Equal(ReviewSection.Flawed, vm.Review.Section);
    }

    [Fact]
    public async Task Close_FlushesPendingDecisions()
    {
        Samples();
        _f.Settings.Stored.AddRecent(new[] { Path.Combine(_tmp, "kaynak") }, Path.Combine(_tmp, "arsiv"), DateTime.Now);
        Directory.CreateDirectory(Path.Combine(_tmp, "arsiv"));
        var shell = new ShellViewModel(_f.Services);
        var home = (HomeViewModel)shell.CurrentPage;
        home.StartOrganizeCommand.Execute(home.LastOrganize);
        var flow = (OrganizeFlowViewModel)shell.CurrentPage;
        await flow.ScanCommand.ExecuteAsync(null);
        flow.Review!.Similar.ToggleCommand.Execute(RowOf(flow.Review.Similar, Small));
        Assert.Null(new ReviewStore(_f.Caches.Reviews).GetKeep(HashCache.Key(Small))); // zamanlayıcı çalışmadı

        Assert.Equal(CloseDecision.Close, shell.RequestClose());

        Assert.Equal(false, new ReviewStore(_f.Caches.Reviews).GetKeep(HashCache.Key(Small)));
    }
}
