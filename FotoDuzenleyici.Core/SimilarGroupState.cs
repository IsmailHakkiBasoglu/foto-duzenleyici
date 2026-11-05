using System.ComponentModel;
using System.Windows.Media;

namespace FotoDuzenleyici.Core;

/// <summary>"Benzerler" sekmesinde bir dosya. Kalsın işareti kaldırılırsa _Benzerler'e gider.</summary>
public sealed class SimilarRow : INotifyPropertyChanged
{
    bool _keep = true;
    ImageSource? _thumb;
    bool _thumbFailed;

    public required int Group { get; init; }
    public required MediaFile File { get; init; }
    public required SimilarKind Kind { get; init; }
    public string KindText => SimilarGroupState.KindName(Kind);
    public bool Suggested { get; init; }
    public string Star => Suggested ? "★" : "";
    public string Path => File.Path;
    public string Name => File.FileName;
    public string Resolution => File.Width is { } w && File.Height is { } h ? $"{w}×{h}" : "-";
    public string SizeMb => (File.Size / 1048576.0).ToString("N1");
    public string Taken => File.Taken?.ToString("yyyy-MM-dd HH:mm:ss") + (File.SubSec is { Length: > 0 } s ? "." + s : "");

    /// <summary>Karenin RAW eşi var: kartta "RAW" rozeti; ayıklanırsa Plan'da RAW'ın ne olacağı sorulur.</summary>
    public bool HasRaw { get; set; }
    public string RawBadge => HasRaw ? "RAW" : "";

    /// <summary>Kararların saklandığı dosya kimliği (<see cref="HashCache.Key"/>); hesaplanamadıysa null.</summary>
    public string? StoreKey { get; set; }

    public ImageSource? Thumb
    {
        get => _thumb;
        set { _thumb = value; Raise(nameof(Thumb)); }
    }

    /// <summary>Küçük resim çözülemedi (dosya yok ya da açılamıyor).</summary>
    public bool ThumbFailed
    {
        get => _thumbFailed;
        set { _thumbFailed = value; Raise(nameof(ThumbFailed)); }
    }

    /// <summary>İşaret kaldırılmadan önce sorulur: grubun son işaretli dosyası bırakılamaz.</summary>
    public Func<SimilarRow, bool>? CanRelease { get; set; }
    /// <summary>Kullanıcı işareti değiştirdi (geri yüklemede çağrılmaz).</summary>
    public Action<SimilarRow>? Changed { get; set; }

    public bool Keep
    {
        get => _keep;
        set
        {
            if (_keep == value) return;
            if (!value && CanRelease?.Invoke(this) == false) { Raise(nameof(Keep)); return; }
            _keep = value;
            Raise(nameof(Keep));
            Changed?.Invoke(this);
        }
    }

    /// <summary>Kaydedilmiş kararı uygular: görünüm güncellenir ama <see cref="Changed"/> çağrılmaz.</summary>
    internal void LoadKeep(bool keep)
    {
        if (_keep == keep) return;
        _keep = keep;
        Raise(nameof(Keep));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Bir benzer grubunun arayüz durumu. Şerit görünümü de karşılaştırma penceresi de aynı nesneyi kullanır.
/// Her grupta en az bir kare kalır.
/// </summary>
public sealed class SimilarGroupState : INotifyPropertyChanged
{
    bool _reviewed;

    public SimilarGroupState(int number, SimilarKind kind, IReadOnlyList<SimilarRow> rows)
    {
        Number = number; Kind = kind; Rows = rows;
        foreach (var r in rows) r.CanRelease = CanRelease;
    }

    public int Number { get; }
    public SimilarKind Kind { get; }
    public string KindText => KindName(Kind);
    public IReadOnlyList<SimilarRow> Rows { get; }
    public int KeptCount => Rows.Count(r => r.Keep);

    /// <summary>Üye anahtarlarından imza; bir üyenin anahtarı yoksa null (incelendi bilgisi tutulmaz).</summary>
    public string? Signature { get; private set; }

    public bool Reviewed
    {
        get => _reviewed;
        private set { if (_reviewed == value) return; _reviewed = value; Raise(nameof(Reviewed)); }
    }

    public static string KindName(SimilarKind k) => k switch
    {
        SimilarKind.SameFrame => "Aynı kare", SimilarKind.ResizedCopy => "Küçültülmüş kopya", _ => "Seri çekim",
    };

    public static List<SimilarGroupState> FromGroups(IReadOnlyList<SimilarGroup> groups) =>
        groups.Select((g, i) => new SimilarGroupState(i + 1, g.Kind, g.Members.Select(m => new SimilarRow
        {
            Group = i + 1, File = m, Kind = g.Kind, Suggested = m == g.Suggested,
        }).ToList())).ToList();

    public bool CanRelease(SimilarRow row) => Rows.Any(o => o != row && o.Keep);

    /// <summary>Yalnız bu kare kalsın; önce o işaretlenir ki grup hiçbir an boş kalmasın.</summary>
    public void KeepOnly(SimilarRow row)
    {
        row.Keep = true;
        foreach (var r in Rows.Where(r => r != row)) r.Keep = false;
    }

    /// <summary>
    /// Kaydedilmiş işaretleri ve incelendi bilgisini uygular. Hepsi "ayıkla" çıkarsa ★, yoksa ilk kare kalır.
    /// Üyeler değiştiyse imza değişir: grup incelenmedi görünür ama eski üyelerin işaretleri korunur.
    /// </summary>
    public void Restore(ReviewStore store)
    {
        foreach (var r in Rows)
            if (r.StoreKey is { } k && store.GetKeep(k) is { } keep) r.LoadKeep(keep);
        if (KeptCount == 0) (Rows.FirstOrDefault(r => r.Suggested) ?? Rows[0]).LoadKeep(true);

        Signature = Rows.All(r => r.StoreKey is not null) ? ReviewStore.Signature(Rows.Select(r => r.StoreKey!)) : null;
        Reviewed = Signature is not null && store.IsReviewed(Signature);
    }

    public void SetReviewed(ReviewStore store, bool reviewed)
    {
        if (Signature is null) return;
        store.SetReviewed(Signature, reviewed);
        Reviewed = reviewed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
