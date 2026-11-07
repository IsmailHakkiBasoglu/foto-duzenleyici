using System.ComponentModel;
using System.Windows.Controls;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Kusurlu bölümü. Tam ekrandan dönünce son bakılan kare seçili ve görünür olur.</summary>
public partial class FlawedSection : UserControl
{
    FrameReviewViewModel? _vm;

    public FlawedSection()
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
}
