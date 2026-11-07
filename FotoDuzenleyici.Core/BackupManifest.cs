using System.IO;
using System.Text.Json;

namespace FotoDuzenleyici.Core;

/// <summary>Yedekteki bir dosyanın kaydı. <see cref="Path"/> arşiv köküne göre göreli yoldur.</summary>
public sealed record BackupEntry(string Path, long Size, DateTime Modified, string Sha256, DateTime BackedUp);

/// <summary>
/// Yedek kökündeki <c>.fotoduzenleyici-yedek.json</c>: hangi dosyanın hangi içerikle yedeklendiği.
/// Atomik kaydedilir (önce .tmp, sonra yerine taşınır); okunamıyorsa yedek başlamamalı.
/// </summary>
public sealed class BackupManifest
{
    public const string FileName = ".fotoduzenleyici-yedek.json";

    readonly string _path;
    readonly Dictionary<string, BackupEntry> _map = new(StringComparer.OrdinalIgnoreCase);

    BackupManifest(string path) => _path = path;

    public int Count => _map.Count;
    public IReadOnlyCollection<BackupEntry> Entries => _map.Values;

    sealed class FileModel
    {
        public int Version { get; set; } = 1;
        public List<BackupEntry> Entries { get; set; } = new();
    }

    public static BackupManifest Load(string backupRoot)
    {
        var m = new BackupManifest(System.IO.Path.Combine(backupRoot, FileName));
        if (!File.Exists(m._path)) return m;
        FileModel? model;
        try { model = JsonSerializer.Deserialize<FileModel>(File.ReadAllText(m._path)); }
        catch (JsonException ex) { throw new InvalidDataException("Yedek listesi okunamadı: " + ex.Message, ex); }
        if (model is null) throw new InvalidDataException("Yedek listesi okunamadı: boş içerik");
        foreach (var e in model.Entries) m._map[e.Path] = e;
        return m;
    }

    public bool TryGet(string relPath, out BackupEntry entry) => _map.TryGetValue(relPath, out entry!);
    public void Set(BackupEntry entry) => _map[entry.Path] = entry;

    public void Save()
    {
        var tmp = _path + ".tmp";
        var model = new FileModel { Entries = _map.Values.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToList() };
        File.WriteAllText(tmp, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}
