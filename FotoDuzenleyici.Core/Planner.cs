namespace FotoDuzenleyici.Core;

public sealed class Plan
{
    public List<PlanItem> Items { get; } = new();
    public int Collisions { get; set; }
    public int Renamed => Items.Count(i => i.Renamed);
}

/// <summary>Hiçbir dosyaya dokunmaz; sadece "şu dosya şuraya gidecek" planını üretir.</summary>
public static class Planner
{
    public static Plan Build(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups, PlanOptions opt)
    {
        PropagateRawJpgPairs(files);

        var plan = new Plan();
        var dupOf = new Dictionary<MediaFile, MediaFile>();
        foreach (var g in dupGroups) foreach (var c in g.Copies) dupOf[c] = g.Keeper;

        var dest = Path.GetFullPath(opt.DestinationRoot).TrimEnd('\\');
        var scanned = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(dest))
            foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
                if (!scanned.Contains(f)) taken.Add(f);

        bool InDest(MediaFile f) => f.Path.StartsWith(dest + "\\", StringComparison.OrdinalIgnoreCase);

        // Hedefte zaten duran dosyalar önce: yerlerini korusunlar.
        foreach (var f in files.OrderByDescending(InDest).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            var date = f.Taken ?? DateTime.MinValue;
            var month = f.Taken is null ? opt.UndatedFolderName : date.ToString("yyyy-MM");
            dupOf.TryGetValue(f, out var keeper);

            string dir;
            if (keeper is not null)
                dir = Path.Combine(dest, opt.DuplicatesFolderName, month);
            else
                dir = Path.Combine(dest, month, KindFolder(f, month, opt));

            var (name, renamed, collided) = ResolveName(f, date, dir, opt, taken, keeper is null && InDest(f));
            if (collided) plan.Collisions++;

            var item = new PlanItem
            {
                File = f,
                TargetDir = dir,
                TargetName = name,
                DuplicateOf = keeper,
                Renamed = renamed,
                Action = keeper is not null ? PlanAction.Duplicate : PlanAction.Move,
            };
            if (string.Equals(item.TargetPath, f.Path, StringComparison.OrdinalIgnoreCase))
                item.Action = PlanAction.AlreadyInPlace;
            if (collided) item.Note = "İsim çakışması: tarih eklendi";
            plan.Items.Add(item);
        }
        return plan;
    }

    static string KindFolder(MediaFile f, string month, PlanOptions opt)
    {
        var kind = f.Kind == FileKind.Raw && !opt.SeparateRawFolder ? FileKind.Photo : f.Kind;
        var kf = $"{month}-{FileClassifier.FolderSuffix(kind)}";
        bool hasCamera = f.Kind is FileKind.Photo or FileKind.Raw or FileKind.Video;
        if (opt.SourceFolder == SourceFolderMode.SubFolder && hasCamera)
            return Path.Combine(kf, $"{month}-{f.Source ?? "Bilinmeyen"}");
        return kf;
    }

    static (string name, bool renamed, bool collided) ResolveName(MediaFile f, DateTime date, string dir,
        PlanOptions opt, HashSet<string> taken, bool inPlaceCandidate)
    {
        var ext = Path.GetExtension(f.Path);
        var stem = Path.GetFileNameWithoutExtension(f.Path);
        var day = date.ToString("yyyy-MM-dd");
        var src = opt.AppendSourceToName && f.Source is not null ? " " + f.Source : "";

        string Compose(bool dated, bool withTime, int n = 0)
        {
            var prefix = dated && !stem.StartsWith(day) ? (withTime ? $"{day} {date:HHmmss} " : day + " ") : "";
            var suffix = n > 0 ? $" ({n})" : "";
            return FileClassifier.SafeName($"{prefix}{stem}{src}{suffix}") + ext;
        }

        bool Free(string n)
        {
            var full = Path.Combine(dir, n);
            return !taken.Contains(full) && !(File.Exists(full) && !full.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
        }

        bool alreadyDated = opt.RenameWithDate;
        var candidate = Compose(alreadyDated, false);
        bool collided = false;

        if (!Free(candidate) && !(inPlaceCandidate && Path.Combine(dir, candidate).Equals(f.Path, StringComparison.OrdinalIgnoreCase)))
        {
            collided = true;
            // Çakışmada tarih zorunlu; sonra saat; en son sayaç.
            candidate = Compose(true, false);
            if (!Free(candidate)) candidate = Compose(true, true);
            for (int n = 2; !Free(candidate); n++) candidate = Compose(true, true, n);
        }
        taken.Add(Path.Combine(dir, candidate));
        return (candidate, !candidate.Equals(Path.GetFileName(f.Path), StringComparison.Ordinal), collided);
    }

    /// <summary>EXIF'i okunamayan RAW/JPG eşini, aynı klasör ve aynı adlı eşinden gelen tarih/kaynakla eşitler.</summary>
    static void PropagateRawJpgPairs(IReadOnlyList<MediaFile> files)
    {
        foreach (var g in files.Where(f => f.Kind is FileKind.Photo or FileKind.Raw)
                     .GroupBy(f => Path.Combine(Path.GetDirectoryName(f.Path)!, Path.GetFileNameWithoutExtension(f.Path)),
                         StringComparer.OrdinalIgnoreCase))
        {
            var good = g.FirstOrDefault(f => f.DateSource == DateSource.Exif);
            if (good is null) continue;
            foreach (var f in g.Where(f => f.DateSource != DateSource.Exif))
            {
                f.Taken = good.Taken; f.DateSource = DateSource.Exif;
                if (!f.SourceFromExif && good.Source is not null) f.Source = good.Source;
            }
        }
    }
}
