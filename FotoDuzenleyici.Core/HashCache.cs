using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Benzerlik izlerinin önbelleği. Yalnızca iz ve 16×16 gri küçük resim saklanır, görüntü saklanmaz. Anahtar dosya yolundan
/// bağımsızdır: Uygula dosyaları taşıyıp yeniden adlandırır ama zaman damgalarını korur, iz yine bulunur.
/// </summary>
public sealed class HashCache
{
    readonly string _path;
    readonly ConcurrentDictionary<string, ImagePrint> _map = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FotoDuzenleyici", "benzerlik-onbellek.json");

    /// <summary>Önbelleği yükler; dosya yoksa ya da bozuksa boş başlar.</summary>
    public HashCache(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) is { } d)
                foreach (var kv in d) _map[kv.Key] = new ImagePrint(kv.Value.H, Convert.FromBase64String(kv.Value.T));
        }
        catch (Exception) { _map.Clear(); }
    }

    public int Count => _map.Count;

    /// <summary>Boyut + son yazma zamanı (UTC) + ilk 64 KB'nin SHA-256'sı.</summary>
    public static string Key(string filePath)
    {
        var info = new FileInfo(filePath);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[(int)Math.Min(64 * 1024, info.Length)];
        fs.ReadExactly(head);
        return $"{info.Length}:{info.LastWriteTimeUtc.Ticks}:{Convert.ToHexString(SHA256.HashData(head))}";
    }

    public bool TryGet(string key, out ImagePrint print) => _map.TryGetValue(key, out print);
    public void Set(string key, ImagePrint print) => _map[key] = print;

    /// <summary>Önce geçici dosyaya yazar, sonra değiştirir: kesinti önbelleği bozmaz.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        var entries = _map.ToDictionary(kv => kv.Key, kv => new Entry(kv.Value.Hash, Convert.ToBase64String(kv.Value.Thumb)));
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>JSON kaydı: iz ve küçük resim (base64).</summary>
    sealed record Entry(ulong H, string T);
}
