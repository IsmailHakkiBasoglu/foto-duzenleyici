using System.Globalization;
using System.Text;

namespace FotoDuzenleyici.Core;

/// <summary>Plan sekmesindeki önizleme paneli: satırın bilgi metni ve gösterilecek görüntü. Dosyalara dokunmaz.</summary>
public static class PlanPreview
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Plan tablosunun "Eylem" sütunu.</summary>
    public static string ActionName(PlanAction a) => a switch
    {
        PlanAction.Move => "Taşı",
        PlanAction.Duplicate => "Kopya",
        PlanAction.AlreadyInPlace => "Yerinde",
        PlanAction.Similar => "Benzer",
        PlanAction.Flawed => "Kusurlu",
        _ => a.ToString(),
    };

    static string Verb(PlanAction a) => a switch
    {
        PlanAction.Move => "Taşınacak",
        PlanAction.Duplicate => "Kopya, ayrılacak",
        PlanAction.Similar => "Benzer, ayrılacak",
        PlanAction.Flawed => "Kusurlu, ayrılacak",
        _ => a.ToString(),
    };

    /// <param name="score">Netlik ölçülmüşse.</param>
    /// <param name="pair">RAW+JPG eşinin öbür üyesi, varsa.</param>
    public static string Describe(PlanItem item, QualityScore? score, MediaFile? pair)
    {
        var f = item.File;
        var lines = new List<string> { Path.GetFileName(f.Path) };

        var size = (f.Size / 1048576.0).ToString("0.0", Tr) + " MB";
        lines.Add(f.Width is { } w && f.Height is { } h ? $"{size} · {w}×{h}" : size);

        var date = f.Taken?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Tarihsiz";
        var camera = f.Model ?? f.Source;
        lines.Add(string.IsNullOrEmpty(camera) ? date : $"{date} · {camera}");

        if (item.Action == PlanAction.AlreadyInPlace) lines.Add("Zaten yerinde, değişmeyecek");
        else { lines.Add(Verb(item.Action) + " →"); lines.Add(item.TargetPath); }

        if (item.DuplicateOf is { } keeper) lines.Add("Korunan: " + keeper.Path);

        if (score is { } s)
        {
            var q = $"Netlik {Math.Round(s.Sharpness, MidpointRounding.AwayFromZero)} · Parlaklık {Math.Round(s.MeanLuma, MidpointRounding.AwayFromZero)}";
            if (QualityRules.FlawText(s) is { } flaw) q += " · " + flaw;
            lines.Add(q);
        }

        if (pair is not null) lines.Add((pair.Kind == FileKind.Raw ? "RAW eşi: " : "JPG eşi: ") + Path.GetFileName(pair.Path));
        if (!string.IsNullOrEmpty(item.Note)) lines.Add("Not: " + item.Note);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Önizlemede gösterilecek görüntü ve altına yazılacak not. Video ve PDF çözülmez. RAW'ı Windows açamazsa
    /// (codec yoksa) <paramref name="photoPair"/> gösterilir.
    /// </summary>
    public static (T? Image, string? Note) Load<T>(MediaFile file, MediaFile? photoPair, Func<MediaFile, T?> decode) where T : class
    {
        switch (file.Kind)
        {
            case FileKind.Video: return (null, "Önizleme yok — video");
            case FileKind.Pdf: return (null, "Önizleme yok — PDF");
            case FileKind.Photo: return decode(file) is { } img ? (img, null) : (null, "Görüntü açılamadı");
            case FileKind.Raw:
                if (decode(file) is { } raw) return (raw, null);
                if (photoPair is { Kind: FileKind.Photo } && decode(photoPair) is { } jpg) return (jpg, "JPG eşi gösteriliyor");
                return (null, "RAW açılamadı (Windows'ta bu RAW için codec yok)");
            default: return (null, "Önizleme yok");
        }
    }
}
