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
        _s.Settings.Save(_settings);
    }

    OrganizeFlowViewModel? Flow => CurrentPage as OrganizeFlowViewModel;

    bool Locked => Flow is { } f && (f.NavigationLocked || f.IsBusy);

    HomeViewModel NewHome()
    {
        var h = new HomeViewModel(_settings);
        h.OrganizeRequested += StartOrganize;
        return h;
    }

    void StartOrganize(RecentPair? p)
    {
        var f = new OrganizeFlowViewModel(_s, _settings);
        if (p is not null) f.Folders.Load(p);
        CurrentPage = f;
    }

    [RelayCommand]
    void GoHome()
    {
        if (Locked) return;
        CurrentPage = NewHome();
    }

    [RelayCommand]
    async Task Undo()
    {
        if (Locked) return;
        var initial = _settings.Recent.FirstOrDefault()?.Archive is { } a ? Path.Combine(a, "_Gunluk") : null;
        var journal = _s.Files.OpenJournal(initial);
        if (journal is null) return;
        if (!_s.Dialogs.Confirm("Geri Al",
                "Bu günlükteki dosyalar doğrulanarak eski yerlerine döner; arşivdeki kopyaları Geri Dönüşüm Kutusu'na gider. Devam edilsin mi?"))
            return;
        var f = new OrganizeFlowViewModel(_s, _settings);
        CurrentPage = f;
        try { await f.StartUndo(journal); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    void OpenClassic() => _s.Shell.OpenClassic();

    [RelayCommand]
    void DismissError() => ErrorMessage = null;

    /// <summary>
    /// Pencere kapanmadan önce. Uygulama sürüyorsa sorar; evet → durdurur ve false döner (pencere, iş bitince
    /// <see cref="RunningTask"/>'ı bekleyip yeniden kapatır). Hayır → false.
    /// </summary>
    public bool CanClose()
    {
        if (Flow is not { NavigationLocked: true } f) return true;
        if (_s.Dialogs.Confirm("İşlem sürüyor",
                "Dosyalar hâlâ işleniyor. Durdurulsun mu? O anki dosya bitirilir, kalanlar sonra devam ettirilebilir."))
            f.Apply?.CancelCommand.Execute(null);
        return false;
    }

    /// <summary>Sürmekte olan uygulama / geri alma işi; yoksa null.</summary>
    public Task? RunningTask => Flow is { NavigationLocked: true } f ? f.ApplyTask : null;
}
