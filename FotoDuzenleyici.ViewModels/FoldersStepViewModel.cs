using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

public sealed class SourceEntry : ObservableObject
{
    public SourceEntry(string path) { Path = path; Exists = Directory.Exists(path); }
    public string Path { get; }
    /// <summary>Klasör yoksa (disk takılı değil, silinmiş) listede uyarı rengiyle görünür, taramaya katılmaz.</summary>
    public bool Exists { get; }
}

/// <summary>1. adım: taranacak kaynaklar ve arşiv; düzen seçeneklerinin özeti.</summary>
public sealed partial class FoldersStepViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;

    public FoldersStepViewModel(Services s, AppSettings settings)
    {
        _s = s; _settings = settings;
        Sources.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(ExistingSources)); };
    }

    public ObservableCollection<SourceEntry> Sources { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanScan))]
    private string? archive;

    public bool CanScan => ExistingSources.Count > 0 && !string.IsNullOrWhiteSpace(Archive);

    public IReadOnlyList<string> ExistingSources => Sources.Where(e => e.Exists).Select(e => e.Path).ToList();

    public string OptionsSummary
    {
        get
        {
            var parts = new List<string> { "yyyy-MM › yyyy-MM-Foto" };
            if (_settings.SourceFolder == SourceFolderMode.SubFolder) parts.Add("Kaynağa göre alt klasör");
            if (_settings.RenameWithDate) parts.Add("Ada tarih ekle");
            if (_settings.AppendSourceToName) parts.Add("Ada kaynak ekle");
            if (_settings.SeparateRawFolder) parts.Add("RAW ayrı klasörde");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Seçenekler iletişim kutusu <see cref="AppSettings"/>'i değiştirdikten sonra çağrılır.</summary>
    public void NotifyOptionsChanged()
    {
        _s.Settings.TrySave(_settings);
        OnPropertyChanged(nameof(OptionsSummary));
    }

    public AppSettings Settings => _settings;

    [RelayCommand]
    void AddSource()
    {
        var p = _s.Folders.PickFolder("Taranacak klasörü seç");
        if (p is null) return;
        Add(p);
    }

    [RelayCommand]
    void RemoveSource(SourceEntry? e)
    {
        if (e is not null) Sources.Remove(e);
    }

    [RelayCommand]
    void PickArchive()
    {
        var p = _s.Folders.PickFolder("Arşiv klasörünü seç", Archive);
        if (p is not null) Archive = Clean(p);
    }

    public PlanOptions BuildPlanOptions() => new()
    {
        DestinationRoot = Path.GetFullPath(Clean(Archive ?? throw new InvalidOperationException("Arşiv seçilmedi"))),
        SourceFolder = _settings.SourceFolder,
        RenameWithDate = _settings.RenameWithDate,
        AppendSourceToName = _settings.AppendSourceToName,
        SeparateRawFolder = _settings.SeparateRawFolder,
    };

    public void Load(RecentPair p)
    {
        Sources.Clear();
        foreach (var s in p.Sources) Add(s);
        Archive = Clean(p.Archive);
    }

    public void RememberCurrent(DateTime now)
    {
        if (Archive is null) return;
        _settings.AddRecent(Sources.Select(e => e.Path), Archive, now);
        _s.Settings.TrySave(_settings);
    }

    void Add(string path)
    {
        var c = Clean(path);
        if (Sources.Any(e => e.Path.Equals(c, StringComparison.OrdinalIgnoreCase))) return;
        Sources.Add(new SourceEntry(c));
    }

    /// <summary>Sondaki ayıraç atılır; sürücü kökü <c>E:\</c> kalır.</summary>
    static string Clean(string p) => Path.TrimEndingDirectorySeparator(p.Trim());
}
