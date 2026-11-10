using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Kurulum (Inno AppMutex) uygulamanın açık olduğunu bu adla anlar.</summary>
public class RunningMarkerTests
{
    [Fact]
    public void Hold_CreatesNamedMutex()
    {
        RunningMarker.Hold();

        Assert.True(Mutex.TryOpenExisting(RunningMarker.Name, out var m));
        m!.Dispose();
        Assert.Equal("FotoDuzenleyici.Calisiyor", RunningMarker.Name);
    }
}
