using CommunityToolkit.Mvvm.ComponentModel;

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

    public override IReadOnlyList<SimilarGroupState> Visible =>
        OnlyUnreviewed ? Groups.Where(g => !g.Reviewed).ToList() : Groups;

    protected override void Counters(bool listChanged = true)
    {
        base.Counters(listChanged);
        OnPropertyChanged(nameof(ReviewedCount));
        OnPropertyChanged(nameof(CounterText));
    }
}
