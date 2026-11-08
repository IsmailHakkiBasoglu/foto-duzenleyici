using System.Windows;
using System.Windows.Controls;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

public partial class FoldersPage : UserControl
{
    public FoldersPage() => InitializeComponent();

    void Options_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as OrganizeFlowViewModel)?.Folders is not { } f) return;
        var dlg = new OptionsDialog(f.Settings, f.Kind) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) f.NotifyOptionsChanged();
    }
}
