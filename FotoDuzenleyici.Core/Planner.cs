using System.Text.RegularExpressions;

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
    // Uygulamanın kendi eklediği önek: "yyyy-MM-dd " ya da "yyyy-MM-dd HHmmss ".
    static readonly Regex DatePrefix = new(@"^\d{4}-\d{2}-\d{2} (\d{6} )?", RegexOptions.Compiled);

    /// <param name="similarOf">Kullanıcının ayıkladığı "muhtemelen aynı kare" dosyaları → grupta kalan eşi.</param>
    /// <param name="flawed">Kullanıcının Kusurlu sekmesinde ayıkladığı bulanık / karanlık kareler.</param>
    public static Plan Build(IReadOnlyList<MediaFile> files, IReadOnlyList<DuplicateGroup> dupGroups, PlanOptions opt,
        IReadOnlyDictionary<MediaFile, MediaFile>? similarOf = null, IReadOnlySet<MediaFile>? flawed = null)
    {
        PropagateRawJpgPairs(files);

        var plan = new Plan();
        var dest = Path.GetFullPath(opt.DestinationRoot).TrimEnd('\\');
        var dupDir = Path.Combine(dest, opt.DuplicatesFolderName) + "\\";
        var simDir = Path.Combine(dest, opt.SimilarFolderName) + "\\";
        var flawDir = Path.Combine(dest, opt.FlawedFolderName) + "\\";
        bool InDest(MediaFile f) => Path.GetFullPath(f.Path).StartsWith(dest + "\\", StringComparison.OrdinalIgnoreCase);
        bool Organized(MediaFile f) => InDest(f) && !Path.GetFullPath(f.Path).StartsWith(dupDir, StringComparison.OrdinalIgnoreCase);

        // Tekrar çalıştırmada düzenli klasördeki kopya kalır; dışarıdan gelen eşi _Tekrarlar'a gider.
        // OrderByDescending kararlıdır: düzenli kopya yoksa DuplicateFinder'ın seçimi (en eski) korunur.
        var dupOf = new Dictionary<MediaFile, MediaFile>();
        foreach (var g in dupGroups)
        {
            var members = g.Copies.Prepend(g.Keeper).OrderByDescending(Organized).ToList();
            foreach (var c in members.Skip(1)) dupOf[c] = members[0];
        }

        var scanned = new HashSet<string>(files.Select(f => Path.GetFullPath(f.Path)), StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(dest))
            foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
                if (!scanned.Contains(f)) taken.Add(f);

        PlanAction Route(MediaFile f)
        {
            if (dupOf.ContainsKey(f)) return PlanAction.Duplicate;
            if (similarOf?.ContainsKey(f) == true) return PlanAction.Similar;
            if (flawed?.Contains(f) == true) return PlanAction.Flawed;
            // Kullanıcı önceki turda _Benzerler'e ya da _Kusurlu'ya ayırdıysa orada kalır.
            var full = Path.GetFullPath(f.Path);
            if (full.StartsWith(simDir, StringComparison.OrdinalIgnoreCase) || full.StartsWith(flawDir, StringComparison.OrdinalIgnoreCase))
                return PlanAction.AlreadyInPlace;
            return PlanAction.Move;
        }

        // RAW + JPG eşi aynı yere gidiyorsa (ikisi de düzenli klasöre ya da ikisi de _Benzerler'e)
        // adı birlikte belirlenir: biri çakışırsa ikisi de aynı yeni adı alır. Kopyalar bağımsızdır.
        var pairOf = RawJpgPairs.Index(files);
        var planned = new HashSet<MediaFile>();

        // Hedefte zaten duran dosyalar önce: yerlerini korusunlar.
        foreach (var f in files.OrderByDescending(InDest).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (planned.Contains(f)) continue;
            var route = Route(f);

            if (route == PlanAction.AlreadyInPlace)
            {
                var full = Path.GetFullPath(f.Path);
                taken.Add(full);
                planned.Add(f);
                plan.Items.Add(new PlanItem
                {
                    File = f, TargetDir = Path.GetDirectoryName(full)!, TargetName = Path.GetFileName(full),
                    Action = PlanAction.AlreadyInPlace,
                });
                continue;
            }

            var members = route is PlanAction.Move or PlanAction.Similar or PlanAction.Flawed
                          && pairOf.TryGetValue(f, out var pair) && pair.All(m => !planned.Contains(m) && Route(m) == route)
                ? pair : new List<MediaFile> { f };

            // Eşin tarihi ilk üyeden (fotoğraf): saniye farkıyla ay sınırında bile aynı klasöre düşer.
            var lead = members[0];
            var date = lead.Taken ?? DateTime.MinValue;
            var month = lead.Taken is null ? opt.UndatedFolderName : date.ToString("yyyy-MM");
            var targets = members.Select(m => (
                File: m,
                Dir: route switch
                {
                    PlanAction.Duplicate => Path.Combine(dest, opt.DuplicatesFolderName, month),
                    PlanAction.Similar => Path.Combine(dest, opt.SimilarFolderName, month),
                    PlanAction.Flawed => Path.Combine(dest, opt.FlawedFolderName, month),
                    _ => Path.Combine(dest, month, KindFolder(m, month, opt)),
                },
                InPlace: route == PlanAction.Move && InDest(m))).ToList();

            var (names, collided) = ResolveNames(targets, date, opt, taken);
            if (collided) plan.Collisions += members.Count;

            for (int k = 0; k < targets.Count; k++)
            {
                var m = targets[k].File;
                planned.Add(m);
                dupOf.TryGetValue(m, out var keeper);
                MediaFile? similar = null;
                if (route == PlanAction.Similar) similarOf!.TryGetValue(m, out similar);
                var item = new PlanItem
                {
                    File = m,
                    TargetDir = targets[k].Dir,
                    TargetName = names[k],
                    DuplicateOf = keeper,
                    Renamed = !names[k].Equals(m.FileName, StringComparison.Ordinal),
                    Action = route,
                };
                if (string.Equals(item.TargetPath, Path.GetFullPath(m.Path), StringComparison.OrdinalIgnoreCase))
                    item.Action = PlanAction.AlreadyInPlace;
                if (collided) item.Note = "İsim çakışması: tarih eklendi";
                if (similar is not null) item.Note = "Muhtemelen aynı kare: " + similar.FileName;
                if (route == PlanAction.Flawed) item.Note = collided ? "Kusurlu kare; isim çakışması: tarih eklendi" : "Kusurlu kare";
                if (members.Count > 1 && collided) item.Note += " (RAW+JPG eşiyle birlikte)";
                plan.Items.Add(item);
            }
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

    /// <summary>
    /// Tek dosyaya ya da RAW+JPG eşine ad verir. Eşin üyeleri aynı gövdeyi taşır; gövde ancak her üye
    /// kendi klasöründe boşsa (ya da zaten oradaysa) kabul edilir.
    /// </summary>
    static (List<string> names, bool collided) ResolveNames(
        IReadOnlyList<(MediaFile File, string Dir, bool InPlace)> members, DateTime date, PlanOptions opt, HashSet<string> taken)
    {
        var f = members[0].File;
        var stem = Path.GetFileNameWithoutExtension(f.Path);
        var day = date.ToString("yyyy-MM-dd");
        // Önceki turda eklenmiş kaynak ("... Fuji" ya da "... Fuji (2)") tekrar eklenmez.
        bool hasSrc = f.Source is not null &&
            Regex.IsMatch(stem, $@" {Regex.Escape(f.Source)}( \(\d+\))?$", RegexOptions.IgnoreCase);
        var src = opt.AppendSourceToName && f.Source is not null && !hasSrc ? " " + f.Source : "";

        string Compose(bool dated, bool withTime, int n = 0)
        {
            // Tarih sonradan düzelirse (ör. video UTC düzeltmesi) eski önek yenisiyle değiştirilir;
            // yoksa "2023-03-20 2023-03-19 MVI_3028" gibi çift tarih oluşur.
            var body = dated && !stem.StartsWith(day) ? DatePrefix.Replace(stem, "") : stem;
            var prefix = dated && !stem.StartsWith(day) ? (withTime ? $"{day} {date:HHmmss} " : day + " ") : "";
            var suffix = n > 0 ? $" ({n})" : "";
            return FileClassifier.SafeName($"{prefix}{body}{src}{suffix}");
        }

        string Full(int k, string body) => Path.Combine(members[k].Dir, body + Path.GetExtension(members[k].File.Path));
        bool IsSelf(int k, string full) => full.Equals(Path.GetFullPath(members[k].File.Path), StringComparison.OrdinalIgnoreCase);

        bool Free(string body) => Enumerable.Range(0, members.Count).All(k =>
        {
            var full = Full(k, body);
            return !taken.Contains(full) && !(File.Exists(full) && !IsSelf(k, full));
        });

        // İlk aday: her üye boşsa ya da zaten tam o adda duruyorsa kabul.
        bool FirstOk(string body) => Enumerable.Range(0, members.Count).All(k =>
        {
            var full = Full(k, body);
            return (!taken.Contains(full) && !(File.Exists(full) && !IsSelf(k, full))) || (members[k].InPlace && IsSelf(k, full));
        });

        var candidate = Compose(opt.RenameWithDate, false);
        bool collided = false;

        if (!FirstOk(candidate))
        {
            collided = true;
            // Çakışmada tarih zorunlu; sonra saat; en son sayaç.
            candidate = Compose(true, false);
            if (!Free(candidate)) candidate = Compose(true, true);
            for (int n = 2; !Free(candidate); n++) candidate = Compose(true, true, n);
        }
        var names = new List<string>();
        for (int k = 0; k < members.Count; k++)
        {
            taken.Add(Full(k, candidate));
            names.Add(Path.GetFileName(Full(k, candidate)));
        }
        return (names, collided);
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
