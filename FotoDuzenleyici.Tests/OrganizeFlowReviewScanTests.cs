using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>2. parça: dört aşamalı tarama (metadata, kopyalar, benzerlik, netlik) ve durdurma.</summary>
public class OrganizeFlowReviewScanTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_flowrev_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    public OrganizeFlowReviewScanTests() => Directory.CreateDirectory(_tmp);
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

    /// <summary>Bir küçültülmüş kopya çifti ve tek başına karanlık bir kare.</summary>
    void Samples()
    {
        Put(@"kaynak\IMG_0001.JPG", TestImages.PatternJpeg(1200, 800, 5, 95));
        Put(@"kaynak\WhatsApp Image 2024-03-10.jpeg", TestImages.PatternJpeg(400, 267, 5, 60));
        Put(@"kaynak\gece.jpg", TestImages.DarkPatternJpeg(1600, 1200, 9, 0.08));
    }

    OrganizeFlowViewModel Flow()
    {
        var vm = new OrganizeFlowViewModel(_f.Services, _f.Settings.Stored);
        Directory.CreateDirectory(Path.Combine(_tmp, "arsiv"));
        vm.Folders.Load(new RecentPair(new List<string> { Path.Combine(_tmp, "kaynak") }, Path.Combine(_tmp, "arsiv"), DateTime.Now));
        return vm;
    }

    [Fact]
    public async Task Scan_FindsSimilarAndFlawed()
    {
        Samples();
        var vm = Flow();
        var phases = new List<string>();
        vm.OnPhaseForTest = p => { lock (phases) phases.Add(p); };

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(FlowStep.Review, vm.Step);
        var d = vm.Review!.Data;
        Assert.True(d.SimilarDone);
        Assert.True(d.QualityDone);
        var g = Assert.Single(d.Similar);
        Assert.Equal(SimilarKind.ResizedCopy, g.Kind);
        var f = Assert.Single(d.Flawed);
        Assert.EndsWith("gece.jpg", f.Rows[0].Path);
        Assert.Equal(new[] { "metadata", "similar", "quality" }, phases);
    }

    [Fact]
    public async Task Scan_OptionsOff_SkipsPhases3And4()
    {
        Samples();
        // Önceki bir oturumdan kalan karar: seçenekler kapalı taramada silinmemeli.
        var store = new ReviewStore(_f.Caches.Reviews);
        store.SetKeep("eski-anahtar", false);
        store.Save();
        _f.Settings.Stored.FindSimilar = false;
        _f.Settings.Stored.MarkFlawed = false;
        var vm = Flow();
        var phases = new List<string>();
        vm.OnPhaseForTest = p => { lock (phases) phases.Add(p); };

        await vm.ScanCommand.ExecuteAsync(null);

        var d = vm.Review!.Data;
        Assert.False(d.SimilarDone);
        Assert.False(d.QualityDone);
        Assert.Empty(d.Similar);
        Assert.Empty(d.Flawed);
        Assert.Equal(new[] { "metadata" }, phases);
        Assert.DoesNotContain(vm.Review.Plan.Items, i => i.Action is PlanAction.Similar or PlanAction.Flawed);
        Assert.False(new ReviewStore(_f.Caches.Reviews).GetKeep("eski-anahtar"));
    }

    [Fact]
    public async Task Scan_CancelInSimilar_OpensReviewWithNote()
    {
        Samples();
        var vm = Flow();
        vm.OnPhaseForTest = p => { if (p == "similar") vm.CancelCommand.Execute(null); };

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(FlowStep.Review, vm.Step);
        var d = vm.Review!.Data;
        Assert.Empty(d.Similar);
        Assert.False(d.SimilarDone);
        Assert.False(d.QualityDone);
        Assert.StartsWith("Benzerlik araması durduruldu", vm.ErrorMessage);
    }

    [Fact]
    public async Task Scan_CancelInQuality_KeepsSimilar()
    {
        Samples();
        var vm = Flow();
        vm.OnPhaseForTest = p => { if (p == "quality") vm.CancelCommand.Execute(null); };

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(FlowStep.Review, vm.Step);
        var d = vm.Review!.Data;
        Assert.True(d.SimilarDone);
        Assert.Single(d.Similar);
        Assert.False(d.QualityDone);
        Assert.Empty(d.Flawed);
        Assert.StartsWith("Netlik ölçümü durduruldu", vm.ErrorMessage);
    }

    [Fact]
    public async Task Scan_CorruptReviewFile_WarnsAndContinues()
    {
        Samples();
        Directory.CreateDirectory(Path.GetDirectoryName(_f.Caches.Reviews)!);
        File.WriteAllText(_f.Caches.Reviews, "{bozuk");
        var vm = Flow();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(FlowStep.Review, vm.Step);
        Assert.Single(vm.Review!.Data.Similar);
        Assert.Equal("Önceki benzer kararları okunamadı; boş başlandı.", vm.ErrorMessage);
    }
}
