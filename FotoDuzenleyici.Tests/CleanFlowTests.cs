using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Temizle akışı uçtan uca: tara, incele, taşı, Geri Al; öncelikli klasör, iç içe kökler, durdur/devam.</summary>
public class CleanFlowTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_cleanflow_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public CleanFlowTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string D(string rel) => Path.Combine(_tmp, rel);

    string Put(string rel, byte[] bytes, int day = 10)
    {
        var p = D(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, bytes);
        var t = new DateTime(2024, 3, day, 9, 0, 0);
        File.SetCreationTime(p, t); File.SetLastWriteTime(p, t);
        return p;
    }

    static byte[] Pdf(string s) => System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 " + s);

    string DupOld = "", DupNew = "", Big = "", Small = "", Night = "";

    void Samples()
    {
        DupOld = Put(@"klasor\a.pdf", Pdf("ayni"), day: 1);
        DupNew = Put(@"klasor\alt\a.pdf", Pdf("ayni"), day: 5);
        Big = Put(@"klasor\IMG_0001.JPG", TestImages.PatternJpeg(1200, 800, 5, 95));
        Small = Put(@"klasor\WhatsApp Image 2024-03-10.jpeg", TestImages.PatternJpeg(400, 267, 5, 60));
        Night = Put(@"klasor\gece.jpg", TestImages.DarkPatternJpeg(1600, 1200, 9, 0.08));
    }

    OrganizeFlowViewModel Flow(IEnumerable<string> folders, IEnumerable<string>? priority = null)
    {
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored, FlowKind.Clean);
        vm.Folders.Load(new RecentPair(folders.ToList(), "", DateTime.Now, priority?.ToList()));
        return vm;
    }

    static SimilarRow Row(FrameReviewViewModel s, string path) =>
        s.Groups.SelectMany(g => g.Rows).Single(r => r.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    string Rejects(string sub, string rel) => Path.Combine(D("klasor"), "_Ayiklananlar", sub, rel);

    [Fact]
    public async Task Clean_ScanReviewApplyUndo()
    {
        Samples();
        var vm = Flow(new[] { D("klasor") });
        Assert.Equal(FlowKind.Clean, vm.Kind);
        Assert.True(vm.Folders.CanScan);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.Equal(FlowStep.Review, vm.Step);
        var r = vm.Review!;
        Assert.Equal(FlowKind.Clean, r.Kind);
        r.Similar.ToggleCommand.Execute(Row(r.Similar, Small));
        r.Flawed.ToggleCommand.Execute(Row(r.Flawed, Night));

        vm.GoToConfirmCommand.Execute(null);
        var c = vm.Confirm!;
        Assert.True(c.MoveOnly);
        Assert.False(c.ShowSpace);
        Assert.Empty(c.OtherDriveSources);
        Assert.True(c.CanApply);
        Assert.Equal(Path.Combine(D("klasor"), "_Ayiklananlar"), c.Archive);
        Assert.StartsWith(Path.Combine(D("klasor"), "_Ayiklananlar", "_Gunluk", "temizlik-"), c.JournalPath);
        Assert.Equal(@"→ _Ayiklananlar\Tekrarlar", c.DupTarget);
        Assert.Equal(@"→ _Ayiklananlar\Benzerler", c.SimilarTarget);
        Assert.Equal(@"→ _Ayiklananlar\Kusurlu", c.FlawedTarget);
        Assert.Equal(c.Plan.Items.Sum(i => i.File.Size), c.RejectBytes);

        c.ApplyCommand.Execute(null);
        await vm.ApplyTask!;
        Assert.Equal(3, vm.Apply!.DoneCount);
        Assert.False(File.Exists(DupNew));
        Assert.False(File.Exists(Small));
        Assert.False(File.Exists(Night));
        Assert.True(File.Exists(DupOld));
        Assert.True(File.Exists(Big));
        Assert.Equal(Pdf("ayni"), File.ReadAllBytes(Rejects("Tekrarlar", @"alt\a.pdf")));
        Assert.True(File.Exists(Rejects("Benzerler", "WhatsApp Image 2024-03-10.jpeg")));
        Assert.True(File.Exists(Rejects("Kusurlu", "gece.jpg")));

        await vm.Apply.UndoCommand.ExecuteAsync(null);
        Assert.True(File.Exists(DupNew));
        Assert.True(File.Exists(Small));
        Assert.True(File.Exists(Night));
        Assert.False(File.Exists(Rejects("Tekrarlar", @"alt\a.pdf")));
        Assert.False(File.Exists(Rejects("Kusurlu", "gece.jpg")));
    }

    [Fact]
    public async Task Clean_PreferredFolderKeeper()
    {
        var old = Put(@"klasor\a.pdf", Pdf("ayni"), day: 1);
        var pref = Put(@"klasor\onemli\a.pdf", Pdf("ayni"), day: 5);
        var vm = Flow(new[] { D("klasor") }, new[] { D(@"klasor\onemli") });
        await vm.ScanCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Review!.Plan.Items);
        Assert.Equal(old, item.File.Path);
        Assert.Equal("öncelikli", vm.Review.Duplicates.PreferredLabel);
        Assert.True(vm.Review.Duplicates.Groups[0].Rows.Single(x => x.File.Path == pref).IsKeeper);
    }

    [Fact]
    public async Task Clean_NestedFolders_SingleRoot()
    {
        Put(@"klasor\a.pdf", Pdf("ayni"), day: 1);
        var copy = Put(@"klasor\alt\a.pdf", Pdf("ayni"), day: 5);
        var vm = Flow(new[] { D("klasor"), D(@"klasor\alt") });
        await vm.ScanCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Review!.Plan.Items);
        Assert.Equal(copy, item.File.Path);
        Assert.Equal(Rejects("Tekrarlar", @"alt\a.pdf"), item.TargetPath);
    }

    [Fact]
    public async Task Clean_StopThenResume()
    {
        Put(@"klasor\a.pdf", Pdf("bir"), day: 1);
        var a2 = Put(@"klasor\x\a.pdf", Pdf("bir"), day: 5);
        Put(@"klasor\b.pdf", Pdf("iki"), day: 1);
        var b2 = Put(@"klasor\x\b.pdf", Pdf("iki"), day: 5);
        _f.Settings.Stored.FindSimilar = false; _f.Settings.Stored.MarkFlawed = false;
        var vm = Flow(new[] { D("klasor") });
        await vm.ScanCommand.ExecuteAsync(null);
        vm.GoToConfirmCommand.Execute(null);
        vm.OnApplyProgressForTest = done => { if (done >= 1) vm.Apply?.CancelCommand.Execute(null); };

        vm.Confirm!.ApplyCommand.Execute(null);
        await vm.ApplyTask!;
        Assert.True(vm.Apply!.Stopped);
        Assert.Equal(1, vm.Apply.PendingAfterStop);

        vm.OnApplyProgressForTest = null;
        await vm.Apply.ResumeCommand.ExecuteAsync(null);
        Assert.False(File.Exists(a2));
        Assert.False(File.Exists(b2));
        Assert.True(File.Exists(Rejects("Tekrarlar", @"x\a.pdf")));
        Assert.True(File.Exists(Rejects("Tekrarlar", @"x\b.pdf")));
    }

    [Fact]
    public async Task Clean_SummaryMatchesPlan()
    {
        Samples();
        var vm = Flow(new[] { D("klasor") });
        await vm.ScanCommand.ExecuteAsync(null);
        var r = vm.Review!;
        r.Similar.ToggleCommand.Execute(Row(r.Similar, Small));
        Assert.True(r.PrepareForConfirm());

        Assert.Equal(r.Plan.Items.Count(i => i.Action == PlanAction.Duplicate), r.Summary.DupCount);
        Assert.Equal(1, r.Summary.SimilarCount);
        Assert.Equal(0, r.Summary.Undated);
        Assert.Equal(r.Plan.Items.Sum(i => i.File.Size), r.RejectBytes);
        Assert.Equal(r.Plan.Items.Count, r.Summary.ApplyCount);
        Assert.StartsWith($"Plan hazır: {r.Plan.Items.Count} dosya _Ayiklananlar'a taşınacak (", r.BarTitle);
    }

    [Fact]
    public async Task Organize_AfterClean_IgnoresRejects()
    {
        Samples();
        var clean = Flow(new[] { D("klasor") });
        await clean.ScanCommand.ExecuteAsync(null);
        clean.GoToConfirmCommand.Execute(null);
        clean.Confirm!.ApplyCommand.Execute(null);
        await clean.ApplyTask!;
        Assert.True(File.Exists(Rejects("Tekrarlar", @"alt\a.pdf")));

        var org = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        Directory.CreateDirectory(D("arsiv"));
        org.Folders.Load(new RecentPair(new List<string> { D("klasor") }, D("arsiv"), DateTime.Now));
        await org.ScanCommand.ExecuteAsync(null);
        Assert.DoesNotContain(org.Review!.Plan.Items, i => i.File.Path.Contains("_Ayiklananlar"));
    }

    [Fact]
    public async Task Clean_RecentRemembered_Separately()
    {
        Put(@"klasor\a.pdf", Pdf("x"));
        var vm = Flow(new[] { D("klasor") }, new[] { D(@"klasor\onemli") });
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(_f.Settings.Stored.Recent);
        var rc = Assert.Single(_f.Settings.Stored.RecentClean);
        Assert.Equal(new[] { D("klasor") }, rc.Sources);
        Assert.Equal(new[] { D(@"klasor\onemli") }, rc.Priority);
        Assert.Equal("Neyi temizleyelim?", vm.Folders.Title);
    }

    [Fact]
    public void Clean_CanScan_WithoutArchive()
    {
        Directory.CreateDirectory(D("klasor"));
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored, FlowKind.Clean);
        Assert.False(vm.Folders.CanScan);
        _f.Folders.Next.Enqueue(D("klasor"));
        vm.Folders.AddSourceCommand.Execute(null);
        Assert.True(vm.Folders.CanScan);
        _f.Folders.Next.Enqueue(D(@"klasor\onemli"));
        vm.Folders.AddPriorityCommand.Execute(null);
        Assert.Single(vm.Folders.Priority);
        vm.Folders.RemovePriorityCommand.Execute(vm.Folders.Priority[0]);
        Assert.Empty(vm.Folders.Priority);
    }
}
