using System.Windows.Media;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Benzerler şeridinin küçük resimleri: arka planda en fazla <c>parallelism</c> dosya birden çözülür, son
/// <c>capacity</c> sonuç bellekte tutulur (en eski kullanılan atılır). Ekrandan çıkan satırın bekleyen isteği
/// <see cref="Cancel"/> ile düşer; hızlı kaydırmada boşa çözme olmaz. Sonuçlar oluşturulduğu iş parçacığına
/// (arayüz) gönderilir.
/// </summary>
public sealed class ThumbnailLoader
{
    readonly Func<MediaFile, ImageSource?> _decode;
    readonly int _capacity, _parallelism;
    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    readonly object _sync = new();

    // LRU: en son kullanılan listenin başında. Çözülemeyen dosya da (null) saklanır, tekrar denenmez.
    readonly Dictionary<MediaFile, LinkedListNode<(MediaFile File, ImageSource? Image)>> _cache = new();
    readonly LinkedList<(MediaFile File, ImageSource? Image)> _lru = new();

    readonly LinkedList<MediaFile> _queue = new();
    readonly Dictionary<MediaFile, List<IThumbTarget>> _waiting = new();
    int _workers;

    public ThumbnailLoader(Func<MediaFile, ImageSource?> decode, int capacity = 600, int parallelism = 4)
    {
        _decode = decode; _capacity = capacity; _parallelism = parallelism;
    }

    /// <summary>Fotoğraflar WIC ile; videolar Windows'un kendi küçük resmiyle (WIC video açamaz).</summary>
    public static ImageSource? DefaultDecode(MediaFile f) => f.Kind == FileKind.Video
        ? ShellThumbnail.Get(f.Path, 256)
        : ImageHasher.Decode(f.Path, f.Orientation, 256);

    public int CachedCount { get { lock (_sync) return _cache.Count; } }

    bool _paused;

    /// <summary>true iken istekler kuyrukta bekler, çözme başlamaz (zaman çubuğu sürüklenirken); false olunca sürer.</summary>
    public bool Paused
    {
        get { lock (_sync) return _paused; }
        set
        {
            lock (_sync)
            {
                _paused = value;
                if (!value) StartWorkers();
            }
        }
    }

    /// <summary>Kuyruk boş değilse en fazla <c>parallelism</c> çalışan. Kilit içinde çağrılır.</summary>
    void StartWorkers()
    {
        while (!_paused && _workers < _parallelism && _workers < _queue.Count) { _workers++; Task.Run(Work); }
    }

    public void Request(IThumbTarget row)
    {
        lock (_sync)
        {
            if (_cache.TryGetValue(row.File, out var node))
            {
                _lru.Remove(node); _lru.AddFirst(node);
                Deliver(row, node.Value.Image, immediate: true);
                return;
            }
            if (_waiting.TryGetValue(row.File, out var list)) { if (!list.Contains(row)) list.Add(row); return; }
            _waiting[row.File] = new List<IThumbTarget> { row };
            _queue.AddLast(row.File);
            StartWorkers();
        }
    }

    /// <summary>Satır artık görünmüyor; dosyayı bekleyen başka satır yoksa ve çözme başlamadıysa istek düşer.</summary>
    public void Cancel(IThumbTarget row)
    {
        lock (_sync)
        {
            if (!_waiting.TryGetValue(row.File, out var list)) return;
            list.Remove(row);
            if (list.Count == 0 && _queue.Remove(row.File)) _waiting.Remove(row.File);
        }
    }

    /// <summary>Bekleyen bütün istekleri düşürür (yeniden taramada). Çözülmekte olanın sonucu kimseye gitmez.</summary>
    public void Clear()
    {
        lock (_sync) { _queue.Clear(); _waiting.Clear(); }
    }

    void Work()
    {
        while (true)
        {
            MediaFile file;
            lock (_sync)
            {
                if (_queue.First is null || _paused) { _workers--; return; }
                file = _queue.First.Value;
                _queue.RemoveFirst();
            }

            ImageSource? img;
            try { img = _decode(file); } catch (Exception) { img = null; }

            List<IThumbTarget> rows;
            lock (_sync)
            {
                _cache[file] = _lru.AddFirst((file, img));
                while (_cache.Count > _capacity) { _cache.Remove(_lru.Last!.Value.File); _lru.RemoveLast(); }
                rows = _waiting.Remove(file, out var l) ? l : new();
            }
            foreach (var r in rows) Deliver(r, img, immediate: false);
        }
    }

    void Deliver(IThumbTarget row, ImageSource? img, bool immediate)
    {
        void Set() { row.Thumb = img; row.ThumbFailed = img is null; }
        if (immediate || _ui is null) Set();
        else _ui.Post(_ => Set(), null);
    }
}
