using System.Collections.Concurrent;
using System.Text.Json;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Netlik/karanlık ölçümlerinin önbelleği; görüntü saklanmaz. Anahtar <see cref="HashCache.Key"/>: dosya taşınıp
/// yeniden adlandırılsa da ölçüm bulunur.
/// </summary>
public sealed class QualityCache
{
    readonly string _path;
    readonly ConcurrentDictionary<string, QualityScore> _map = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FotoDuzenleyici", "kalite-onbellek.json");

    /// <summary>Önbelleği yükler; dosya yoksa ya da bozuksa boş başlar.</summary>
    public QualityCache(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) is { } d)
                foreach (var kv in d) _map[kv.Key] = new QualityScore(kv.Value.S, kv.Value.L, kv.Value.D, kv.Value.F);
        }
        catch (Exception) { _map.Clear(); }
    }

    public int Count => _map.Count;
    public bool TryGet(string key, out QualityScore score) => _map.TryGetValue(key, out score);
    public void Set(string key, QualityScore score) => _map[key] = score;

    /// <summary>Önce geçici dosyaya yazar, sonra değiştirir: kesinti önbelleği bozmaz.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        var entries = _map.ToDictionary(kv => kv.Key, kv => new Entry(kv.Value.Sharpness, kv.Value.MeanLuma, kv.Value.DarkFraction, kv.Value.Flat));
        File.WriteAllText(tmp, JsonSerializer.Serialize(entries));
        File.Move(tmp, _path, overwrite: true);
    }

    /// <summary>JSON kaydı: netlik, ortalama parlaklık, karanlık oranı, düz sahne.</summary>
    sealed record Entry(double S, double L, double D, bool F);
}
