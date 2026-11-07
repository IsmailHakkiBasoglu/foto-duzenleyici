using System.Windows;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Wpf.Ui.Controls;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Düzen seçenekleri; Kaydet ile ayarlara yazılır.</summary>
public partial class OptionsDialog : FluentWindow
{
    readonly AppSettings _s;

    public OptionsDialog(AppSettings s)
    {
        InitializeComponent();
        _s = s;
        SubFolder.IsChecked = s.SourceFolder == SourceFolderMode.SubFolder;
        RenameWithDate.IsChecked = s.RenameWithDate;
        AppendSource.IsChecked = s.AppendSourceToName;
        RawSeparate.IsChecked = s.SeparateRawFolder;
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        _s.SourceFolder = SubFolder.IsChecked == true ? SourceFolderMode.SubFolder : SourceFolderMode.None;
        _s.RenameWithDate = RenameWithDate.IsChecked == true;
        _s.AppendSourceToName = AppendSource.IsChecked == true;
        _s.SeparateRawFolder = RawSeparate.IsChecked == true;
        DialogResult = true;
    }
}
