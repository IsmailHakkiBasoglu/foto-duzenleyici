using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Yinelenenler: her kopya grubunda korunacak konum. Kararlar yalnız bu tarama boyunca tutulur (klasikteki gibi).</summary>
public sealed partial class DuplicatesReviewViewModel : ObservableObject
{
    readonly PlanOptions _opt;
    readonly Services _s;
    readonly Action _changed;

    public DuplicatesReviewViewModel(IReadOnlyList<DuplicateGroupState> groups, PlanOptions opt, Services s, Action changed)
    {
        Groups = groups; _opt = opt; _s = s; _changed = changed;
        foreach (var g in groups)
        {
            g.PropertyChanged += OnGroupChanged;
            foreach (var r in g.Rows) r.PropertyChanged += OnRowChanged;
        }
    }

    public IReadOnlyList<DuplicateGroupState> Groups { get; }

    public string CounterText
    {
        get
        {
            if (Groups.Count == 0) return "Birebir kopya yok.";
            var gb = Groups.Sum(g => g.WastedBytes) / 1e9;
            var manual = Groups.Count(g => g.UserChosen);
            return $"{Groups.Count:N0} grup · {Groups.Sum(g => g.Group.Copies.Count):N0} kopya · {gb:N2} GB kazanılacak" +
                   (manual > 0 ? $" · {manual:N0} grupta elle seçim" : "");
        }
    }

    void OnGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(CounterText));

    /// <summary>Korunan konum değişti (grup zaten elle seçilmiş olsa da): plan eskir.</summary>
    void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DuplicateRow.IsKeeper) && sender is DuplicateRow { IsKeeper: true }) _changed();
    }

    /// <summary>"Öneriye dön": elle seçimleri bırakır.</summary>
    [RelayCommand]
    void Reset()
    {
        foreach (var g in Groups) g.Reset(_opt);
    }

    [RelayCommand]
    void OpenFile(DuplicateRow? row)
    {
        if (row is not null) _s.Shell.OpenFile(row.File.Path);
    }

    /// <summary>Hedef klasör değişmiş olabilir: öneriler ve "düzenli" etiketleri tazelenir.</summary>
    public void Refresh() { foreach (var g in Groups) g.Refresh(_opt); }

    public IReadOnlySet<MediaFile> UserKeepers => DuplicateGroupState.UserKeepers(Groups);

    public void Detach()
    {
        foreach (var g in Groups)
        {
            g.PropertyChanged -= OnGroupChanged;
            foreach (var r in g.Rows) r.PropertyChanged -= OnRowChanged;
        }
    }
}
