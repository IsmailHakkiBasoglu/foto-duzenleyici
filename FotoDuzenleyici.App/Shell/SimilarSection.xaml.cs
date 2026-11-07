using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Benzerler bölümü. Karşılaştırmadan dönünce son bakılan grup görünür kalır.</summary>
public partial class SimilarSection : UserControl
{
    FrameReviewViewModel? _vm;

    public SimilarSection()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = e.NewValue as FrameReviewViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FrameReviewViewModel.Selected) && _vm?.Selected is { } g) List.ScrollIntoView(g);
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm?.Selected is not { } g) return;
        e.Handled = true;
        _vm.OpenCompareCommand.Execute(g);
    }
}
