using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class DuplicatesReviewTests
{
    readonly Fakes _f = new();
    static readonly PlanOptions Opt = new() { DestinationRoot = @"D:\Arsiv" };

    static MediaFile F(string path, long size, int day) => new()
    {
        Path = path, Size = size, Kind = FileKind.Photo, Created = new DateTime(2024, 1, day),
    };

    static List<DuplicateGroupState> Groups()
    {
        var g1 = new DuplicateGroup { Keeper = F(@"E:\a\IMG_1.jpg", 2 * 1048576, 1), Copies = new() { F(@"E:\b\IMG_1.jpg", 2 * 1048576, 2) } };
        var g2 = new DuplicateGroup { Keeper = F(@"E:\a\IMG_2.jpg", 1048576, 1), Copies = new() { F(@"E:\c\IMG_2.jpg", 1048576, 3) } };
        return DuplicateGroupState.Build(new[] { g1, g2 }, Opt);
    }

    [Fact]
    public void Counter_NoGroups()
    {
        var vm = new DuplicatesReviewViewModel(new List<DuplicateGroupState>(), _f.Services, () => { });
        Assert.Equal("Birebir kopya yok.", vm.CounterText);
    }

    [Fact]
    public void Duplicates_ChooseRow_CounterAndChanged()
    {
        int changed = 0;
        var vm = new DuplicatesReviewViewModel(Groups(), _f.Services, () => changed++);
        Assert.StartsWith("2 grup · 2 kopya · ", vm.CounterText);
        Assert.EndsWith(" GB kazanılacak", vm.CounterText);

        vm.Groups[0].Rows.Single(r => !r.IsKeeper).IsKeeper = true;

        Assert.Equal(1, changed);
        Assert.EndsWith(" GB kazanılacak · 1 grupta elle seçim", vm.CounterText);
    }

    [Fact]
    public void Duplicates_SecondManualChoice_StillChanged()
    {
        var g = new DuplicateGroup { Keeper = F(@"E:aIMG_1.jpg", 10, 1), Copies = new() { F(@"E:IMG_1.jpg", 10, 2), F(@"E:cIMG_1.jpg", 10, 3) } };
        int changed = 0;
        var vm = new DuplicatesReviewViewModel(DuplicateGroupState.Build(new[] { g }, Opt), _f.Services, () => changed++);
        vm.Groups[0].Rows[1].IsKeeper = true;
        var before = changed;
        vm.Groups[0].Rows[2].IsKeeper = true;
        Assert.True(changed > before);
    }

    [Fact]
    public void Duplicates_Reset_ClearsManual()
    {
        int changed = 0;
        var vm = new DuplicatesReviewViewModel(Groups(), _f.Services, () => changed++);
        var other = vm.Groups[0].Rows.Single(r => !r.IsKeeper);
        other.IsKeeper = true;

        vm.ResetCommand.Execute(null);

        Assert.False(vm.Groups[0].UserChosen);
        Assert.False(other.IsKeeper);
        Assert.DoesNotContain("elle seçim", vm.CounterText);
        Assert.True(changed >= 2);
    }

    [Fact]
    public void Duplicates_OpenFile_UsesShell()
    {
        var vm = new DuplicatesReviewViewModel(Groups(), _f.Services, () => { });
        vm.OpenFileCommand.Execute(vm.Groups[1].Rows[1]);
        Assert.Equal(new[] { vm.Groups[1].Rows[1].File.Path }, _f.Shell.Files);
    }
}
