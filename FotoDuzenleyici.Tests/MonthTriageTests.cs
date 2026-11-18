using System.Globalization;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Ay görünümünde işaretleme, Ayıkla, Gözden geçir ve atma.</summary>
public class MonthTriageTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_mtr_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();
    readonly Dictionary<string, DateTime> _dates = new(StringComparer.OrdinalIgnoreCase);

    public MonthTriageTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        try { Directory.Delete(_root + "_log", true); } catch { }
    }

    string Month => Path.Combine(_root, "2026-08");

    string Put(string rel, int size, int minute = 0)
    {
        var p = Path.Combine(Month, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        _dates[p] = new DateTime(2026, 8, 15, 10, minute, 0);
        return p;
    }

    MediaFile Read(string p) => new()
    {
        Path = p, Size = new FileInfo(p).Length, Kind = FileClassifier.Classify(p),
        Taken = _dates[p], DateSource = DateSource.Exif,
    };

    async Task<MonthViewModel> Loaded()
    {
        var vm = new MonthViewModel(_f.Services, _f.Settings.Stored, _root, TimelineMethod.FolderNames, 2026, 8,
            new[] { Month }, KindFilter.Photo, Read, _root + "_log");
        await vm.LoadAsync();
        return vm;
    }

    /// <summary>a.jpg (1000) + a.cr3 (5000), b.jpg (2000), c.jpg (3000).</summary>
    async Task<MonthViewModel> Three()
    {
        Put("a.jpg", 1000, 0);
        Put("a.cr3", 5000, 0);
        Put("b.jpg", 2000, 1);
        Put("c.jpg", 3000, 2);
        return await Loaded();
    }

    static MonthPhoto P(MonthViewModel vm, string name) => vm.Photos.Single(p => Path.GetFileName(p.File.Path) == name);

    [Fact]
    public async Task ToggleMark_UpdatesCountAndText()
    {
        var vm = await Three();

        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));

        Assert.True(P(vm, "b.jpg").Marked);
        Assert.Equal(1, vm.MarkedCount);
        Assert.Equal(2000, vm.MarkedSize);
        Assert.True(vm.HasMarked);
        Assert.Equal("🗑 Atılacaklar: 1 · " + Sizes.Text(2000), vm.MarkedText);

        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        Assert.Equal(0, vm.MarkedCount);
        Assert.False(vm.HasMarked);
    }

    [Fact]
    public async Task MarkedJpg_TakesRawAlong()
    {
        var vm = await Three();

        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        Assert.Equal(6000, vm.MarkedSize);
        await vm.DiscardCommand.ExecuteAsync(null); // Gözden geçir açılmadan atılmaz
        Assert.True(File.Exists(Path.Combine(Month, "a.jpg")));

        vm.OpenReviewCommand.Execute(null);
        Assert.Equal("2 dosya · " + Sizes.Text(6000), vm.ReviewTotal);
        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(Month, "a.jpg")));
        Assert.False(File.Exists(Path.Combine(Month, "a.cr3")));
    }

    [Fact]
    public async Task KeepRaw_RawNotDiscarded_NotInSize()
    {
        var vm = await Three();
        var a = P(vm, "a.jpg");
        vm.StartTriageCommand.Execute(a);
        vm.ToggleKeepRawCommand.Execute(null); // Ayıkla'da açık olan fotoğraf
        vm.MarkAndNextCommand.Execute(null);

        Assert.True(a.KeepRaw);
        Assert.Equal("RAW kalır", a.RawLabel);
        Assert.Equal(1000, vm.MarkedSize);
        vm.OpenReviewCommand.Execute(null);
        Assert.Equal("1 dosya · " + Sizes.Text(1000), vm.ReviewTotal);
        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.False(File.Exists(Path.Combine(Month, "a.jpg")));
        Assert.True(File.Exists(Path.Combine(Month, "a.cr3")));
    }

    [Fact]
    public async Task MarkAndNext_TogglesAndAdvances()
    {
        var vm = await Three();
        vm.StartTriageCommand.Execute(null);
        Assert.True(vm.IsTriage);
        Assert.Equal("a.jpg", vm.Current!.Name);
        Assert.Equal("1 / 3", vm.Position);

        vm.MarkAndNextCommand.Execute(null);

        Assert.True(P(vm, "a.jpg").Marked);
        Assert.Equal("b.jpg", vm.Current!.Name);
        Assert.Equal("2 / 3", vm.Position);

        vm.PreviousCommand.Execute(null);
        vm.MarkAndNextCommand.Execute(null);
        Assert.False(P(vm, "a.jpg").Marked);
    }

    [Fact]
    public async Task Next_AtEnd_Stays()
    {
        var vm = await Three();
        vm.StartTriageCommand.Execute(P(vm, "c.jpg"));

        vm.NextCommand.Execute(null);
        Assert.Equal("c.jpg", vm.Current!.Name);

        vm.StartTriageCommand.Execute(P(vm, "a.jpg"));
        vm.PreviousCommand.Execute(null);
        Assert.Equal("a.jpg", vm.Current!.Name);
    }

    [Fact]
    public async Task DiscardAndNext_Marks()
    {
        var vm = await Three();
        vm.StartTriageCommand.Execute(null);

        vm.DiscardAndNextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        vm.DiscardAndNextCommand.Execute(null); // ikinci kez de işaretli kalır

        Assert.True(P(vm, "a.jpg").Marked);
        Assert.Equal("b.jpg", vm.Current!.Name);
    }

    [Fact]
    public async Task KeepAndNext_Unmarks()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.StartTriageCommand.Execute(null);

        vm.KeepAndNextCommand.Execute(null);

        Assert.False(P(vm, "a.jpg").Marked);
        Assert.Equal("b.jpg", vm.Current!.Name);
    }

    [Fact]
    public async Task CloseTriage_LeavesTriage()
    {
        var vm = await Three();
        vm.StartTriageCommand.Execute(null);

        vm.CloseTriageCommand.Execute(null);

        Assert.False(vm.IsTriage);
    }

    [Fact]
    public async Task Review_ButtonText_AllRecycle()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));

        vm.OpenReviewCommand.Execute(null);

        Assert.True(vm.IsReview);
        Assert.Equal(2, vm.ReviewLines.Count);
        Assert.Equal("3 dosyayı Geri Dönüşüm Kutusu'na at", vm.DiscardButtonText);
        Assert.False(vm.HasRejectRoute);
    }

    [Fact]
    public async Task Review_ButtonText_Mixed()
    {
        var vm = await Three();
        _f.Bin.Can = (_, size) => size < 4000; // RAW kutuya sığmıyor
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));

        vm.OpenReviewCommand.Execute(null);

        Assert.Equal("2 dosyayı Geri Dönüşüm Kutusu'na at, 1 dosyayı _Ayiklananlar'a taşı", vm.DiscardButtonText);
        Assert.True(vm.HasRejectRoute);
        var a = vm.ReviewLines.Single(l => l.Photo.Name == "a.jpg");
        Assert.Equal((DiscardRoute.RecycleBin, true), (a.Route, a.RawToRejects));
    }

    [Fact]
    public async Task Review_NoBin_HasRejectRoute()
    {
        var vm = await Three();
        _f.Bin.Can = (_, _) => false;
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));

        vm.OpenReviewCommand.Execute(null);

        Assert.Equal("1 dosyayı _Ayiklananlar'a taşı", vm.DiscardButtonText);
        Assert.True(vm.HasRejectRoute);
        Assert.Equal(DiscardRoute.MoveToRejects, Assert.Single(vm.ReviewLines).Route);
    }

    [Fact]
    public async Task Review_ToggleKeepRaw_Replans()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.OpenReviewCommand.Execute(null);
        Assert.Equal("2 dosyayı Geri Dönüşüm Kutusu'na at", vm.DiscardButtonText);

        vm.ToggleKeepRawCommand.Execute(P(vm, "a.jpg"));

        Assert.Equal("1 dosyayı Geri Dönüşüm Kutusu'na at", vm.DiscardButtonText);
        Assert.Equal("1 dosya · " + Sizes.Text(1000), vm.ReviewTotal);
    }

    [Fact]
    public async Task Unmark_InReview_RemovesLine()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        vm.OpenReviewCommand.Execute(null);

        vm.UnmarkCommand.Execute(P(vm, "b.jpg"));

        Assert.Single(vm.ReviewLines);
        Assert.False(P(vm, "b.jpg").Marked);
        Assert.Equal(1, vm.MarkedCount);
    }

    [Fact]
    public async Task Unmark_LastLine_ClosesReview()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        vm.OpenReviewCommand.Execute(null);

        vm.UnmarkCommand.Execute(P(vm, "b.jpg"));

        Assert.False(vm.IsReview);
    }

    [Fact]
    public async Task CancelReview_KeepsMarks()
    {
        var vm = await Three();
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        vm.OpenReviewCommand.Execute(null);

        vm.CancelReviewCommand.Execute(null);

        Assert.False(vm.IsReview);
        Assert.Equal(1, vm.MarkedCount);
        Assert.True(File.Exists(Path.Combine(Month, "b.jpg")));
    }

    [Fact]
    public async Task Discard_RemovesDonePhotos_KeepsFailedMarked_Message()
    {
        var vm = await Three();
        _f.Bin.SendFunc = p => { if (p.EndsWith("c.jpg")) return false; File.Delete(p); return true; };
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        vm.ToggleMarkCommand.Execute(P(vm, "c.jpg"));
        vm.OpenReviewCommand.Execute(null);

        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.Equal("1 dosya atıldı, 1 dosya atılamadı", vm.ResultMessage);
        Assert.False(vm.IsReview);
        Assert.False(vm.IsDiscarding);
        Assert.DoesNotContain(vm.Photos, p => p.Name == "b.jpg");
        Assert.True(P(vm, "c.jpg").Marked);
        Assert.Equal(1, vm.MarkedCount);
        Assert.Equal(2, vm.Photos.Count);
        Assert.Equal("15 Ağustos Cumartesi · 2", Assert.Single(vm.Days).Header);
        var d = Assert.Single(vm.Discarded);
        Assert.Equal(("b.jpg", false), (Path.GetFileName(d.File.Path), d.Moved));
    }

    [Fact]
    public async Task Discard_AllDone_MessageWithoutFailures_RawCountedInDiscarded()
    {
        var vm = await Three();
        _f.Bin.Can = (_, size) => size < 4000;
        vm.ToggleMarkCommand.Execute(P(vm, "a.jpg"));
        vm.OpenReviewCommand.Execute(null);

        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.Equal("2 dosya atıldı", vm.ResultMessage);
        Assert.Equal(2, vm.Discarded.Count);
        Assert.Contains(vm.Discarded, d => d.File.Path.EndsWith("a.cr3") && d.Moved);
        Assert.True(File.Exists(Path.Combine(_root, "_Ayiklananlar", "Ayikla", "2026-08", "a.cr3")));
        Assert.False(vm.HasMarked);
    }

    [Fact]
    public async Task Discard_TriageCurrentRemoved_CurrentMovesOn()
    {
        var vm = await Three();
        vm.StartTriageCommand.Execute(P(vm, "b.jpg"));
        vm.MarkAndNextCommand.Execute(null);
        vm.PreviousCommand.Execute(null);
        vm.OpenReviewCommand.Execute(null);

        await vm.DiscardCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Current);
        Assert.Contains(vm.Current, vm.Photos);
    }

    [Fact]
    public async Task Back_WithMarks_AsksAndStays()
    {
        var vm = await Three();
        int backs = 0;
        vm.BackRequested += () => backs++;
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        _f.Dialogs.Answer = false;

        vm.BackCommand.Execute(null);

        Assert.Equal(0, backs);
        Assert.Contains("1 dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?", Assert.Single(_f.Dialogs.Asked));
        Assert.Equal("1 dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?", vm.LeaveQuestion);
    }

    [Fact]
    public async Task Back_NoMarks_RaisesBackRequested()
    {
        var vm = await Three();
        int backs = 0;
        vm.BackRequested += () => backs++;

        vm.BackCommand.Execute(null);

        Assert.Equal(1, backs);
        Assert.Empty(_f.Dialogs.Asked);
        Assert.Null(vm.LeaveQuestion);
    }

    [Fact]
    public async Task Back_WhileDiscarding_Ignored()
    {
        var vm = await Three();
        int backs = 0;
        vm.BackRequested += () => backs++;
        using var gate = new ManualResetEventSlim();
        _f.Bin.SendFunc = p => { gate.Wait(TimeSpan.FromSeconds(5)); File.Delete(p); return true; };
        vm.ToggleMarkCommand.Execute(P(vm, "b.jpg"));
        vm.OpenReviewCommand.Execute(null);

        var run = vm.DiscardCommand.ExecuteAsync(null);
        Assert.True(vm.IsDiscarding);
        Assert.NotNull(vm.DiscardTask);
        vm.BackCommand.Execute(null);
        gate.Set();
        await run;

        Assert.Equal(0, backs);
    }
}
