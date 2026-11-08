using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Ana ekran: modül kartları ve son kullanılanlar.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    public HomeViewModel(AppSettings settings)
    {
        Recent = settings.Recent.ToList();
        LastOrganize = Recent.FirstOrDefault();
        LastClean = settings.RecentClean.FirstOrDefault();
        LastBackup = settings.LastBackup;
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

    [RelayCommand]
    void StartBackup() => BackupRequested?.Invoke();
}
