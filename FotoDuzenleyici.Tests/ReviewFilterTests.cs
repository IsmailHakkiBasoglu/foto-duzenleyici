using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>2. parça: benzer / kusurlu ayarları, özet sayıları ve plan süzgeci.</summary>
public class ReviewFilterTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_rf_" + Guid.NewGuid().ToString("N"));
    public ReviewFilterTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    static PlanItem Item(PlanAction a, long size = 10) => new()
    {
        File = new MediaFile { Path = @"C:\k\" + Guid.NewGuid().ToString("N") + ".jpg", Size = size, Kind = FileKind.Photo },
        Action = a, TargetDir = @"D:\Arsiv\x", TargetName = "x.jpg",
    };

    [Fact]
    public void Settings_NewFlags_DefaultTrue_AndRoundTrip()
    {
        var s = new AppSettings();
        Assert.True(s.FindSimilar);
        Assert.True(s.MarkFlawed);
        s.FindSimilar = false; s.MarkFlawed = false;
        var path = Path.Combine(_tmp, "ayarlar.json");
        new JsonSettingsStore(path).Save(s);
        var back = new JsonSettingsStore(path).Load();
        Assert.False(back.FindSimilar);
        Assert.False(back.MarkFlawed);
    }

    [Fact]
    public void Summary_CountsSimilarAndFlawed()
    {
        var plan = new Plan();
        plan.Items.Add(Item(PlanAction.Move));
        plan.Items.Add(Item(PlanAction.Similar));
        plan.Items.Add(Item(PlanAction.Similar));
        plan.Items.Add(Item(PlanAction.Flawed));
        var s = PlanSummary.From(plan, new PlanOptions { DestinationRoot = @"D:\Arsiv" });
        Assert.Equal(2, s.SimilarCount);
        Assert.Equal(1, s.FlawedCount);
        Assert.Equal(4, s.ApplyCount);
    }

    [Fact]
    public void Filter_SimilarAndFlawed()
    {
        var plan = new Plan();
        var sim = Item(PlanAction.Similar); var fl = Item(PlanAction.Flawed);
        plan.Items.Add(Item(PlanAction.Move)); plan.Items.Add(sim); plan.Items.Add(fl);
        var session = new OrganizeSession(Array.Empty<string>(), new PlanOptions { DestinationRoot = _tmp });
        var vm = new ReviewStepViewModel(new OrganizeJob(session), plan, new Fakes().Services, ReviewData.Empty(new ReviewStore(Path.Combine(_tmp, "k.json"))));
        vm.Filter = PlanFilter.Similar;
        Assert.Equal(new[] { sim }, vm.Rows);
        vm.Filter = PlanFilter.Flawed;
        Assert.Equal(new[] { fl }, vm.Rows);
    }
}

public class OptionsSummaryScanTests
{
    [Fact]
    public void OptionsSummary_ShowsScanOptions()
    {
        var f = new Fakes();
        var vm = new FoldersStepViewModel(f.Services, f.Settings.Stored);
        Assert.Contains("Benzerleri ara", vm.OptionsSummary);
        Assert.Contains("Bulanıkları işaretle", vm.OptionsSummary);
        f.Settings.Stored.FindSimilar = false; f.Settings.Stored.MarkFlawed = false;
        Assert.DoesNotContain("Benzerleri ara", vm.OptionsSummary);
        Assert.DoesNotContain("Bulanıkları işaretle", vm.OptionsSummary);
    }
}
