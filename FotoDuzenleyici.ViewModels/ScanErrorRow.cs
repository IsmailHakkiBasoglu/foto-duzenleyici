namespace FotoDuzenleyici.ViewModels;

/// <summary>
/// Okunamayanlar bölümünde bir satır. Hata metni "yol: neden" biçimindedir; yol taramadaki bir dosyaysa
/// <see cref="Frame"/> küçük resim için doludur. Klasör ve önbellek hatalarında yalnız metin gösterilir.
/// </summary>
public sealed class ScanErrorRow
{
    ScanErrorRow(string text, string? path, string message, SimilarRow? frame)
    {
        Text = text; Path = path; Message = message; Frame = frame;
    }

    public string Text { get; }
    public string? Path { get; }
    public string Message { get; }
    public SimilarRow? Frame { get; }
    public string Name => Frame?.Name ?? Text;

    public static ScanErrorRow Create(string error, IEnumerable<MediaFile> files) =>
        Create(error, files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<ScanErrorRow> CreateAll(IEnumerable<string> errors, IEnumerable<MediaFile> files)
    {
        var byPath = files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return errors.Select(e => Create(e, byPath)).ToList();
    }

    static ScanErrorRow Create(string error, Dictionary<string, MediaFile> byPath)
    {
        // Windows yolunda ": " geçmez (sürücü iki noktasından sonra \ gelir): ilk ": " yolun sonudur.
        var cut = error.IndexOf(": ", StringComparison.Ordinal);
        if (cut <= 0 || !byPath.TryGetValue(error[..cut], out var file))
            return new ScanErrorRow(error, null, error, null);
        return new ScanErrorRow(error, file.Path, error[(cut + 2)..],
            new SimilarRow { Group = 0, File = file, Kind = default });
    }
}
