using System.Windows;
using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.App.Shell;

/// <summary>
/// İnceleme kartlarının küçük resimleri: yalnız ekranda görünen kart için istenir, kart kaldırılınca iptal edilir
/// (klasik penceredeki mantık). Kartın satırı Tag'de tutulur: kart kaldırılırken DataContext çoktan "bağlantısız"
/// olabilir, iptal yine doğru satıra gitsin. Sanallaştırmada geri dönüştürülen kartın satırı değişince eskisi iptal,
/// yenisi istenir. Bütün bölümler tek yükleyiciyi paylaşır (arayüz iş parçacığında kurulur).
/// </summary>
static class ThumbTracker
{
    static ThumbnailLoader? _loader;
    static ThumbnailLoader Loader => _loader ??= new ThumbnailLoader(ThumbnailLoader.DefaultDecode);

    /// <summary>Kartın Loaded olayından çağrılır; kartın DataContext'i bir <see cref="SimilarRow"/> olmalı.</summary>
    public static void Attach(FrameworkElement card)
    {
        if (card.Tag is null)
        {
            card.Unloaded += (_, _) => Track(card, null);
            card.DataContextChanged += (_, e) => { if (card.IsLoaded) Track(card, e.NewValue as SimilarRow); };
        }
        Track(card, card.DataContext as SimilarRow);
    }

    static void Track(FrameworkElement card, SimilarRow? row)
    {
        if (card.Tag is SimilarRow old && old != row) Loader.Cancel(old);
        card.Tag = row ?? (object)false; // false: olaylar bağlandı ama satır yok
        if (row is not null) Loader.Request(row);
    }

    /// <summary>Yeniden taramada: önbellek ve bekleyen istekler bırakılır.</summary>
    public static void Clear() => _loader?.Clear();
}
