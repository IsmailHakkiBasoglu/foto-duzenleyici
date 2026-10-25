namespace FotoDuzenleyici.Core;

public static class FileClassifier
{
    static readonly HashSet<string> Photo = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".bmp", ".gif", ".tif", ".tiff", ".webp" };
    static readonly HashSet<string> Raw = new(StringComparer.OrdinalIgnoreCase)
        { ".cr2", ".cr3", ".crw", ".raf", ".dng", ".arw", ".nef", ".orf", ".rw2", ".srw" };
    static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".mts", ".m2ts", ".3gp", ".wmv", ".mpg", ".mpeg" };
    static readonly HashSet<string> Document = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".odt" };

    public static FileKind Classify(string path)
    {
        var ext = Path.GetExtension(path);
        if (Photo.Contains(ext)) return FileKind.Photo;
        if (Raw.Contains(ext)) return FileKind.Raw;
        if (Video.Contains(ext)) return FileKind.Video;
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return FileKind.Pdf;
        if (Document.Contains(ext)) return FileKind.Document;
        return FileKind.Other;
    }

    /// <summary>Klasör adı eki: Foto, Raw, Video, Pdf, Belge, Diger.</summary>
    public static string FolderSuffix(FileKind kind) => kind switch
    {
        FileKind.Photo => "Foto",
        FileKind.Raw => "Raw",
        FileKind.Video => "Video",
        FileKind.Pdf => "Pdf",
        FileKind.Document => "Belge",
        _ => "Diger",
    };

    /// <summary>EXIF Make değerini kısa, klasör adına uygun kaynak adına çevirir.</summary>
    public static string? SourceName(string? make, string? model)
    {
        var m = (make ?? "").Trim();
        if (m.Length == 0) return null;
        var u = m.ToUpperInvariant();
        if (u.Contains("FUJI")) return "Fuji";
        if (u.Contains("CANON")) return "Canon";
        if (u.Contains("NIKON")) return "Nikon";
        if (u.Contains("SONY")) return "Sony";
        if (u.Contains("APPLE")) return "iPhone";
        if (u.Contains("SAMSUNG")) return "Samsung";
        if (u.Contains("GOPRO")) return "GoPro";
        if (u.Contains("DJI")) return "DJI";
        return SafeName(System.Globalization.CultureInfo.InvariantCulture.TextInfo
            .ToTitleCase(m.ToLowerInvariant()));
    }

    public static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim().TrimEnd('.');
    }
}
