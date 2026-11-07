using CommunityToolkit.Mvvm.ComponentModel;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Kusurlu: tek başına bulanık ya da çok karanlık kareler. Öntanımlı hepsi kalır; tek kare ayıklanabilir.</summary>
public sealed partial class FlawedReviewViewModel : FrameReviewViewModel
{
    public FlawedReviewViewModel(List<SimilarGroupState> flawed, bool done, ReviewSaver saver, Services s, Action changed, Action<string> warn)
        : base(flawed, done, saver, s, changed, warn) { }

    public override string EmptyText => Done ? "Kusurlu kare yok." : "Netlik ölçülmedi.";
    protected override string FinishedText => "Bütün kusurlu kareler gözden geçirildi.";

    [ObservableProperty] private bool showBlurry = true;
    [ObservableProperty] private bool showDark = true;
    partial void OnShowBlurryChanged(bool value) => Counters();
    partial void OnShowDarkChanged(bool value) => Counters();

    static string FlawOf(SimilarGroupState g) => g.Rows[0].Flaw ?? "";

    public override IReadOnlyList<SimilarGroupState> Visible =>
        Groups.Where(g => ShowBlurry && FlawOf(g).Contains("BULANIK") || ShowDark && FlawOf(g).Contains("KARANLIK")).ToList();

    public string CounterText
    {
        get
        {
            if (Groups.Count == 0) return "Kusurlu kare yok.";
            return $"Bulanık: {Groups.Count(g => FlawOf(g).Contains("BULANIK")):N0}  ·  Karanlık: {Groups.Count(g => FlawOf(g).Contains("KARANLIK")):N0}" +
                   $"  ·  Ayıklanacak: {ReleasedCount:N0} / {Total:N0}";
        }
    }

    protected override void Counters(bool listChanged = true)
    {
        base.Counters(listChanged);
        OnPropertyChanged(nameof(CounterText));
    }
}
