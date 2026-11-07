namespace FotoDuzenleyici.ViewModels;

/// <summary>Bayt sayısının kısa yazımı (12 KB, 340,5 MB, 1,2 GB); bilgisayarın diliyle.</summary>
public static class Sizes
{
    public static string Text(long bytes) => bytes switch
    {
        < 1024 => $"{bytes:N0} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1048576.0:N1} MB",
        _ => $"{bytes / 1073741824.0:N1} GB",
    };
}
