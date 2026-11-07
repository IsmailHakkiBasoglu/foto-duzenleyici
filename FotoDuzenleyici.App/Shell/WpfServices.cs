using System.Diagnostics;
using System.IO;
using System.Windows;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Microsoft.Win32;

namespace FotoDuzenleyici.App.Shell;

/// <summary>Hizmet arayüzlerinin WPF uygulamaları.</summary>
sealed class WpfServices : IFolderPicker, IFilePicker, IDialogs, IShell
{
    static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public string? PickFolder(string title, string? initial = null)
    {
        var d = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) d.InitialDirectory = initial;
        return d.ShowDialog(Owner) == true ? d.FolderName : null;
    }

    public string? OpenJournal(string? initialDir)
    {
        var d = new OpenFileDialog { Title = "Geri alınacak işlem günlüğünü seç", Filter = "Günlük (*.jsonl)|*.jsonl" };
        if (initialDir is not null && Directory.Exists(initialDir)) d.InitialDirectory = initialDir;
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public string? SaveCsv(string suggestedName)
    {
        var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = suggestedName };
        return d.ShowDialog(Owner) == true ? d.FileName : null;
    }

    public bool Confirm(string title, string message)
    {
        var o = Owner;
        var r = o is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(o, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return r == MessageBoxResult.Yes;
    }

    public void Info(string title, string message)
    {
        var o = Owner;
        if (o is null) MessageBox.Show(message, title);
        else MessageBox.Show(o, message, title);
    }

    public void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Info("Dosya açılamadı", ex.Message); }
    }

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public void OpenClassic(ClassicStart? start)
    {
        var w = new MainWindow();
        w.Show();
        if (start is not null) w.StartFrom(start);
    }

    public static long FreeSpace(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
    }

    public static Services Create()
    {
        var w = new WpfServices();
        return new Services(w, w, w, w, new JsonSettingsStore(JsonSettingsStore.DefaultPath), FreeSpace,
            Schedule: Later, Compare: new WpfCompare());
    }

    /// <summary>Arayüz iş parçacığında, gecikmeyle tek sefer. Dispose bekleyeni iptal eder.</summary>
    static IDisposable Later(TimeSpan delay, Action work)
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = delay };
        t.Tick += (_, _) => { t.Stop(); work(); };
        t.Start();
        return new Stopper(t);
    }

    sealed class Stopper(System.Windows.Threading.DispatcherTimer t) : IDisposable { public void Dispose() => t.Stop(); }
}

/// <summary>Klasikteki karşılaştırma penceresi, yeni kabuğun üstünde kalıcı (modal).</summary>
sealed class WpfCompare : ICompare
{
    public (SimilarGroupState Current, bool FinishedAll) Open(IReadOnlyList<SimilarGroupState> groups, int start, ReviewStore store, Action changed)
    {
        var w = new CompareWindow(groups, start, store, changed) { Owner = Application.Current?.MainWindow };
        w.ShowDialog();
        return (w.Current, w.FinishedAll);
    }
}
