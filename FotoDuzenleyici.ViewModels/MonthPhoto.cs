using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Ay görünümünde bir karo. Bir JPG'nin RAW eşi <see cref="Raws"/>'ta onunla birlikte durur: JPG atılırsa RAW da gider,
/// <see cref="KeepRaw"/> seçiliyse RAW yerinde kalır.
/// </summary>
public sealed partial class MonthPhoto : ObservableObject, IThumbTarget
{
    public MonthPhoto(MediaFile file, IReadOnlyList<MediaFile> raws, bool otherMonth)
    {
        File = file;
        Raws = raws;
        OtherMonth = otherMonth;
        Taken = file.Taken ?? file.Modified;
    }

    public MediaFile File { get; }
    /// <summary>Bu fotoğrafla birlikte gidecek RAW eş(ler)i.</summary>
    public IReadOnlyList<MediaFile> Raws { get; }
    public DateTime Taken { get; }
    /// <summary>Çekim tarihi bu ayda değil (ya da hiç yok): "Başka aydan" grubunda.</summary>
    public bool OtherMonth { get; }
    public bool HasRaw => Raws.Count > 0;
    public bool IsVideo => File.Kind == FileKind.Video;

    [ObservableProperty] private bool marked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscardSize), nameof(RawLabel))]
    private bool keepRaw;

    [ObservableProperty] private ImageSource? thumb;
    [ObservableProperty] private bool thumbFailed;

    /// <summary>Izgaradaki gün başlığı (ViewModel kurar).</summary>
    public string DayHeader { get; internal set; } = "";

    /// <summary>Atılırsa boşalacak yer: dosya + tutulmayan RAW'lar.</summary>
    public long DiscardSize => File.Size + (KeepRaw ? 0 : Raws.Sum(r => r.Size));

    public string RawLabel => KeepRaw ? "RAW kalır" : "+RAW";

    public string Name => Path.GetFileName(File.Path);
    public string SizeText => Sizes.Text(File.Size);
    /// <summary>Ayıkla başlığı: "15 Ağustos 2026 · 10:42".</summary>
    public string TakenText => Taken.ToString("d MMMM yyyy · HH:mm", CultureInfo.CurrentCulture);
    /// <summary>Gözden geçir karosu: "15 Ağu".</summary>
    public string ShortDay => Taken.ToString("d MMM", CultureInfo.CurrentCulture);
}
