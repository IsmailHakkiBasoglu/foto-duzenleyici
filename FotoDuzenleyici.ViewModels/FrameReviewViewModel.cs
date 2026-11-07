using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Benzerler ve Kusurlu bölümlerinin ortak tabanı: kare kartları (kalsın ↔ ayıkla), kararların kaydı, karşılaştırma
/// penceresi. Kararlar klasik pencereyle ortak <see cref="ReviewStore"/>'a yazılır.
/// </summary>
public abstract partial class FrameReviewViewModel : ObservableObject
{
    readonly ReviewSaver _saver;
    readonly Services _s;
    readonly Action _changed;
    protected readonly Action<string> Warn;

    protected FrameReviewViewModel(List<SimilarGroupState> groups, bool done, ReviewSaver saver, Services s, Action changed, Action<string> warn)
    {
        Groups = groups; Done = done; _saver = saver; _s = s; _changed = changed; Warn = warn;
        foreach (var g in groups)
        {
            g.PropertyChanged += OnGroupChanged;
            foreach (var r in g.Rows)
            {
                r.CanRelease = row =>
                {
                    bool ok = g.CanRelease(row);
                    if (!ok) Warn("Her grupta en az bir dosya kalmalı.");
                    return ok;
                };
                r.Changed = OnRowChanged;
            }
        }
    }

    public List<SimilarGroupState> Groups { get; }
    /// <summary>Bu ölçüm yapıldı ve bitti; değilse boş durum yazısı gösterilir.</summary>
    public bool Done { get; }
    public abstract string EmptyText { get; }
    public int Total => Groups.Count;
    public int ReleasedCount => Groups.Sum(g => g.Rows.Count(r => !r.Keep));
    public abstract IReadOnlyList<SimilarGroupState> Visible { get; }
    protected abstract string FinishedText { get; }

    [ObservableProperty] private SimilarGroupState? selected;

    void OnRowChanged(SimilarRow row)
    {
        if (row.StoreKey is { } k) _saver.Store.SetKeep(k, row.Keep);
        _saver.Changed();
        Counters();
        _changed();
    }

    void OnGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SimilarGroupState.Reviewed)) return;
        _saver.Changed();
        Counters();
    }

    protected virtual void Counters()
    {
        OnPropertyChanged(nameof(ReleasedCount));
        OnPropertyChanged(nameof(Visible));
    }

    [RelayCommand]
    void Toggle(SimilarRow? row)
    {
        if (row is not null) row.Keep = !row.Keep;
    }

    /// <summary>Görünen gruplar arasında gezinen tam ekran karşılaştırma.</summary>
    [RelayCommand]
    void OpenCompare(SimilarGroupState? g)
    {
        if (g is null || _s.Compare is null) return;
        var visible = Visible.ToList();
        int start = visible.IndexOf(g);
        if (start < 0) { visible = Groups; start = Groups.IndexOf(g); }
        if (start < 0) return;
        var (current, finished) = _s.Compare.Open(visible, start, _saver.Store, () => { _saver.Changed(); Counters(); _changed(); });
        Counters();
        Selected = current;
        if (finished) Warn(FinishedText);
    }

    /// <summary>Yeniden taramada: eski grupların olayları bırakılır.</summary>
    public void Detach()
    {
        foreach (var g in Groups)
        {
            g.PropertyChanged -= OnGroupChanged;
            foreach (var r in g.Rows) { r.Changed = null; r.CanRelease = g.CanRelease; }
        }
    }
}
