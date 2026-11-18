using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Izgarada bir gün; <paramref name="Day"/> null → "Başka aydan".</summary>
public sealed record DayGroup(DateOnly? Day, string Header, IReadOnlyList<MonthPhoto> Photos);

/// <summary>Takvimde bir gün kutusu; ay dışındaki günler soluk, sayısız. <paramref name="Preview"/> en çok 4 karo.</summary>
public sealed record CalendarDay(int Day, bool InMonth, int Count, IReadOnlyList<MonthPhoto> Preview, int More)
{
    public bool IsEmpty => Count == 0;
    public string MoreText => More > 0 ? $"+{More}" : "";
}

public enum MonthViewMode { Grid, Calendar }

/// <summary>Zaman çizelgesinin süzgecine göre ayın karoları.</summary>
public static class MonthItems
{
    public const int CalendarPreview = 4;

    public static List<MonthPhoto> Build(IReadOnlyList<MediaFile> files, KindFilter filter, int year, int month)
    {
        var pairs = RawJpgPairs.Index(files);
        bool Other(MediaFile f) => f.Taken is not { } t || t.Year != year || t.Month != month;
        // Eşteki RAW'lar grubun ilk fotoğrafına bağlanır (aynı adlı JPG + HEIC varsa RAW bir kez gider).
        IReadOnlyList<MediaFile> RawsOf(MediaFile photo) =>
            pairs.TryGetValue(photo, out var g) && g.First(x => x.Kind == FileKind.Photo) == photo
                ? g.Where(x => x.Kind == FileKind.Raw).ToList()
                : Array.Empty<MediaFile>();
        var none = Array.Empty<MediaFile>();

        IEnumerable<MonthPhoto> items = filter switch
        {
            KindFilter.Photo => files.Where(f => f.Kind == FileKind.Photo).Select(f => new MonthPhoto(f, RawsOf(f), Other(f))),
            KindFilter.Raw => files.Where(f => f.Kind == FileKind.Raw).Select(f => new MonthPhoto(f, none, Other(f))),
            KindFilter.Video => files.Where(f => f.Kind == FileKind.Video).Select(f => new MonthPhoto(f, none, Other(f))),
            _ => files.Where(f => f.Kind == FileKind.Photo).Select(f => new MonthPhoto(f, RawsOf(f), Other(f)))
                .Concat(files.Where(f => f.Kind == FileKind.Raw && !pairs.ContainsKey(f)).Select(f => new MonthPhoto(f, none, Other(f))))
                .Concat(files.Where(f => f.Kind == FileKind.Video).Select(f => new MonthPhoto(f, none, Other(f)))),
        };
        return items.OrderBy(p => p.File.Taken ?? DateTime.MaxValue)
            .ThenBy(p => p.File.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

/// <summary>
/// Zaman çizelgesinde bir aya girince açılan görünüm: ayın dosyaları yeniden okunur, Izgara (günlere bölünmüş) ya da
/// Takvim olarak gösterilir. İşaretleme, Ayıkla ve Gözden geçir bu sınıfın öbür yarısında.
/// </summary>
public sealed partial class MonthViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;
    readonly Func<string, MediaFile>? _read;
    readonly string _logDir;
    CancellationTokenSource? _cts;
    int _generation;
    List<MediaFile> _files = new();

    /// <param name="logDir">Geri Dönüşüm Kutusu'na gönderilenlerin kaydı; null → karar dosyasının klasörü\Ayikla
    /// (uygulama veri klasörü).</param>
    public MonthViewModel(Services s, AppSettings settings, string root, TimelineMethod method, int year, int month,
        IReadOnlyList<string> folders, KindFilter filter, Func<string, MediaFile>? read = null, string? logDir = null)
    {
        _s = s;
        _settings = settings;
        _read = read;
        Root = root;
        Method = method;
        Year = year;
        MonthNumber = month;
        Folders = folders;
        Filter = filter;
        _logDir = logDir ?? Path.Combine(Path.GetDirectoryName(s.CacheFiles.Reviews)!, "Ayikla");
        mode = Enum.TryParse<MonthViewMode>(settings.MonthView, out var m) ? m : MonthViewMode.Grid;
    }

    public string Root { get; }
    public TimelineMethod Method { get; }
    public int Year { get; }
    public int MonthNumber { get; }
    public KindFilter Filter { get; }
    public IReadOnlyList<string> Folders { get; }
    public bool HasManyFolders => Folders.Count > 1;

    public string Title => $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(MonthNumber)} {Year}";

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool stopped;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int done;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int total;

    public string ProgressText => Total > 0 ? $"{Done:N0} / {Total:N0} dosya okundu" : "Okunuyor…";

    [ObservableProperty] private MonthViewMode mode;
    /// <summary>Izgarada başına kaydırılacak gün (Takvim'den gün açılınca).</summary>
    [ObservableProperty] private DateOnly? scrollTarget;

    public IReadOnlyList<MonthPhoto> Photos { get; private set; } = Array.Empty<MonthPhoto>();
    public IReadOnlyList<DayGroup> Days { get; private set; } = Array.Empty<DayGroup>();
    public IReadOnlyList<IReadOnlyList<CalendarDay>> Weeks { get; private set; } = Array.Empty<IReadOnlyList<CalendarDay>>();
    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();
    /// <summary>Okunamayanların ilk 20'si (ipucu metni).</summary>
    public string ErrorsText => string.Join('\n', Errors.Take(20));
    public bool IsEmpty => !IsLoading && Photos.Count == 0;

    public string Summary => $"{Photos.Count:N0} dosya · {Sizes.Text(Photos.Sum(p => p.File.Size))}";

    /// <summary>Geri dönülmek isteniyor; zaman çizelgesi ay görünümünü kapatır.</summary>
    public event Action? BackRequested;

    partial void OnModeChanged(MonthViewMode value)
    {
        _settings.MonthView = value.ToString();
        _s.Settings.TrySave(_settings);
    }

    public Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        int gen = ++_generation;
        Stopped = false;
        Done = 0;
        Total = 0;
        IsLoading = true;
        SetPhotos(Array.Empty<MonthPhoto>());
        return Load(gen, _cts.Token);
    }

    async Task Load(int gen, CancellationToken ct)
    {
        IProgress<(int done, int total)> ui = new Progress<(int done, int total)>(v =>
        {
            if (gen != _generation || v.done < Done) return;
            Done = v.done;
            Total = v.total;
        });
        try
        {
            var (method, y, m, folders, read) = (Method, Year, MonthNumber, Folders, _read);
            var r = await Task.Run(() => MonthReader.Read(method, y, m, folders, ui, ct, read));
            if (gen != _generation || ct.IsCancellationRequested || r.Cancelled) return;
            _files = r.Files;
            Errors = r.Errors;
            OnPropertyChanged(nameof(Errors)); OnPropertyChanged(nameof(ErrorsText));
            SetPhotos(MonthItems.Build(_files, Filter, Year, MonthNumber));
        }
        catch (Exception ex) when (gen == _generation)
        {
            Errors = new[] { ex.Message };
            OnPropertyChanged(nameof(Errors)); OnPropertyChanged(nameof(ErrorsText));
        }
        finally
        {
            if (gen == _generation) { IsLoading = false; OnPropertyChanged(nameof(IsEmpty)); }
        }
    }

    void SetPhotos(IReadOnlyList<MonthPhoto> photos)
    {
        Photos = photos;
        Days = BuildDays(photos);
        foreach (var d in Days) foreach (var p in d.Photos) p.DayHeader = d.Header;
        Weeks = BuildWeeks(photos);
        OnPropertyChanged(nameof(Photos));
        OnPropertyChanged(nameof(Days));
        OnPropertyChanged(nameof(Weeks));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
        PhotosChanged();
    }

    /// <summary>Karolar değişince öbür yarının (işaretler, Ayıkla) güncellenmesi.</summary>
    partial void PhotosChanged();

    List<DayGroup> BuildDays(IReadOnlyList<MonthPhoto> photos)
    {
        var fmt = CultureInfo.CurrentCulture.DateTimeFormat;
        var days = photos.Where(p => !p.OtherMonth)
            .GroupBy(p => DateOnly.FromDateTime(p.Taken))
            .OrderBy(g => g.Key)
            .Select(g => new DayGroup(g.Key,
                $"{g.Key.Day} {fmt.GetMonthName(g.Key.Month)} {fmt.GetDayName(g.Key.DayOfWeek)} · {g.Count():N0}",
                g.ToList()))
            .ToList();
        var other = photos.Where(p => p.OtherMonth).ToList();
        if (other.Count > 0) days.Add(new DayGroup(null, $"Başka aydan · {other.Count:N0}", other));
        return days;
    }

    List<IReadOnlyList<CalendarDay>> BuildWeeks(IReadOnlyList<MonthPhoto> photos)
    {
        var byDay = photos.Where(p => !p.OtherMonth).GroupBy(p => p.Taken.Day).ToDictionary(g => g.Key, g => g.ToList());
        var first = new DateOnly(Year, MonthNumber, 1);
        int lead = ((int)first.DayOfWeek + 6) % 7; // Pazartesi = 0
        var start = first.AddDays(-lead);
        var last = first.AddMonths(1).AddDays(-1);
        var weeks = new List<IReadOnlyList<CalendarDay>>();
        for (var d = start; d <= last; d = d.AddDays(7))
        {
            var week = new List<CalendarDay>(7);
            for (int i = 0; i < 7; i++)
            {
                var day = d.AddDays(i);
                bool inMonth = day.Month == MonthNumber && day.Year == Year;
                var list = inMonth && byDay.TryGetValue(day.Day, out var l) ? l : new List<MonthPhoto>();
                week.Add(new CalendarDay(day.Day, inMonth, list.Count, list.Take(MonthItems.CalendarPreview).ToList(),
                    Math.Max(0, list.Count - MonthItems.CalendarPreview)));
            }
            weeks.Add(week);
        }
        return weeks;
    }

    [RelayCommand]
    void OpenDay(CalendarDay? d)
    {
        if (d is not { InMonth: true, Count: > 0 }) return;
        Mode = MonthViewMode.Grid;
        ScrollTarget = null;
        ScrollTarget = new DateOnly(Year, MonthNumber, d.Day);
    }

    [RelayCommand]
    void OpenFolder(string? path)
    {
        if (!string.IsNullOrEmpty(path)) _s.Shell.OpenFolder(path);
    }

    [RelayCommand]
    Task Reload() => LoadAsync();

    /// <summary>Video ya da büyük resim: Windows'un varsayılan uygulamasında açılır; dosyaya dokunulmaz.</summary>
    [RelayCommand]
    void OpenFile(MonthPhoto? p)
    {
        if (p is not null) _s.Shell.OpenFile(p.File.Path);
    }

    [RelayCommand]
    void Stop()
    {
        if (!IsLoading) return;
        _cts?.Cancel();
        _generation++;
        IsLoading = false;
        Stopped = true;
        SetPhotos(Array.Empty<MonthPhoto>());
    }

    [RelayCommand]
    void Back()
    {
        if (!CanLeave()) return;
        if (IsLoading) { _cts?.Cancel(); _generation++; IsLoading = false; }
        BackRequested?.Invoke();
    }

    /// <summary>Ay görünümünden çıkılabilir mi (öbür yarı: işaretliyse sorar, atma sürerken hayır).</summary>
    partial void CheckLeave(ref bool ok);

    bool CanLeave()
    {
        bool ok = true;
        CheckLeave(ref ok);
        return ok;
    }
}
