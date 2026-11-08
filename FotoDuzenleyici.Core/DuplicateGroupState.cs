using System.ComponentModel;
using System.Globalization;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Birebir kopya grubunda hangi dosyanın kalacağı. Plan da Yinelenenler sekmesi de bu kuralı kullanır:
/// kullanıcının seçtiği, yoksa düzenli klasörde duran (tekrar çalıştırmada yerinde kalsın), yoksa en eski.
/// </summary>
public static class DuplicateKeeper
{
    /// <summary>Dosya hedef klasörde ve _Tekrarlar dışında: önceki turda yerleştirilmiş.</summary>
    public static bool IsOrganized(MediaFile f, PlanOptions opt)
    {
        if (string.IsNullOrWhiteSpace(opt.DestinationRoot)) return false;
        var dest = Path.GetFullPath(opt.DestinationRoot).TrimEnd('\\') + "\\";
        var dupDir = dest + opt.DuplicatesFolderName + "\\";
        var full = Path.GetFullPath(f.Path);
        return full.StartsWith(dest, StringComparison.OrdinalIgnoreCase)
               && !full.StartsWith(dupDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Grubun üyeleri: önce <see cref="DuplicateGroup.Keeper"/> (en eski), sonra kopyalar.</summary>
    public static List<MediaFile> Members(DuplicateGroup g) => g.Copies.Prepend(g.Keeper).ToList();

    /// <param name="chosen">Kullanıcının Yinelenenler sekmesinde seçtiği korunacak dosyalar.</param>
    public static MediaFile Choose(DuplicateGroup g, PlanOptions opt, IReadOnlySet<MediaFile>? chosen = null) =>
        Choose(g, m => IsOrganized(m, opt), chosen);

    /// <summary>
    /// Genel kural: kullanıcının seçtiği, yoksa <paramref name="preferred"/> olan ilk üye, yoksa en eski.
    /// Düzenle için "düzenli klasörde", Temizle için "öncelikli klasörde".
    /// </summary>
    public static MediaFile Choose(DuplicateGroup g, Func<MediaFile, bool> preferred, IReadOnlySet<MediaFile>? chosen = null)
    {
        var members = Members(g);
        return members.FirstOrDefault(m => chosen?.Contains(m) == true)
               ?? members.FirstOrDefault(preferred)
               ?? g.Keeper;
    }

    /// <summary>Dosya verilen klasörlerden birinin içinde mi? <c>D:\A2</c>, <c>D:\A</c> içinde sayılmaz.</summary>
    public static bool InAny(MediaFile f, IEnumerable<string> folders)
    {
        var full = Path.GetFullPath(f.Path);
        foreach (var folder in folders)
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            var withSep = root.EndsWith('\\') ? root : root + "\\";
            if (full.StartsWith(withSep, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>Yinelenenler sekmesinde bir konum. "Kalsın" seçilirse grubun öteki üyeleri _Tekrarlar'a gider.</summary>
public sealed class DuplicateRow : INotifyPropertyChanged
{
    bool _isKeeper, _organized;

    public required DuplicateGroupState Owner { get; init; }
    public required MediaFile File { get; init; }
    public string Folder => Path.GetDirectoryName(File.Path) ?? "";
    public string Name => File.FileName;
    public string Created => File.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Öncelikli konumda: Düzenle'de düzenli arşivde (önceki turda yerleştirilmiş), Temizle'de öncelikli klasörde.</summary>
    public bool Organized
    {
        get => _organized;
        internal set { if (_organized == value) return; _organized = value; Raise(nameof(Organized)); }
    }

    /// <summary>true yapmak bu dosyayı korunan yapar; korunanı false yapmak yok sayılır (her grupta biri kalır).</summary>
    public bool IsKeeper
    {
        get => _isKeeper;
        set
        {
            if (value && !_isKeeper) Owner.Choose(this);
            else if (!value && _isKeeper) Raise(nameof(IsKeeper)); // radyo düğmesi geri işaretli görünsün
        }
    }

    internal void SetKeeper(bool k) { if (_isKeeper == k) return; _isKeeper = k; Raise(nameof(IsKeeper)); }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Yinelenenler sekmesinde bir grup. Kararlar yalnız bu tarama boyunca tutulur.</summary>
public sealed class DuplicateGroupState : INotifyPropertyChanged
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    bool _userChosen;

    public required DuplicateGroup Group { get; init; }
    public required int Number { get; init; }
    public List<DuplicateRow> Rows { get; } = new();
    /// <summary>Küçük resim taşıyıcısı (içerik birebir aynı, tek resim yeter).</summary>
    public required SimilarRow Preview { get; init; }

    public long WastedBytes => Group.Copies.Sum(c => c.Size);
    public string SizeText => Mb(Group.Keeper.Size) + " MB";
    public string CountText => $"{Rows.Count} konum";
    public string WastedText => Mb(WastedBytes) + " MB boşa";

    /// <summary>Kullanıcı öneriden farklı bir konum seçti.</summary>
    public bool UserChosen
    {
        get => _userChosen;
        private set { if (_userChosen == value) return; _userChosen = value; Raise(nameof(UserChosen)); }
    }

    static string Mb(long bytes) => (bytes / 1048576.0).ToString("N1", Tr);

    /// <summary>Gruplar en çok yer kazandırandan başlayarak sıralanır.</summary>
    public static List<DuplicateGroupState> Build(IEnumerable<DuplicateGroup> groups, PlanOptions opt) =>
        Build(groups, m => DuplicateKeeper.IsOrganized(m, opt));

    /// <param name="preferred">Bu konumdaki kopya önerilir ve "Organized" etiketi alır (Düzenle: düzenli arşiv,
    /// Temizle: öncelikli klasör). Durum bu kuralı saklar: <see cref="Refresh()"/> / <see cref="Reset()"/> onu kullanır.</param>
    public static List<DuplicateGroupState> Build(IEnumerable<DuplicateGroup> groups, Func<MediaFile, bool> preferred)
    {
        var list = new List<DuplicateGroupState>();
        int n = 0;
        foreach (var g in groups.OrderByDescending(g => g.Copies.Sum(c => c.Size)))
        {
            var s = new DuplicateGroupState
            {
                Group = g, Number = ++n, Preferred = preferred,
                Preview = new SimilarRow { Group = n, Kind = SimilarKind.SameFrame, File = g.Keeper },
            };
            foreach (var m in DuplicateKeeper.Members(g)) s.Rows.Add(new DuplicateRow { Owner = s, File = m });
            s.Refresh();
            list.Add(s);
        }
        return list;
    }

    /// <summary>Planner'a verilecek: kullanıcının elle seçtiği korunacak dosyalar.</summary>
    public static IReadOnlySet<MediaFile> UserKeepers(IEnumerable<DuplicateGroupState> states) =>
        states.Where(s => s.UserChosen).Select(s => s.Rows.First(r => r.IsKeeper).File).ToHashSet();

    /// <summary>"Düzenli" etiketlerini ve (kullanıcı seçmediyse) öneriyi güncel hedef klasöre göre yeniler.</summary>
    public void Refresh(PlanOptions opt)
    {
        foreach (var r in Rows) r.Organized = DuplicateKeeper.IsOrganized(r.File, opt);
        if (!UserChosen) Select(DuplicateKeeper.Choose(Group, opt));
    }

    /// <summary>Elle seçimi bırakıp Plan'ın önerisine döner.</summary>
    public void Reset(PlanOptions opt) { UserChosen = false; Refresh(opt); }

    /// <summary>Kurulurken verilen öncelik kuralı.</summary>
    public required Func<MediaFile, bool> Preferred { get; init; }

    /// <summary>Etiketleri ve (kullanıcı seçmediyse) öneriyi kurulurken verilen kurala göre yeniler.</summary>
    public void Refresh()
    {
        foreach (var r in Rows) r.Organized = Preferred(r.File);
        if (!UserChosen) Select(DuplicateKeeper.Choose(Group, Preferred));
    }

    /// <summary>Elle seçimi bırakıp kuralın önerisine döner.</summary>
    public void Reset() { UserChosen = false; Refresh(); }

    internal void Choose(DuplicateRow row) { Select(row.File); UserChosen = true; }

    void Select(MediaFile keeper)
    {
        foreach (var r in Rows) r.SetKeeper(r.File == keeper);

    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
