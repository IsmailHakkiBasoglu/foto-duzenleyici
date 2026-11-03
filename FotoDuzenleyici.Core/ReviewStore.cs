using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Benzer gruplarında kullanıcının kararları: hangi dosya kalsın, hangi grup incelendi. Uygulama kapanınca da
/// hatırlanır. Fotoğraf ya da küçük resim saklanmaz. Dosya kimliği <see cref="HashCache.Key"/>: yoldan bağımsızdır,
/// dosya taşınıp yeniden adlandırılsa da karar bulunur.
/// </summary>
public sealed class ReviewStore
{
    readonly string _path;
    readonly Dictionary<string, bool> _keep = new();
    readonly HashSet<string> _reviewed = new();

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FotoDuzenleyici", "benzer-kararlar.json");

    /// <summary>Dosya vardı ama okunamadı ya da bozuktu; boş başlandı.</summary>
    public bool LoadFailed { get; }

    /// <summary>Kararları yükler; dosya yoksa boş, bozuksa boş başlar ve <see cref="LoadFailed"/> olur.</summary>
    public ReviewStore(string path)
    {
        _path = path;
        try
        {
            if (!File.Exists(path)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? throw new JsonException("boş");
            foreach (var kv in d.Keep ?? new()) _keep[kv.Key] = kv.Value;
            foreach (var s in d.Reviewed ?? new()) _reviewed.Add(s);
        }
        catch (Exception) { _keep.Clear(); _reviewed.Clear(); LoadFailed = true; }
    }

    public bool? GetKeep(string key) => _keep.TryGetValue(key, out var k) ? k : null;
    public void SetKeep(string key, bool keep) => _keep[key] = keep;

    /// <summary>Grubun imzası: üye anahtarları sıralanıp birleştirilir, SHA-256. Üye eklenip çıkınca değişir.</summary>
    public static string Signature(IEnumerable<string> memberKeys) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\n", memberKeys.OrderBy(k => k, StringComparer.Ordinal)))));

    public bool IsReviewed(string signature) => _reviewed.Contains(signature);

    public void SetReviewed(string signature, bool reviewed)
    {
        if (reviewed) _reviewed.Add(signature); else _reviewed.Remove(signature);
    }

    /// <summary>Önce geçici dosyaya yazar, sonra değiştirir: kesinti dosyayı bozmaz. Hata olursa fırlatır.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(new Data(_keep, _reviewed.ToList())));
        File.Move(tmp, _path, overwrite: true);
    }

    sealed record Data(Dictionary<string, bool>? Keep, List<string>? Reviewed);
}
