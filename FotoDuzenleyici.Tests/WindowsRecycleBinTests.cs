using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Gerçek kutuya dokunmayan kurallar: kalıcı silme her durumda engellenir.</summary>
public class WindowsRecycleBinTests
{
    const uint RecycleIfPossible = 0x80; // TSF_DELETE_RECYCLE_IF_POSSIBLE

    [Fact]
    public void PreDelete_RecycleFlag_Allowed()
    {
        Assert.True(WindowsRecycleBin.IsRecycle(RecycleIfPossible));
        Assert.True(WindowsRecycleBin.IsRecycle(RecycleIfPossible | 0x1));
    }

    [Fact]
    public void PreDelete_WithoutRecycleFlag_Vetoed()
    {
        Assert.False(WindowsRecycleBin.IsRecycle(0));
        Assert.False(WindowsRecycleBin.IsRecycle(0x1));
    }

    [Fact]
    public void CanRecycle_UncPath_False()
    {
        Assert.False(WindowsRecycleBin.Instance.CanRecycle(@"\\sunucu\paylasim\a.jpg", 1));
    }

    [Fact]
    public void Send_MissingFile_False_NoThrow()
    {
        var p = Path.Combine(Path.GetTempPath(), "fd_yok_" + Guid.NewGuid().ToString("N") + ".jpg");
        Assert.False(WindowsRecycleBin.Instance.Send(p));
    }
}
