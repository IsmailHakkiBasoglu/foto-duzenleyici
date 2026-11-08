using System.Windows;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Wpf.Ui.Controls;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Düzen seçenekleri; Kaydet ile ayarlara yazılır.</summary>
public partial class OptionsDialog : FluentWindow
{
    readonly AppSettings _s;

    /// <param name="kind">Temizle: yalnız tarama seçenekleri (tarih düzeni yok).</param>
    public OptionsDialog(AppSettings s, FlowKind kind = FlowKind.Organize)
    {
        InitializeComponent();
        if (kind == FlowKind.Clean)
            foreach (var c in new FrameworkElement[] { LayoutNote, SubFolder, RenameWithDate, AppendSource, RawSeparate })
                c.Visibility = Visibility.Collapsed;
        _s = s;
        SubFolder.IsChecked = s.SourceFolder == SourceFolderMode.SubFolder;
        RenameWithDate.IsChecked = s.RenameWithDate;
        AppendSource.IsChecked = s.AppendSourceToName;
        RawSeparate.IsChecked = s.SeparateRawFolder;
        FindSimilar.IsChecked = s.FindSimilar;
        MarkFlawed.IsChecked = s.MarkFlawed;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _s.SourceFolder = SubFolder.IsChecked == true ? SourceFolderMode.SubFolder : SourceFolderMode.None;
        _s.RenameWithDate = RenameWithDate.IsChecked == true;
        _s.AppendSourceToName = AppendSource.IsChecked == true;
        _s.SeparateRawFolder = RawSeparate.IsChecked == true;
        _s.FindSimilar = FindSimilar.IsChecked == true;
        _s.MarkFlawed = MarkFlawed.IsChecked == true;
        DialogResult = true;
    }
}
