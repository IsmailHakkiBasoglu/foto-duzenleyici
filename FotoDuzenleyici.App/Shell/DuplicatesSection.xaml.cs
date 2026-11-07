using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Yinelenenler bölümü: korunacak konum seçimi. Çift tık dosyayı Windows'ta açar.</summary>
public partial class DuplicatesSection : UserControl
{
    public DuplicatesSection() => InitializeComponent();

    void Row_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not DuplicateRow row) return;
        e.Handled = true;
        (DataContext as DuplicatesReviewViewModel)?.OpenFileCommand.Execute(row);
    }

    /// <summary>Küçük resim: incelemenin ortak yükleyicisi (bkz. <see cref="ThumbTracker"/>).</summary>
    void Thumb_Loaded(object sender, RoutedEventArgs e) => ThumbTracker.Attach((FrameworkElement)sender);
}
