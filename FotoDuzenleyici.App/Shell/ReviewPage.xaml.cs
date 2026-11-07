using System.ComponentModel;
using System.Windows.Controls;
using FotoDuzenleyici.ViewModels;

namespace FotoDuzenleyici.App.Shell;

public partial class ReviewPage : UserControl
{
    OrganizeFlowViewModel? _flow;

    public ReviewPage()
    {
        InitializeComponent();
        // Yeni tarama: önceki incelemenin küçük resimleri bırakılır.
        DataContextChanged += (_, e) =>
        {
            if (_flow is not null) _flow.PropertyChanged -= OnFlowChanged;
            _flow = e.NewValue as OrganizeFlowViewModel;
            if (_flow is not null) _flow.PropertyChanged += OnFlowChanged;
        };
    }

    void OnFlowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrganizeFlowViewModel.Review)) ThumbTracker.Clear();
    }
}
