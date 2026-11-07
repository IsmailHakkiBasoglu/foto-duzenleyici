using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.Tests;

/// <summary>Elle çalıştırılan zamanlayıcı: testte "1 sn geçti" demek için.</summary>
sealed class ManualScheduler
{
    readonly List<(Action Work, Handle H)> _pending = new();
    public int Scheduled;
    public Func<TimeSpan, Action, IDisposable> Fn => (_, w) => { Scheduled++; var h = new Handle(); _pending.Add((w, h)); return h; };
    public void RunAll() { foreach (var (w, h) in _pending.ToList()) if (!h.Cancelled) w(); _pending.Clear(); }
    public sealed class Handle : IDisposable { public bool Cancelled; public void Dispose() => Cancelled = true; }
}

public class ReviewSaverTests : IDisposable
{
    readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd_saver_" + Guid.NewGuid().ToString("N"));
    readonly List<string> _warnings = new();
    public ReviewSaverTests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { } }

    string StorePath => Path.Combine(_tmp, "k.json");

    [Fact]
    public void Saver_Debounces_OneSave()
    {
        var clock = new ManualScheduler();
        var store = new ReviewStore(StorePath);
        var saver = new ReviewSaver(store, clock.Fn, _warnings.Add);
        store.SetKeep("a", false); saver.Changed();
        store.SetKeep("b", false); saver.Changed();
        Assert.False(File.Exists(StorePath));
        clock.RunAll();
        Assert.True(File.Exists(StorePath));
        Assert.Equal(false, new ReviewStore(StorePath).GetKeep("b"));
        Assert.Equal(2, clock.Scheduled);
    }

    [Fact]
    public void Saver_Flush_SavesPending()
    {
        var clock = new ManualScheduler();
        var store = new ReviewStore(StorePath);
        var saver = new ReviewSaver(store, clock.Fn, _warnings.Add);
        store.SetKeep("a", false); saver.Changed();
        saver.Flush();
        Assert.Equal(false, new ReviewStore(StorePath).GetKeep("a"));
        File.Delete(StorePath);
        clock.RunAll(); // iptal edildi: ikinci kez yazmaz
        Assert.False(File.Exists(StorePath));
        saver.Flush(); // bekleyen yok
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void Saver_SaveFails_WarnsAndContinues()
    {
        Directory.CreateDirectory(StorePath); // aynı adlı klasör: dosya yazılamaz
        var saver = new ReviewSaver(new ReviewStore(StorePath), (_, w) => { w(); return new ManualScheduler.Handle(); }, _warnings.Add);
        saver.Changed();
        Assert.Equal(new[] { "Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli." }, _warnings);
    }
}
