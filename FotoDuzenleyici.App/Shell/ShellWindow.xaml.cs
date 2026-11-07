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
        var running = _vm.RunningTask;
        switch (_vm.RequestClose())
        {
            case CloseDecision.Close:
                base.OnClosing(e);
                return;
            case CloseDecision.Stay:
                e.Cancel = true;
                return;
            case CloseDecision.WaitThenClose:
                // O anki dosya bitirilsin; bekleme sürerken yeniden X'e basılırsa model "Stay" der.
                e.Cancel = true;
                if (running is not null) { try { await running; } catch { } }
                Close();
                return;
        }
    }
}
