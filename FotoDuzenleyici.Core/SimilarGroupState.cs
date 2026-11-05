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
    /// <summary>★ önerilen kopya; seri çekimde ◎ en net kare.</summary>
    public string Star => Suggested ? "★" : Sharpest ? "◎" : "";

    int? _sharpPercent;
    bool _sharpest;
    string? _flaw;

    /// <summary>Seri çekimde grubun en netine göre netlik yüzdesi; ölçülmediyse null.</summary>
    public int? SharpPercent
    {
        get => _sharpPercent;
        set { _sharpPercent = value; Raise(nameof(SharpPercent)); }
    }

    public bool Sharpest
    {
        get => _sharpest;
        set { _sharpest = value; Raise(nameof(Sharpest)); Raise(nameof(Star)); }
    }

    /// <summary>Kart rozeti: "BULANIK", "KARANLIK" ya da ikisi; yoksa null.</summary>
    public string? Flaw
    {
        get => _flaw;
        set { _flaw = value; Raise(nameof(Flaw)); }
    }
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

    readonly bool _allowEmpty;

    /// <param name="allowEmpty">Kusurlu liste: tek kare ayıklanabilir, "en az bir kare kalsın" kuralı yok.</param>
    public SimilarGroupState(int number, SimilarKind kind, IReadOnlyList<SimilarRow> rows, bool allowEmpty = false)
    {
        Number = number; Kind = kind; Rows = rows; _allowEmpty = allowEmpty;
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
        SimilarKind.SameFrame => "Aynı kare", SimilarKind.ResizedCopy => "Küçültülmüş kopya",
        SimilarKind.Flawed => "Kusurlu", _ => "Seri çekim",
    };

    public static List<SimilarGroupState> FromGroups(IReadOnlyList<SimilarGroup> groups) =>
        groups.Select((g, i) => new SimilarGroupState(i + 1, g.Kind, g.Members.Select(m => new SimilarRow
        {
            Group = i + 1, File = m, Kind = g.Kind, Suggested = m == g.Suggested,
        }).ToList())).ToList();

    public bool CanRelease(SimilarRow row) => _allowEmpty || Rows.Any(o => o != row && o.Keep);

    /// <summary>Seri çekim grubunda netlik sıralamasını satırlara yazar; diğer türlere dokunmaz.</summary>
    public void ApplyBurstRanks(IReadOnlyDictionary<MediaFile, QualityScore> scores)
    {
        if (Kind != SimilarKind.Burst) return;
        var ranks = QualityRules.RankBurst(Rows.Select(r => scores.TryGetValue(r.File, out var s) ? s : (QualityScore?)null).ToList());
        for (int i = 0; i < Rows.Count; i++)
        {
            Rows[i].SharpPercent = ranks[i].Percent;
            Rows[i].Sharpest = ranks[i].Sharpest;
            Rows[i].Flaw = ranks[i].Soft ? "BULANIK" : null;
        }
    }

    /// <summary>
    /// "Bulanıkları ayıkla": BULANIK rozetli kareleri ayıklar; en nete dokunmaz, son kare kuralına uyar.
    /// Kullanıcının elle karar verdiği gruplar (incelendi ya da en net kare ayıklanmış) olduğu gibi kalır.
    /// </summary>
    public int ReleaseSoft()
    {
        if (Kind != SimilarKind.Burst || Reviewed) return 0;
        if (Rows.FirstOrDefault(r => r.Sharpest) is not { Keep: true }) return 0;
        int n = 0;
        foreach (var r in Rows.Where(r => r.Flaw == "BULANIK" && !r.Sharpest && r.Keep))
        {
            r.Keep = false;
            if (!r.Keep) n++;
        }
        return n;
    }

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
        if (KeptCount == 0 && !_allowEmpty) (Rows.FirstOrDefault(r => r.Suggested) ?? Rows[0]).LoadKeep(true);

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
