using System.ComponentModel;
using System.Windows.Media;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Büyük çözmeleri sınırlar: aynı anda en fazla <c>limit</c> tane. Başlamadan iptal edilen iş hiç çalışmaz.
/// 24 MP bir fotoğrafın tam çözümü ~96 MB tutar; 14 karelik grupta hepsi birden belleği doldurur.
/// </summary>
public sealed class DecodeLimiter
{
    readonly SemaphoreSlim _slots;
    public DecodeLimiter(int limit) => _slots = new SemaphoreSlim(limit, limit);

    /// <summary>Sıra gelince <paramref name="decode"/>'u arka planda çalıştırır; iptal edildiyse null.</summary>
    public async Task<ImageSource?> RunAsync(Func<ImageSource?> decode, CancellationToken ct)
    {
        try { await _slots.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            if (ct.IsCancellationRequested) return null;
            return await Task.Run(decode).ConfigureAwait(false);
        }
        finally { _slots.Release(); }
    }
}

/// <summary>Karşılaştırma penceresinde bir kare: önce ekran boyutunda, yakınlaşınca tam çözünürlükte.</summary>
public sealed class CompareTile : INotifyPropertyChanged
{
    /// <summary>Tam çözünürlük (<c>decodePixelWidth = 0</c>) için kullanılan genişlik değeri.</summary>
    public const int FullWidth = int.MaxValue;

    /// <summary>Çok kareli gruplarda yakınlaşma çözümü bu genişlikle sınırlanır (~3 kat yakınlaşmaya kadar net).</summary>
    public const int LargeGroupWidth = 3200;
    const int LargeGroupFrames = 4;

    /// <summary>
    /// Yakınlaşınca kullanılacak çözme genişliği; 0 = tam çözünürlük. 24 MP kare tam çözümde ~160 MB tutar:
    /// 4 kareye kadar tam, daha çoğunda <see cref="LargeGroupWidth"/> (14 kare: ~4,5 GB yerine ~0,4 GB).
    /// </summary>
    public static int FullDecodeWidth(int frameCount, int? fileWidth) =>
        frameCount <= LargeGroupFrames || fileWidth is { } w && w <= LargeGroupWidth ? 0 : LargeGroupWidth;

    ImageSource? _image;
    bool _failed;
    int _loadedWidth;

    public required SimilarRow Row { get; init; }
    public required int Number { get; init; }
    public bool FullRequested { get; set; }
    public string Caption => $"{Number}  {Row.Name}  ·  {Row.Resolution}  ·  {Row.SizeMb} MB  ·  {Row.Taken}";
    public ImageSource? Image => _image;
    public bool Failed => _failed;

    /// <summary>
    /// Çözme sonucunu önerir. Gösterilenden küçük çözünürlüklü sonuç yok sayılır (geç biten ekran boyutlu çözme
    /// keskin görüntünün yerine geçmesin). Hiçbir görüntü yokken gelen null, kareyi "önizleme yok" yapar.
    /// </summary>
    public bool Offer(ImageSource? image, int width)
    {
        if (image is null)
        {
            if (_image is null && !_failed) { _failed = true; Raise(nameof(Failed)); }
            return false;
        }
        if (width <= _loadedWidth) return false;
        _loadedWidth = width;
        _image = image; Raise(nameof(Image));
        if (_failed) { _failed = false; Raise(nameof(Failed)); }
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
