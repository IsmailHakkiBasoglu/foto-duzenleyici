using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Pencerenin kendisi: açık sayfa, Geri Al, Klasik görünüm, tema, hata şeridi.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    readonly Services _s;
    readonly AppSettings _settings;

    public ShellViewModel(Services s)
    {
        _s = s;
        _settings = s.Settings.Load();
        if (s.Settings.LoadFailed) errorMessage = "Ayar dosyası okunamadı; varsayılan ayarlarla başlandı.";
        theme = _settings.Theme;
        currentPage = NewHome();
    }

    public AppSettings Settings => _settings;

    [ObservableProperty] private object currentPage;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string theme;

    partial void OnThemeChanged(string value)
    {
        _settings.Theme = value;
        try { _s.Settings.Save(_settings); }
        catch (Exception ex) { ErrorMessage = "Ayarlar kaydedilemedi: " + ex.Message; }
    }

    OrganizeFlowViewModel? Flow => CurrentPage as OrganizeFlowViewModel;

    ILongRunningPage? Long => CurrentPage as ILongRunningPage;

    bool Locked => Long is { IsLocked: true };

    HomeViewModel NewHome()
    {
        var h = new HomeViewModel(_settings);
        h.OrganizeRequested += StartOrganize;
        h.CleanRequested += StartClean;
        h.BackupRequested += () => StartBackup(null);
        return h;
    }

    void StartClean(RecentPair? p)
    {
        var f = new OrganizeFlowViewModel(_s, _settings, FlowKind.Clean);
        if (p is not null) f.Folders.Load(p);
        CurrentPage = f;
    }

    void StartOrganize(RecentPair? p)
    {
        var f = NewOrganize();
        if (p is not null) f.Folders.Load(p);
        CurrentPage = f;
    }

    /// <summary>Düzenle akışı; Bitti ekranındaki Yedekle… düğmesi kabuğa bağlı.</summary>
    OrganizeFlowViewModel NewOrganize()
    {
        var f = new OrganizeFlowViewModel(_s, _settings);
        f.BackupRequested += StartBackup;
        return f;
    }

    /// <param name="archive">Düzenle'nin Bitti ekranından: o işin arşivi; null → son kullanılan.</param>
    public void StartBackup(string? archive)
    {
        if (Locked) return;
        Flow?.FlushReviews();
        CurrentPage = new BackupFlowViewModel(_s, _settings, archive);
    }

    [RelayCommand]
    void GoHome()
    {
        if (Locked) return;
        Flow?.FlushReviews();
        CurrentPage = NewHome();
    }

    [RelayCommand]
    async Task Undo()
    {
        if (Locked) return;
        var initial = _settings.Recent.FirstOrDefault()?.Archive is { } a ? Path.Combine(a, "_Gunluk") : null;
        var journal = _s.Files.OpenJournal(initial);
        if (journal is null) return;
        if (!_s.Dialogs.Confirm("Geri Al", ApplyStepViewModel.UndoText(journal)))
            return;
        // Durdurulmuş bir işin kalanları bırakılır: geri alınan iş sonra "yarım" sanılıp Devam et diye önerilmesin.
        try { if (Executor.Pending(journal).Count > 0) Executor.Abandon(journal); }
        catch (Exception ex) { ErrorMessage = ex.Message; return; }
        var f = NewOrganize();
        CurrentPage = f;
        try { await f.StartUndo(journal); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    /// <summary>Uygulama sürerken açılmaz: eski pencere aynı günlüğü "yarım" sanıp ikinci bir işlem başlatabilir.</summary>
    [RelayCommand]
    void OpenClassic()
    {
        if (Locked) { ErrorMessage = "İşlem sürerken Klasik görünüm açılamaz; bitmesini bekle ya da durdur."; return; }
        ClassicStart? start = null;
        // Klasik pencerede Temizle yok: Temizle akışından boş açılır.
        if (Flow?.Folders is { CanScan: true, Kind: FlowKind.Organize } fo)
            start = new ClassicStart(fo.ExistingSources.ToList(), fo.Archive!, _settings.SourceFolder,
                _settings.RenameWithDate, _settings.AppendSourceToName, _settings.SeparateRawFolder, _settings.RecycleSource);
        // Klasik pencere karar dosyasını açılışta okur: bekleyen kararlar önce yazılsın.
        Flow?.FlushReviews();
        _s.Shell.OpenClassic(start);
    }

    [RelayCommand]
    void DismissError() => ErrorMessage = null;

    bool _waitingToClose;

    /// <summary>
    /// Pencere kapanmadan önce. İş yoksa kapanır. İş sürüyorsa sorar: evet → durdurur ve
    /// <see cref="CloseDecision.WaitThenClose"/> (pencere <see cref="RunningTask"/>'ı bekleyip kapanır); hayır → kalır.
    /// Bekleme sürerken yeniden kapatmak istenirse sorulmaz ve kalınır: o anki dosya yarıda kesilmez.
    /// </summary>
    public CloseDecision RequestClose()
    {
        if (Long is not { MustConfirmClose: true } p)
        {
            _waitingToClose = false;
            Flow?.FlushReviews();
            Long?.RequestStop(); // Yalnız tarama sürüyor olabilir: yazılan bir şey yok, sormadan durur.
            return CloseDecision.Close;
        }
        if (_waitingToClose) return CloseDecision.Stay;
        if (!_s.Dialogs.Confirm("İşlem sürüyor", p.CloseQuestion))
            return CloseDecision.Stay;
        p.RequestStop();
        _waitingToClose = true;
        return CloseDecision.WaitThenClose;
    }

    /// <summary>Sürmekte olan uygulama / geri alma / yedekleme işi; yoksa null.</summary>
    public Task? RunningTask => Long is { MustConfirmClose: true } p ? p.RunningTask : null;
}

public enum CloseDecision { Close, Stay, WaitThenClose }
