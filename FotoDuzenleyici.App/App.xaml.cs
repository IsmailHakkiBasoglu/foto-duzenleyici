using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using FotoDuzenleyici.App.Shell;

namespace FotoDuzenleyici.App;

/// <summary>Açılışta yeni kabuk; <c>--klasik</c> ile doğrudan eski pencere.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Bağlamalardaki sayı ve tarih biçimi (12.458, 214,6 GB) bilgisayarın diliyle.
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        Window w = e.Args.Any(a => a.Equals("--klasik", StringComparison.OrdinalIgnoreCase))
            ? new MainWindow()
            : new ShellWindow();
        MainWindow = w;
        w.Show();
    }
}
