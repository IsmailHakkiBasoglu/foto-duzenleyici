namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Benzer / kusurlu kararlarını kaydeder: art arda tıklamalar 1 sn sonra tek yazmaya iner (klasikteki gibi).
/// Kayıt hatası işi durdurmaz; uyarı verilir, işaretler bu oturumda geçerli kalır.
/// </summary>
public sealed class ReviewSaver
{
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);
    public const string SaveFailed = "Benzer kararları kaydedilemedi; işaretler bu oturumda geçerli.";

    readonly Func<TimeSpan, Action, IDisposable> _schedule;
    readonly Action<string> _warn;
    IDisposable? _pending;

    public ReviewSaver(ReviewStore store, Func<TimeSpan, Action, IDisposable> schedule, Action<string> warn)
    {
        Store = store; _schedule = schedule; _warn = warn;
    }

    public ReviewStore Store { get; }

    public void Changed()
    {
        _pending?.Dispose();
        _pending = null;
        var handle = new Pending();
        _pending = handle;
        handle.Inner = _schedule(Delay, () => { if (_pending == handle) Save(); });
    }

    /// <summary>Bekleyen kayıt varsa hemen yazar.</summary>
    public void Flush()
    {
        if (_pending is null) return;
        Save();
    }

    void Save()
    {
        _pending?.Dispose();
        _pending = null;
        try { Store.Save(); }
        catch (Exception) { _warn(SaveFailed); }
    }

    sealed class Pending : IDisposable
    {
        public IDisposable? Inner;
        public void Dispose() => Inner?.Dispose();
    }
}
