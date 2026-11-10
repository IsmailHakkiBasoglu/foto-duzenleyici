using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Ana ekran: modül kartları ve son kullanılanlar.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    public HomeViewModel(AppSettings settings, string? selectedFolder = null)
    {
        Recent = settings.Recent.ToList();
        LastOrganize = Recent.FirstOrDefault();
        LastClean = settings.RecentClean.FirstOrDefault();
        LastBackup = settings.LastBackup;
        this.selectedFolder = selectedFolder;
    }

    public RecentPair? LastOrganize { get; }
    /// <summary>Son temizlenen klasörler (Temizle kartı).</summary>
    public RecentPair? LastClean { get; }
    public IReadOnlyList<RecentPair> Recent { get; }

    /// <summary>null → boş akış; dolu → klasörler önceden seçili.</summary>
    public event Action<RecentPair?>? OrganizeRequested;

    [RelayCommand]
    void StartOrganize(RecentPair? p) => OrganizeRequested?.Invoke(p);

    /// <summary>null → boş Temizle; dolu → klasörler ve öncelikli klasörler önceden seçili.</summary>
    public event Action<RecentPair?>? CleanRequested;

    [RelayCommand]
    void StartClean(RecentPair? p) => CleanRequested?.Invoke(p);

    /// <summary>Son yedekleme (Yedekle kartı).</summary>
    public BackupPair? LastBackup { get; }

    public event Action? BackupRequested;

    /// <summary>Sağ tıkla açılışta verilen klasör: kartların "Başla →" düğmeleri onu kullanır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFolder))]
    private string? selectedFolder;

    public bool HasSelectedFolder => SelectedFolder is not null;

    /// <summary>× ile seçim bırakıldı: kabuk sonraki ana ekranlarda göstermez.</summary>
    public event Action? SelectedFolderCleared;

    [RelayCommand]
    void ClearSelectedFolder()
    {
        SelectedFolder = null;
        SelectedFolderCleared?.Invoke();
    }

    /// <summary>Düzenle kartı: seçili klasör kaynak, arşiv son kullanılan; seçim yoksa son çift.</summary>
    [RelayCommand]
    void OrganizeCard() => OrganizeRequested?.Invoke(SelectedFolder is { } f
        ? new RecentPair(new() { f }, LastOrganize?.Archive ?? "", DateTime.Now)
        : LastOrganize);

    /// <summary>Temizle kartı: seçili klasör temizlenir; seçim yoksa son temizlenen.</summary>
    [RelayCommand]
    void CleanCard() => CleanRequested?.Invoke(SelectedFolder is { } f
        ? new RecentPair(new() { f }, "", DateTime.Now)
        : LastClean);

    [RelayCommand]
    void StartBackup() => BackupRequested?.Invoke();
}
