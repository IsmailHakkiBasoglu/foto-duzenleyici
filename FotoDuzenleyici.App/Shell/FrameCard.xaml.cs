using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>
/// Benzerler ve Kusurlu bölümlerinde bir kare. Tek tık kalsın ↔ ayıkla; çift tık karşılaştırmayı açar ve ilk
/// tıklamanın değiştirdiği işareti geri alır (klasikteki davranış).
/// </summary>
public partial class FrameCard : UserControl
{
    static (SimilarRow Row, bool Before)? _lastToggle;

    public FrameCard() => InitializeComponent();

    FrameReviewViewModel? Section
    {
        get
        {
            for (DependencyObject? d = this; d is not null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement { DataContext: FrameReviewViewModel vm }) return vm;
            return null;
        }
    }

    void Card_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 || DataContext is not SimilarRow row || Section is not { } vm) return;
        _lastToggle = (row, row.Keep);
        vm.ToggleCommand.Execute(row);
    }

    void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not SimilarRow row || Section is not { } vm) return;
        e.Handled = true;
        if (_lastToggle is { } t && t.Row == row && row.Keep != t.Before) row.Keep = t.Before;
        _lastToggle = null;
        var group = vm.Groups.FirstOrDefault(g => g.Rows.Contains(row));
        if (group is not null) vm.OpenCompareCommand.Execute(group);
    }

    void Thumb_Loaded(object sender, RoutedEventArgs e) => ThumbTracker.Attach(ThumbHost);
}
