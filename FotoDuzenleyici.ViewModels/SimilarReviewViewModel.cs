using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Benzerler: aynı kare, küçültülmüş kopya ve seri çekim grupları. Her grupta en az bir kare kalır.</summary>
public sealed partial class SimilarReviewViewModel : FrameReviewViewModel
{
    public SimilarReviewViewModel(List<SimilarGroupState> groups, bool done, ReviewSaver saver, Services s, Action changed, Action<string> warn)
        : base(groups, done, saver, s, changed, warn) { }

    public override string EmptyText => Done ? "Benzer fotoğraf bulunmadı." : "Benzerlik araması yapılmadı.";
    protected override string FinishedText => "Bütün gruplar gözden geçirildi.";

    [ObservableProperty] private bool onlyUnreviewed;
    partial void OnOnlyUnreviewedChanged(bool value) => Counters();

    public int ReviewedCount => Groups.Count(g => g.Reviewed);
    public string CounterText => $"İncelenen: {ReviewedCount:N0} / {Total:N0}";

    protected override IReadOnlyList<SimilarGroupState> BuildVisible() =>
        OnlyUnreviewed ? Groups.Where(g => !g.Reviewed).ToList() : Groups;

    /// <summary>"Önerileri uygula": önerisi (★) olan gruplarda yalnız o kalır; seri çekimlere dokunulmaz.</summary>
    [RelayCommand]
    void ApplySuggestions()
    {
        foreach (var g in Groups)
            if (g.Rows.FirstOrDefault(r => r.Suggested) is { } star) g.KeepOnly(star);
    }

    /// <summary>"Bulanıkları ayıkla": seri çekimlerde BULANIK kareler; en net kareye ve incelenmiş gruplara dokunulmaz.</summary>
    [RelayCommand]
    void ReleaseSoft()
    {
        int n = Groups.Sum(g => g.ReleaseSoft());
        Warn(n == 0 ? "Ayıklanacak bulanık kare yok." : $"{n:N0} bulanık kare ayıklandı.");
    }

    /// <summary>"Hepsini koru": bütün kareler kalır.</summary>
    [RelayCommand]
    void KeepAll()
    {
        foreach (var r in Groups.SelectMany(g => g.Rows)) r.Keep = true;
    }

    protected override void Counters(bool listChanged = true)
    {
        base.Counters(listChanged);
        OnPropertyChanged(nameof(ReviewedCount));
        OnPropertyChanged(nameof(CounterText));
    }
}
