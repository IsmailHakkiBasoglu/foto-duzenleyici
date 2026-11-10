namespace FotoDuzenleyici.ViewModels;

/// <summary>Açılış argümanları: sağ tık "Foto Düzenleyici ile aç" bir klasör verir.</summary>
public static class StartArgs
{
    /// <summary>
    /// <c>--</c> ile başlamayan ve klasör olan ilk argüman; yoksa null.
    /// Explorer sürücü kökünde <c>"D:\"</c> verince sondaki <c>\"</c> kaçış sayılır ve argüman <c>D:"</c> gelir: onarılır.
    /// </summary>
    public static string? Folder(IEnumerable<string> args, Func<string, bool> isDirectory)
    {
        foreach (var raw in args)
        {
            if (raw.StartsWith("--", StringComparison.Ordinal)) continue;
            var p = raw.Trim().TrimEnd('"');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            if (p.Length > 0 && isDirectory(p)) return p;
        }
        return null;
    }
}
