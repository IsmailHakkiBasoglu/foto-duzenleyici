using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Galeride bir ay. <paramref name="FileCount"/>: tablodaki sayıyla aynı (süzgece göre RAW eşleri de); null → karo sayısı.
/// </summary>
public sealed record GalleryMonth(int Year, int Month, IReadOnlyList<MonthPhoto> Photos, int? FileCount = null)
{
    public int Files => FileCount ?? Photos.Count;
    public string ShortTitle => $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Month)} {Year}";
    public string Title => $"{ShortTitle} · {Files:N0}";
}

/// <summary>
/// Sürekli galeri: kataloğun bütün ayları yeniden eskiye, ay içinde fotoğraflar yeniden eskiye (klasör adlarında
/// tarihi başka aya düşenler ayın sonunda). Salt okunur; ayıklama ay görünümünde.
/// </summary>
public sealed partial class GalleryViewModel : ObservableObject
{
    readonly Services _s;

    GalleryViewModel(Services s, IReadOnlyList<GalleryMonth> months)
    {
        _s = s;
        Months = months;
    }

    public IReadOnlyList<GalleryMonth> Months { get; }
    public bool IsEmpty => Months.Count == 0;

    /// <summary>Ay başlığındaki "Ayıkla ▶": zaman çizelgesi o ayın ay görünümünü açar.</summary>
    public event Action<int, int>? MonthRequested;

    public static GalleryViewModel Build(MediaCatalog c, KindFilter filter, Services s)
    {
        var months = c.Items
            .GroupBy(i => (i.Year, i.Month))
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var photos = MonthItems.Build(g.Select(i => i.File).ToList(), filter, g.Key.Year, g.Key.Month);
                var ordered = photos.Where(p => !p.OtherMonth).OrderByDescending(p => p.Taken).ThenBy(p => p.File.Path, StringComparer.OrdinalIgnoreCase)
                    .Concat(photos.Where(p => p.OtherMonth).OrderByDescending(p => p.Taken).ThenBy(p => p.File.Path, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                int files = ordered.Count + (filter == KindFilter.All ? ordered.Sum(p => p.Raws.Count) : 0);
                return new GalleryMonth(g.Key.Year, g.Key.Month, ordered, files);
            })
            .Where(m => m.Photos.Count > 0)
            .ToList();
        return new GalleryViewModel(s, months);
    }

    [RelayCommand]
    void OpenMonth(GalleryMonth? m)
    {
        if (m is not null) MonthRequested?.Invoke(m.Year, m.Month);
    }

    [RelayCommand]
    void OpenFile(MonthPhoto? p)
    {
        if (p is not null) _s.Shell.OpenFile(p.File.Path);
    }
}
