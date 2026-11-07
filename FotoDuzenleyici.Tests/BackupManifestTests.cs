using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class BackupManifestTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_manifest_" + Guid.NewGuid().ToString("N"));

    public BackupManifestTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    [Fact]
    public void Load_Missing_IsEmpty()
    {
        var m = BackupManifest.Load(_tmp);
        Assert.Equal(0, m.Count);
        Assert.Empty(m.Entries);
    }

    [Fact]
    public void SaveLoad_RoundTrips_CaseInsensitive()
    {
        var m = BackupManifest.Load(_tmp);
        var e = new BackupEntry(@"a\B.jpg", 123, new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), "abc",
            new DateTime(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc));
        m.Set(e);
        m.Save();

        var again = BackupManifest.Load(_tmp);
        Assert.Equal(1, again.Count);
        Assert.True(again.TryGet(@"A\b.JPG", out var got));
        Assert.Equal(e, got);
    }

    [Fact]
    public void Load_CorruptJson_Throws()
    {
        File.WriteAllText(Path.Combine(_tmp, BackupManifest.FileName), "{bozuk");
        Assert.Throws<InvalidDataException>(() => BackupManifest.Load(_tmp));
    }

    [Fact]
    public void Save_LeavesNoTmp()
    {
        var m = BackupManifest.Load(_tmp);
        m.Set(new BackupEntry("x.jpg", 1, DateTime.UtcNow, "h", DateTime.UtcNow));
        m.Save();
        Assert.True(File.Exists(Path.Combine(_tmp, BackupManifest.FileName)));
        Assert.Empty(Directory.GetFiles(_tmp, "*.tmp"));
    }
}
