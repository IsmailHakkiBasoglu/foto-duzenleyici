using System.Windows.Media.Imaging;
using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.App.Shell;

/// <summary>
/// Ayıkla'nın büyük resmi: ekran genişliğinde, arka planda çözülür; son birkaç sonuç tutulur ki bir sonraki resim
/// önceden yüklenebilsin ve geri gelince yeniden çözülmesin. Çözülemeyen dosya (video, bozuk) null döner.
/// </summary>
sealed class LargeImage
{
    const int Keep = 4;
    readonly Dictionary<(string Path, int Width), Task<BitmapSource?>> _cache = new();
    readonly LinkedList<(string Path, int Width)> _order = new();

    public Task<BitmapSource?> Get(MediaFile f, int width)
    {
        width = Math.Clamp(width, 400, 4000);
        var key = (f.Path, width);
        if (_cache.TryGetValue(key, out var t))
        {
            _order.Remove(key); _order.AddFirst(key);
            return t;
        }
        t = f.Kind == FileKind.Video
            ? Task.FromResult<BitmapSource?>(null)
            : Task.Run(() => { try { return ImageHasher.Decode(f.Path, f.Orientation, width); } catch (Exception) { return null; } });
        _cache[key] = t;
        _order.AddFirst(key);
        while (_order.Count > Keep) { _cache.Remove(_order.Last!.Value); _order.RemoveLast(); }
        return t;
    }

    public void Clear() { _cache.Clear(); _order.Clear(); }
}
