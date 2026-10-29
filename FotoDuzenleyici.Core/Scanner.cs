using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using Directory = System.IO.Directory;

namespace FotoDuzenleyici.Core;

/// <summary>Salt okunur tarama: dosyaları bulur, EXIF/video metadatasını okur. Hiçbir şeyi değiştirmez.</summary>
public sealed class Scanner
{
    static readonly Regex NameDate = new(
        @"(?<!\d)(20\d{2}|19\d{2})[-_.]?(0[1-9]|1[0-2])[-_.]?(0[1-9]|[12]\d|3[01])(?!\d)",
        RegexOptions.Compiled);

    readonly ScanOptions _opt;
    public Scanner(ScanOptions options) => _opt = options;

    public List<string> Errors { get; } = new();

    public IEnumerable<string> EnumerateFiles()
    {
        var excluded = _opt.ExcludedFolders.Select(Normalize).ToList();
        foreach (var root in _opt.Roots)
            // Kök "C:/x" gibi verilse bile yollar hep "C:\x\..." biçiminde dönsün; yoksa planlayıcı
            // hedefteki dosyaları tanıyamaz ve hepsini yeniden adlandırmaya kalkar.
            foreach (var f in Walk(Normalize(root), excluded))
                yield return f;
    }

    IEnumerable<string> Walk(string dir, List<string> excluded)
    {
        var norm = Normalize(dir);
        if (excluded.Any(e => norm.Equals(e, StringComparison.OrdinalIgnoreCase))) yield break;

        string[] files = Array.Empty<string>(), subs = Array.Empty<string>();
        try { files = Directory.GetFiles(dir); subs = Directory.GetDirectories(dir); }
        catch (Exception ex) { lock (Errors) Errors.Add($"{dir}: {ex.Message}"); }

        foreach (var f in files)
        {
            if (_opt.ExcludedFiles.Contains(f)) continue;
            if (_opt.IncludedKinds.Contains(FileClassifier.Classify(f))) yield return f;
        }
        foreach (var s in subs)
            foreach (var f in Walk(s, excluded))
                yield return f;
    }

    static string Normalize(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');

    public List<MediaFile> Scan(IProgress<(int done, int total)>? progress = null, CancellationToken ct = default)
    {
        var paths = EnumerateFiles().ToList();
        var result = new MediaFile[paths.Count];
        int done = 0;
        Parallel.For(0, paths.Count,
            new ParallelOptions { MaxDegreeOfParallelism = _opt.MaxParallelism, CancellationToken = ct },
            i =>
            {
                result[i] = ReadFile(paths[i]);
                progress?.Report((Interlocked.Increment(ref done), paths.Count));
            });
        return result.ToList();
    }

    public static MediaFile ReadFile(string path)
    {
        var info = new FileInfo(path);
        var kind = FileClassifier.Classify(path);
        var m = new MediaFile
        {
            Path = path,
            Size = info.Length,
            Kind = kind,
            Created = info.CreationTime,
            Modified = info.LastWriteTime,
        };

        if (kind is FileKind.Photo or FileKind.Raw or FileKind.Video)
        {
            try { ReadMetadataInto(m); }
            catch (Exception ex) { m.Error = ex.Message; }
        }

        if (m.Taken is null)
        {
            var nd = DateFromFileName(m.FileName);
            if (nd is not null) { m.Taken = nd; m.DateSource = DateSource.FileName; }
        }
        if (m.Taken is null)
        {
            // Kopyalamalar oluşturma tarihini ileri alabilir; ikisinden eskisi daha güvenilirdir.
            m.Taken = m.Created < m.Modified ? m.Created : m.Modified;
            m.DateSource = DateSource.FileTime;
        }

        if (m.Source is null) m.Source = SourceFromFileName(m.FileName);
        return m;
    }

    static void ReadMetadataInto(MediaFile m)
    {
        using var fs = new FileStream(m.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var dirs = ImageMetadataReader.ReadMetadata(fs);

        foreach (var d in dirs.OfType<ExifIfd0Directory>())
        {
            m.Make ??= d.GetString(ExifDirectoryBase.TagMake)?.Trim();
            m.Model ??= d.GetString(ExifDirectoryBase.TagModel)?.Trim();
        }
        foreach (var d in dirs.OfType<QuickTimeMetadataHeaderDirectory>())
        {
            m.Make ??= d.GetString(QuickTimeMetadataHeaderDirectory.TagMake)?.Trim();
            m.Model ??= d.GetString(QuickTimeMetadataHeaderDirectory.TagModel)?.Trim();
        }

        // Gerçek piksel boyutu JPEG çerçevesindedir; bazı programlar dışa aktarırken EXIF'teki eski boyutu bırakır.
        foreach (var d in dirs.OfType<MetadataExtractor.Formats.Jpeg.JpegDirectory>())
        {
            if (m.Width is null && d.TryGetInt32(MetadataExtractor.Formats.Jpeg.JpegDirectory.TagImageWidth, out var w)) m.Width = w;
            if (m.Height is null && d.TryGetInt32(MetadataExtractor.Formats.Jpeg.JpegDirectory.TagImageHeight, out var h)) m.Height = h;
        }
        foreach (var d in dirs.OfType<ExifSubIfdDirectory>())
        {
            m.SubSec ??= d.GetString(ExifDirectoryBase.TagSubsecondTimeOriginal)?.Trim();
            m.Serial ??= d.GetString(ExifDirectoryBase.TagBodySerialNumber)?.Trim();
            if (m.Width is null && d.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var w)) m.Width = w;
            if (m.Height is null && d.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var h)) m.Height = h;
        }

        foreach (var d in dirs.OfType<ExifSubIfdDirectory>())
        {
            if (d.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) && Valid(dt))
            { m.Taken = dt; m.DateSource = DateSource.Exif; break; }
        }
        if (m.Taken is null)
            foreach (var d in dirs.OfType<ExifSubIfdDirectory>())
                if (d.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dt) && Valid(dt))
                { m.Taken = dt; m.DateSource = DateSource.Exif; break; }
        if (m.Taken is null)
            foreach (var d in dirs.OfType<ExifIfd0Directory>())
                if (d.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt) && Valid(dt))
                { m.Taken = dt; m.DateSource = DateSource.Exif; break; }
        // iPhone gibi cihazlar çekildiği yerin saat dilimiyle birlikte yazar; en doğru kaynak budur.
        if (m.Taken is null)
            foreach (var d in dirs.OfType<QuickTimeMetadataHeaderDirectory>())
                if (ParseOffsetDate(d.GetString(QuickTimeMetadataHeaderDirectory.TagCreationDate)) is { } dt && Valid(dt))
                { m.Taken = dt; m.DateSource = DateSource.VideoMetadata; break; }
        // Film başlığındaki tarih UTC'dir (Canon ve Fuji'de doğrulandı); yerel saate çevrilmezse
        // akşam çekilen videolar önceki güne, ay sonundaysa önceki aya düşer.
        if (m.Taken is null)
            foreach (var d in dirs.OfType<QuickTimeMovieHeaderDirectory>())
            {
                if (!d.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var raw)) continue;
                var dt = UtcToLocal(raw);
                if (Valid(dt)) { m.Taken = dt; m.DateSource = DateSource.VideoMetadata; break; }
            }

        var src = FileClassifier.SourceName(m.Make, m.Model);
        if (src is not null) { m.Source = src; m.SourceFromExif = true; }
    }

    static bool Valid(DateTime dt) => dt.Year >= 1990 && dt <= DateTime.Now.AddDays(2);

    /// <summary>QuickTime film başlığı tarihini (UTC) verilen saat dilimine (varsayılan: bilgisayarınki) çevirir.</summary>
    public static DateTime UtcToLocal(DateTime raw, TimeZoneInfo? zone = null) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(raw, DateTimeKind.Utc), zone ?? TimeZoneInfo.Local);

    /// <summary>"2023-05-01T12:34:56+0300" gibi saat dilimli tarihi çekildiği yerin saatiyle döndürür.</summary>
    public static DateTime? ParseOffsetDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // "+0300" biçimini .NET'in tanıdığı "+03:00" biçimine getir.
        var fixedS = Regex.Replace(s.Trim(), @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(fixedS, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dto) ? dto.DateTime : null;
    }

    public static DateTime? DateFromFileName(string name)
    {
        var mt = NameDate.Match(name);
        if (!mt.Success) return null;
        return DateTime.TryParse($"{mt.Groups[1].Value}-{mt.Groups[2].Value}-{mt.Groups[3].Value}", out var d) && Valid(d)
            ? d : null;
    }

    /// <summary>EXIF'te kaynak yoksa (bazı videolar) dosya adı ön ekinden tahmin. EXIF her zaman önceliklidir.</summary>
    static string? SourceFromFileName(string name)
    {
        var n = name.ToUpperInvariant();
        if (n.StartsWith("DSCF")) return "Fuji";
        if (n.StartsWith("MVI_") || n.StartsWith("_MG_")) return "Canon";
        return null;
    }
}
