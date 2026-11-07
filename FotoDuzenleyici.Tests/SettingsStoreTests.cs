using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

public class SettingsStoreTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_settings_" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }
    string FilePath => Path.Combine(_tmp, "alt", "ayarlar.json");

    [Fact]
    public void Settings_RoundTrip()
    {
        var s = new AppSettings
        {
            SourceFolder = SourceFolderMode.SubFolder, RenameWithDate = true, AppendSourceToName = true,
            SeparateRawFolder = false, Theme = "Koyu", RecycleSource = false,
        };
        s.AddRecent(new[] { @"D:\a", @"E:\b" }, @"D:\Arsiv", new DateTime(2026, 11, 7, 10, 0, 0));
        var store = new JsonSettingsStore(FilePath);
        store.Save(s);

        var back = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(SourceFolderMode.SubFolder, back.SourceFolder);
        Assert.True(back.RenameWithDate);
        Assert.True(back.AppendSourceToName);
        Assert.False(back.SeparateRawFolder);
        Assert.Equal("Koyu", back.Theme);
        Assert.False(back.RecycleSource);
        var r = Assert.Single(back.Recent);
        Assert.Equal(new[] { @"D:\a", @"E:\b" }, r.Sources);
        Assert.Equal(@"D:\Arsiv", r.Archive);
        Assert.Equal(new DateTime(2026, 11, 7, 10, 0, 0), r.Used);
    }

    [Fact]
    public void Settings_Missing_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(FilePath);
        var s = store.Load();
        Assert.False(store.LoadFailed);
        Assert.Empty(s.Recent);
        Assert.True(s.RecycleSource);
        Assert.True(s.SeparateRawFolder);
        Assert.Equal("Sistem", s.Theme);
    }

    [Fact]
    public void Settings_Corrupt_ReturnsDefaults_AndFlags()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "{bozuk");
        var store = new JsonSettingsStore(FilePath);
        var s = store.Load();
        Assert.True(store.LoadFailed);
        Assert.Empty(s.Recent);
    }

    [Fact]
    public void AddRecent_KeepsFiveNewestFirst_AndDedupes()
    {
        var s = new AppSettings();
        var t = new DateTime(2026, 1, 1);
        for (int i = 0; i < 6; i++) s.AddRecent(new[] { $@"D:\k{i}" }, @"D:\Arsiv", t.AddDays(i));
        Assert.Equal(5, s.Recent.Count);
        Assert.Equal(@"D:\k5", s.Recent[0].Sources[0]);

        s.AddRecent(new[] { @"D:\x", @"D:\y" }, @"D:\Arsiv", t.AddDays(10));
        s.AddRecent(new[] { @"d:\Y\", @"D:\X" }, @"d:\arsiv", t.AddDays(11));
        Assert.Equal(5, s.Recent.Count);
        Assert.Single(s.Recent, r => r.Sources.Count == 2);
        Assert.Equal(t.AddDays(11), s.Recent[0].Used);
    }
}
