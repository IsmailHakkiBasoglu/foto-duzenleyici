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
    readonly Dictionary<MediaFile, List<SimilarRow>> _waiting = new();
    int _workers;

    public ThumbnailLoader(Func<MediaFile, ImageSource?> decode, int capacity = 600, int parallelism = 4)
    {
        _decode = decode; _capacity = capacity; _parallelism = parallelism;
    }

    public static ImageSource? DefaultDecode(MediaFile f) => ImageHasher.Decode(f.Path, f.Orientation, 256);

    public int CachedCount { get { lock (_sync) return _cache.Count; } }

    public void Request(SimilarRow row)
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
            _waiting[row.File] = new List<SimilarRow> { row };
            _queue.AddLast(row.File);
            if (_workers < _parallelism) { _workers++; Task.Run(Work); }
        }
    }

    /// <summary>Satır artık görünmüyor; dosyayı bekleyen başka satır yoksa ve çözme başlamadıysa istek düşer.</summary>
    public void Cancel(SimilarRow row)
    {
        lock (_sync)
        {
            if (!_waiting.TryGetValue(row.File, out var list)) return;
            list.Remove(row);
            if (list.Count == 0 && _queue.Remove(row.File)) _waiting.Remove(row.File);
        }
    }

    void Work()
    {
        while (true)
        {
            MediaFile file;
            lock (_sync)
            {
                if (_queue.First is null) { _workers--; return; }
                file = _queue.First.Value;
                _queue.RemoveFirst();
            }

            ImageSource? img;
            try { img = _decode(file); } catch (Exception) { img = null; }

            List<SimilarRow> rows;
            lock (_sync)
            {
                _cache[file] = _lru.AddFirst((file, img));
                while (_cache.Count > _capacity) { _cache.Remove(_lru.Last!.Value.File); _lru.RemoveLast(); }
                rows = _waiting.Remove(file, out var l) ? l : new();
            }
            foreach (var r in rows) Deliver(r, img, immediate: false);
        }
    }

    void Deliver(SimilarRow row, ImageSource? img, bool immediate)
    {
        void Set() { row.Thumb = img; row.ThumbFailed = img is null; }
        if (immediate || _ui is null) Set();
        else _ui.Post(_ => Set(), null);
    }
}
