using System.Text.Json;

namespace FotoDuzenleyici.ViewModels;

public sealed record RecentPair(List<string> Sources, string Archive, DateTime Used);

/// <summary>Uygulama ayarları: son kullanılan klasörler, düzen seçenekleri, tema, Geri Dönüşüm seçimi.</summary>
public sealed class AppSettings
{
    public const int MaxRecent = 5;

    public List<RecentPair> Recent { get; set; } = new();
    public SourceFolderMode SourceFolder { get; set; } = SourceFolderMode.None;
    public bool RenameWithDate { get; set; }
    public bool AppendSourceToName { get; set; }
    public bool SeparateRawFolder { get; set; } = true;
    /// <summary>Sistem | Açık | Koyu</summary>
    public string Theme { get; set; } = "Sistem";
    public bool RecycleSource { get; set; } = true;

    /// <summary>Çifti başa ekler. Aynı kaynak kümesi + arşiv (sıra, sondaki ayıraç ve harf büyüklüğü fark etmez) tek kayıttır.</summary>
    public void AddRecent(IEnumerable<string> sources, string archive, DateTime now)
    {
        var list = sources.ToList();
        var key = Key(list, archive);
        Recent.RemoveAll(r => Key(r.Sources, r.Archive) == key);
        Recent.Insert(0, new RecentPair(list, archive, now));
        if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
    }

    static string Key(IEnumerable<string> sources, string archive) =>
        string.Join("|", sources.Select(Norm).OrderBy(s => s, StringComparer.Ordinal)) + ">" + Norm(archive);

    static string Norm(string p) => p.Trim().TrimEnd('\\', '/').ToUpperInvariant();
}

/// <summary>Ayarlar bir JSON dosyasında; kayıt atomiktir (.tmp üzerinden).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    readonly string _path;
    public JsonSettingsStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FotoDuzenleyici", "ayarlar.json");

    public bool LoadFailed { get; private set; }

    public AppSettings Load()
    {
        LoadFailed = false;
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? throw new JsonException("boş");
            s.Recent ??= new();
            s.Recent.RemoveAll(r => r is null || r.Sources is null || r.Archive is null);
            s.Theme ??= "Sistem";
            return s;
        }
        catch (Exception) { LoadFailed = true; return new AppSettings(); }
    }

    public void Save(AppSettings s)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
