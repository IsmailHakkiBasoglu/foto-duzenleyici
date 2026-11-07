using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FotoDuzenleyici.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FotoDuzenleyici.App.Shell;

public partial class ShellWindow : FluentWindow
{
    readonly ShellViewModel _vm;
    bool _closeWhenDone;

    public ShellWindow()
    {
        InitializeComponent();
        _vm = new ShellViewModel(WpfServices.Create());
        DataContext = _vm;
        AboutItem.Header = "Sürüm " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.Theme)) ApplyTheme(); };
        Loaded += (_, _) => ApplyTheme();
    }

    void ApplyTheme()
    {
        SystemThemeWatcher.UnWatch(this);
        switch (_vm.Theme)
        {
            case "Koyu": ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica); break;
            case "Açık": ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica); break;
            default:
                ApplicationThemeManager.Apply(ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.Mica);
                SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
                break;
        }
    }

    void Menu_Click(object sender, RoutedEventArgs e)
    {
        var cm = MenuButton.ContextMenu!;
        cm.PlacementTarget = MenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    void Theme_Click(object sender, RoutedEventArgs e) => _vm.Theme = (string)((System.Windows.Controls.MenuItem)sender).Tag;

    /// <summary>Uygulama sürüyorsa önce sorulur; durdurulursa iş bitince pencere kendiliğinden kapanır.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closeWhenDone || _vm.CanClose()) { base.OnClosing(e); return; }
        e.Cancel = true;
        // Kullanıcı "Durdur" dediyse belirteç tetiklendi; iş bitince kapat.
        if (_vm.RunningTask is { } t && IsStopping())
        {
            _closeWhenDone = true;
            try { await t; } catch { }
            Close();
        }
    }

    bool IsStopping() =>
        _vm.CurrentPage is OrganizeFlowViewModel { Apply: { } a } && a.IsCancellationRequested;
}
