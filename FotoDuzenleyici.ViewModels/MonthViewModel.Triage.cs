using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Gözden geçir'de bir karo: fotoğrafın ve (tutulmuyorsa) RAW'ının nereye gideceği.</summary>
public sealed record ReviewLine(MonthPhoto Photo, DiscardRoute Route, bool RawToRejects)
{
    public bool ToRejects => Route == DiscardRoute.MoveToRejects || RawToRejects;
}

/// <summary>İşaretleme, Ayıkla (tek tek büyük görünüm), Gözden geçir ve atma.</summary>
public sealed partial class MonthViewModel : ILeaveGuard
{
    List<DiscardStep> _steps = new();
    CancellationTokenSource? _discardCts;
    readonly List<(MediaFile File, bool Moved)> _discarded = new();

    // ── İşaretler ────────────────────────────────────────────────

    public int MarkedCount => Photos.Count(p => p.Marked);
    public long MarkedSize => Photos.Where(p => p.Marked).Sum(p => p.DiscardSize);
    public bool HasMarked => MarkedCount > 0;
    public string MarkedText => $"🗑 Atılacaklar: {MarkedCount:N0} · {Sizes.Text(MarkedSize)}";

    /// <summary>İşaretliyken çıkılmak istenirse sorulacak soru; işaret yoksa null.</summary>
    public string? LeaveQuestion => MarkedCount > 0 ? $"{MarkedCount:N0} dosya atılmak üzere işaretli. Bırakıp çıkılsın mı?" : null;

    partial void PhotosChanged()
    {
        foreach (var p in Photos)
        {
            p.PropertyChanged -= PhotoChanged;
            p.PropertyChanged += PhotoChanged;
        }
        MarksChanged();
        OnPropertyChanged(nameof(Position));
    }

    void PhotoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonthPhoto.Marked) or nameof(MonthPhoto.KeepRaw)) MarksChanged();
    }

    void MarksChanged()
    {
        OnPropertyChanged(nameof(MarkedCount));
        OnPropertyChanged(nameof(MarkedSize));
        OnPropertyChanged(nameof(HasMarked));
        OnPropertyChanged(nameof(MarkedText));
        OnPropertyChanged(nameof(LeaveQuestion));
    }

    [RelayCommand]
    void ToggleMark(MonthPhoto? p)
    {
        if (p is null || IsDiscarding) return;
        p.Marked = !p.Marked;
    }

    // ── Ayıkla ───────────────────────────────────────────────────

    [ObservableProperty] private bool isTriage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position), nameof(Next1))]
    private MonthPhoto? current;

    /// <summary>"58 / 140".</summary>
    public string Position => Current is { } c && Photos.Count > 0 ? $"{IndexOf(c) + 1:N0} / {Photos.Count:N0}" : "";

    /// <summary>Bir sonraki fotoğraf (büyük resmi önceden yüklemek için).</summary>
    public MonthPhoto? Next1 => Current is { } c && IndexOf(c) is var i && i >= 0 && i + 1 < Photos.Count ? Photos[i + 1] : null;

    int IndexOf(MonthPhoto p)
    {
        for (int i = 0; i < Photos.Count; i++) if (ReferenceEquals(Photos[i], p)) return i;
        return -1;
    }

    [RelayCommand]
    void StartTriage(MonthPhoto? from)
    {
        if (Photos.Count == 0 || IsReview) return;
        Current = from is not null && IndexOf(from) >= 0 ? from : Photos[0];
        IsTriage = true;
    }

    [RelayCommand]
    void Next() => Move(+1);

    [RelayCommand]
    void Previous() => Move(-1);

    void Move(int step)
    {
        if (Current is null) return;
        int i = IndexOf(Current) + step;
        if (i >= 0 && i < Photos.Count) Current = Photos[i];
    }

    /// <summary>Delete: işareti koyar ya da kaldırır, sonrakine geçer.</summary>
    [RelayCommand]
    void MarkAndNext()
    {
        if (Current is null || IsDiscarding) return;
        Current.Marked = !Current.Marked;
        Next();
    }

    /// <summary>Sola sürükleme: atılsın, sonrakine geç.</summary>
    [RelayCommand]
    void DiscardAndNext()
    {
        if (Current is null || IsDiscarding) return;
        Current.Marked = true;
        Next();
    }

    /// <summary>Sağa sürükleme: kalsın, sonrakine geç.</summary>
    [RelayCommand]
    void KeepAndNext()
    {
        if (Current is null || IsDiscarding) return;
        Current.Marked = false;
        Next();
    }

    /// <summary>"RAW'ı tut": bu fotoğraf atılırsa RAW eşi yerinde kalır. Parametresiz: Ayıkla'da açık olan.</summary>
    [RelayCommand]
    void ToggleKeepRaw(MonthPhoto? p)
    {
        p ??= Current;
        if (p is not { HasRaw: true } || IsDiscarding) return;
        p.KeepRaw = !p.KeepRaw;
        if (IsReview) Replan();
    }

    [RelayCommand]
    void CloseTriage() => IsTriage = false;

    // ── Gözden geçir ─────────────────────────────────────────────

    [ObservableProperty] private bool isReview;
    public IReadOnlyList<ReviewLine> ReviewLines { get; private set; } = Array.Empty<ReviewLine>();

    /// <summary>"6 dosya · 48 MB": işaretli fotoğraflar + tutulmayan RAW'lar.</summary>
    public string ReviewTotal
    {
        get
        {
            var marked = ReviewLines.Select(l => l.Photo).ToList();
            int files = marked.Sum(p => 1 + (p.KeepRaw ? 0 : p.Raws.Count));
            return $"{files:N0} dosya · {Sizes.Text(marked.Sum(p => p.DiscardSize))}";
        }
    }

    public bool HasRejectRoute => _steps.Any(s => s.Route == DiscardRoute.MoveToRejects);

    public string DiscardButtonText
    {
        get
        {
            int bin = _steps.Count(s => s.Route == DiscardRoute.RecycleBin);
            int move = _steps.Count - bin;
            return (bin, move) switch
            {
                (_, 0) => $"{bin:N0} dosyayı Geri Dönüşüm Kutusu'na at",
                (0, _) => $"{move:N0} dosyayı _Ayiklananlar'a taşı",
                _ => $"{bin:N0} dosyayı Geri Dönüşüm Kutusu'na at, {move:N0} dosyayı _Ayiklananlar'a taşı",
            };
        }
    }

    [RelayCommand]
    void OpenReview()
    {
        if (!HasMarked || IsDiscarding) return;
        ResultMessage = null;
        HasFailures = false;
        Replan();
        IsReview = true;
    }

    void Replan()
    {
        var marked = Photos.Where(p => p.Marked).ToList();
        var files = marked.SelectMany(p => p.KeepRaw ? new[] { p.File } : new[] { p.File }.Concat(p.Raws)).ToList();
        _steps = Discarder.Plan(files, Root, _s.RecycleBin);
        var route = _steps.ToDictionary(s => s.File, s => s.Route);
        ReviewLines = marked.Select(p => new ReviewLine(p,
            route.TryGetValue(p.File, out var r) ? r : DiscardRoute.MoveToRejects,
            !p.KeepRaw && p.Raws.Any(x => route.TryGetValue(x, out var rr) && rr == DiscardRoute.MoveToRejects))).ToList();
        OnPropertyChanged(nameof(ReviewLines));
        OnPropertyChanged(nameof(ReviewTotal));
        OnPropertyChanged(nameof(HasRejectRoute));
        OnPropertyChanged(nameof(DiscardButtonText));
    }

    /// <summary>Gözden geçir'de karoya tıklama: listeden (ve işaretten) çıkar.</summary>
    [RelayCommand]
    void Unmark(MonthPhoto? p)
    {
        if (p is null || IsDiscarding) return;
        p.Marked = false;
        if (!IsReview) return;
        if (HasMarked) Replan();
        else IsReview = false;
    }

    [RelayCommand]
    void CancelReview()
    {
        if (IsDiscarding) return;
        IsReview = false;
    }

    // ── Atma ─────────────────────────────────────────────────────

    [ObservableProperty] private bool isDiscarding;
    [ObservableProperty] private string? resultMessage;
    /// <summary>Son atmada atılamayan dosya oldu (sonuç sarı gösterilir).</summary>
    [ObservableProperty] private bool hasFailures;
    [ObservableProperty] private string discardProgress = "";

    /// <summary>Sürmekte olan atma işlemi (pencere kapanışı bekler).</summary>
    public Task? DiscardTask { get; private set; }

    /// <summary>Bu görünüm açıkken atılan bütün dosyalar (RAW'lar dahil); Moved: _Ayiklananlar'a taşındı.</summary>
    public IReadOnlyList<(MediaFile File, bool Moved)> Discarded => _discarded;

    [RelayCommand]
    async Task Discard()
    {
        if (!IsReview || IsDiscarding || _steps.Count == 0) return;
        var steps = _steps;
        var planned = steps.Select(s => s.File).ToHashSet();
        int notPlanned = Photos.Where(p => p.Marked)
            .SelectMany(p => p.KeepRaw ? new[] { p.File } : new[] { p.File }.Concat(p.Raws))
            .Count(f => !planned.Contains(f));
        IsDiscarding = true;
        IProgress<(int done, int total, string current)> ui = new Progress<(int done, int total, string current)>(v =>
            DiscardProgress = $"{v.done:N0} / {v.total:N0} · {v.current}");
        var (root, bin, logDir) = (Root, _s.RecycleBin, _logDir);
        _discardCts = new CancellationTokenSource();
        var ct = _discardCts.Token;
        try
        {
            var task = Task.Run(() => Discarder.Run(steps, root, bin, logDir, ui, ct));
            DiscardTask = task;
            var res = await task;
            Apply(res, notPlanned);
        }
        catch (Exception ex)
        {
            ResultMessage = "Atma yarıda kaldı: " + ex.Message;
        }
        finally
        {
            IsDiscarding = false;
            IsReview = false;
            DiscardProgress = "";
            _steps = new();
        }
    }

    void Apply(DiscardResult res, int notPlanned)
    {
        var done = res.Done.ToDictionary(s => s.File, s => s.Route == DiscardRoute.MoveToRejects);
        foreach (var (f, moved) in done) _discarded.Add((f, moved));

        var keepCurrent = Current;
        int currentIndex = Current is null ? -1 : IndexOf(Current);
        var remaining = Photos.Where(p => !done.ContainsKey(p.File)).ToList();
        foreach (var p in Photos.Where(p => done.ContainsKey(p.File))) p.Marked = false;
        SetPhotos(remaining);

        if (keepCurrent is not null && !remaining.Contains(keepCurrent))
            Current = remaining.Count == 0 ? null : remaining[Math.Min(Math.Max(currentIndex, 0), remaining.Count - 1)];
        if (remaining.Count == 0) IsTriage = false;

        int failed = res.Failed.Count + notPlanned;
        HasFailures = failed > 0;
        var reasons = res.Failed.Select(f => f.Error).Prepend(res.Error).OfType<string>().Distinct().Take(3).ToList();
        ResultMessage = failed == 0
            ? $"{done.Count:N0} dosya atıldı"
            : $"{done.Count:N0} dosya atıldı, {failed:N0} dosya atılamadı" + (reasons.Count > 0 ? ": " + string.Join("; ", reasons) : "");
    }

    /// <summary>Pencere kapanırken: okuma durur, atma sıradaki dosyadan önce durur (o anki dosya yarıda kalmaz).</summary>
    public void RequestStop()
    {
        if (IsLoading) StopCommand.Execute(null);
        _discardCts?.Cancel();
    }

    partial void CheckLeave(ref bool ok)
    {
        if (IsDiscarding) { ok = false; return; }
        if (LeaveQuestion is { } q) ok = _s.Dialogs.Confirm("Ay görünümü", q);
    }
}
