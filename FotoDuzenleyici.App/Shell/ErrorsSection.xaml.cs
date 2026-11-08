using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

public partial class ErrorsSection : UserControl
{
    public ErrorsSection() => InitializeComponent();

    void Thumb_Loaded(object sender, RoutedEventArgs e) => ThumbTracker.Attach((FrameworkElement)sender);

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Yalnız satırın üstündeki çift tık (kaydırma çubuğu değil).
        if (ItemsControl.ContainerFromElement((ListBox)sender, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: ScanErrorRow row })
            (DataContext as ReviewStepViewModel)?.OpenErrorFileCommand.Execute(row);
    }
}
