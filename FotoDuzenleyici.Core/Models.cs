namespace FotoDuzenleyici.Core;

public enum FileKind { Photo, Raw, Video, Pdf, Document, Other }

public enum DateSource { Exif, VideoMetadata, FileName, FileTime, None }

public sealed class MediaFile
{
    public required string Path { get; init; }
    public long Size { get; init; }
    public FileKind Kind { get; init; }
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }

    /// <summary>Çekim tarihi (EXIF / video / dosya adı / dosya zamanı sırasıyla).</summary>
    public DateTime? Taken { get; set; }
    public DateSource DateSource { get; set; } = DateSource.None;

    /// <summary>EXIF Make alanı (ham).</summary>
    public string? Make { get; set; }
    public string? Model { get; set; }
    /// <summary>EXIF SubSecTimeOriginal: aynı saniyedeki seri çekimleri ayırır.</summary>
    public string? SubSec { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    /// <summary>EXIF BodySerialNumber: aynı model iki gövdeyi ayırır.</summary>
    public string? Serial { get; set; }

    /// <summary>Gösterim adı: Canon, Fuji, ... EXIF yoksa null.</summary>
    public string? Source { get; set; }
    public bool SourceFromExif { get; set; }

    public string? Sha256 { get; set; }
    public string? Error { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

public enum PlanAction
{
    /// <summary>Kopyala + doğrula + kaynağı Geri Dönüşüm Kutusu'na al.</summary>
    Move,
    /// <summary>Başka bir dosyanın birebir kopyası; _Tekrarlar klasörüne alınır.</summary>
    Duplicate,
    /// <summary>Zaten olması gereken yerde.</summary>
    AlreadyInPlace,
    /// <summary>Kullanıcının ayıkladığı "muhtemelen aynı kare"; _Benzerler klasörüne alınır.</summary>
    Similar,
}

public sealed class PlanItem
{
    public required MediaFile File { get; init; }
    public PlanAction Action { get; set; }
    public required string TargetDir { get; set; }
    public required string TargetName { get; set; }
    public string TargetPath => System.IO.Path.Combine(TargetDir, TargetName);
    public MediaFile? DuplicateOf { get; init; }
    public string? Note { get; set; }
    public bool Renamed { get; set; }
}

public enum SourceFolderMode
{
    /// <summary>2026-01-Foto altında Canon + Fuji karışık.</summary>
    None,
    /// <summary>2026-01-Foto\2026-01-Fuji gibi.</summary>
    SubFolder,
}

public sealed class ScanOptions
{
    public List<string> Roots { get; set; } = new();
    /// <summary>Tamamen hariç tutulan klasörler (alt klasörleri dahil).</summary>
    public HashSet<string> ExcludedFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ExcludedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<FileKind> IncludedKinds { get; set; } =
        new() { FileKind.Photo, FileKind.Raw, FileKind.Video, FileKind.Pdf };
    public int MaxParallelism { get; set; } = 4;
}

public sealed class PlanOptions
{
    public required string DestinationRoot { get; set; }
    public SourceFolderMode SourceFolder { get; set; } = SourceFolderMode.None;
    /// <summary>Tüm dosyaların adına "2026-01-15 " öneki ekle. Çakışmada zaten zorunlu.</summary>
    public bool RenameWithDate { get; set; }
    /// <summary>Dosya adının sonuna kaynak (Canon/Fuji) ekle.</summary>
    public bool AppendSourceToName { get; set; }
    public string DuplicatesFolderName { get; set; } = "_Tekrarlar";
    public string SimilarFolderName { get; set; } = "_Benzerler";
    public string UndatedFolderName { get; set; } = "Tarihsiz";
    /// <summary>true ise CR3/RAF gibi RAW'lar -Raw klasörüne, false ise -Foto içine gider.</summary>
    public bool SeparateRawFolder { get; set; } = true;
}
