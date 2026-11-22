using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Oturum kataloğu: uygulama açık kaldıkça son okunan kökün kataloğu. Kapatınca gider; dosya taşıyan işler
/// (Düzenle, Temizle, Geri Al) başlayınca bırakılır, zaman çizelgesine dönünce yeniden okunur.
/// </summary>
public sealed class CatalogStore
{
    MediaCatalog? _current;

    /// <summary>Kök (büyük/küçük harf, sondaki ayıraç önemsiz) ve yöntem eşleşirse katalog; yoksa null.</summary>
    public MediaCatalog? Get(string root, TimelineMethod method) =>
        _current is { } c && c.Method == method && Same(c.Root, root) ? c : null;

    /// <summary>
    /// Öncekini bırakır. Yarıda kesilmiş ya da hiç okunamamış (kök yok, sürücü çıkarılmış: hiç öğe yok ve hata var)
    /// katalog saklanmaz; sürücü takılınca yeniden okunsun.
    /// </summary>
    public void Set(MediaCatalog c) => _current = c.Cancelled || (c.Items.Count == 0 && c.Errors.Count > 0) ? null : c;

    public void Invalidate() => _current = null;

    static bool Same(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    static string Norm(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
}
