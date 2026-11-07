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
    }

    public RecentPair? LastOrganize { get; }
    public IReadOnlyList<RecentPair> Recent { get; }

    /// <summary>null → boş akış; dolu → klasörler önceden seçili.</summary>
    public event Action<RecentPair?>? OrganizeRequested;

    [RelayCommand]
    void StartOrganize(RecentPair? p) => OrganizeRequested?.Invoke(p);
}
