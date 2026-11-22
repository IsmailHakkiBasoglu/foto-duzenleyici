namespace FotoDuzenleyici.Core;

/// <summary>
/// Katalogdaki bir dosya. <paramref name="Folder"/>: klasör adlarından okumada ay klasörünün tam yolu, çekim
/// tarihinden okumada dosyanın doğrudan klasörü (<see cref="TimelineEntry.Folder"/> ile aynı anlam).
/// <paramref name="OtherMonth"/>: klasör adlarından okumada çekim tarihi yok ya da başka bir aya düşüyor.
/// </summary>
public sealed record CatalogItem(MediaFile File, int Year, int Month, string Folder, bool OtherMonth);

/// <summary>
/// Bir kökün bütün medya dosyaları, bir kez okunmuş hâliyle. Uygulama açık kaldıkça bellekte tutulur; zaman
/// çizelgesi tablosu, galeri ve ay görünümü bunu kullanır. Salt okunur: hiçbir dosyaya dokunmaz.
/// </summary>
public sealed class MediaCatalog
{
    readonly List<CatalogItem> _items;

    internal MediaCatalog(string root, TimelineMethod method, List<CatalogItem> items, TimelineResult scratch)
    {
        Root = root;
        Method = method;
        _items = items;
        Errors = scratch.Errors;
        Special = scratch.Special;
        Outside = scratch.Outside;
        Cancelled = scratch.Cancelled;
    }

    public string Root { get; }
    public TimelineMethod Method { get; }
    public IReadOnlyList<CatalogItem> Items => _items;
    /// <summary>"yol: ileti" — okunamayan klasör ve dosyalar.</summary>
    public List<string> Errors { get; }
    public SortedDictionary<string, int> Special { get; }
    public int Outside { get; }
    public bool Cancelled { get; }

    /// <summary>Tablonun girdisi: bugünkü sayımın sonucu biçiminde.</summary>
    public TimelineResult ToResult()
    {
        var r = new TimelineResult { Root = Root, Method = Method, Outside = Outside, Cancelled = Cancelled };
        foreach (var i in _items)
            r.Entries.Add(new TimelineEntry(i.Year, i.Month, i.File.Kind, i.File.Size, i.Folder, i.File.DateSource));
        foreach (var (k, v) in Special) r.Special[k] = v;
        r.Errors.AddRange(Errors);
        return r;
    }

    public IReadOnlyList<MediaFile> FilesOf(int year, int month) =>
        _items.Where(i => i.Year == year && i.Month == month).Select(i => i.File).ToList();

    /// <summary>Uygulamanın kendisinin taşıdığı / attığı dosyaları düşer. Dönen: düşülen öğe sayısı.</summary>
    public int Remove(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths.Select(Full), StringComparer.OrdinalIgnoreCase);
        return _items.RemoveAll(i => set.Contains(Full(i.File.Path)));
    }

    static string Full(string p) => Path.GetFullPath(p);
}

/// <summary>Kataloğu okur: her medya dosyası bir kez, 4 iş parçacığıyla. Salt okunur.</summary>
public static class CatalogReader
{
    public static MediaCatalog Read(string root, TimelineMethod method, IProgress<(int done, int total)>? progress = null,
        CancellationToken ct = default, Func<string, MediaFile>? read = null)
    {
        read ??= Scanner.ReadFile;
        var scratch = new TimelineResult { Root = root, Method = method };
        var items = new List<CatalogItem>();
        try
        {
            // (yol, klasör adındaki yıl/ay ya da 0, ay klasörü ya da null)
            var paths = method == TimelineMethod.FolderNames
                ? Timeline.FolderPaths(root, scratch, ct).Select(x => (x.Path, x.Year, x.Month, (string?)x.Folder)).ToList()
                : Timeline.CapturePaths(root, scratch, ct).Select(p => (Path: p, Year: 0, Month: 0, Folder: (string?)null)).ToList();
            var found = new CatalogItem?[paths.Count];
            int done = 0;
            Parallel.For(0, paths.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, i =>
            {
                var (path, y, m, folder) = paths[i];
                MediaFile? f = null;
                try { f = read(path); }
                catch (Exception ex)
                {
                    lock (scratch.Errors) scratch.Errors.Add($"{path}: {ex.Message}");
                    // Klasör adlarında okunamayan dosya da sayılır: tablo bugünkü sayımla aynı kalsın.
                    if (folder is not null)
                        f = new MediaFile { Path = path, Size = Timeline.Length(path, scratch), Kind = FileClassifier.Classify(path) };
                }
                if (f is not null)
                {
                    if (folder is not null)
                        found[i] = new CatalogItem(f, y, m, folder, f.Taken is not { } t || t.Year != y || t.Month != m);
                    else if (f.Taken is { } t)
                        found[i] = new CatalogItem(f, t.Year, t.Month, Path.GetDirectoryName(path) ?? root, false);
                }
                progress?.Report((Interlocked.Increment(ref done), paths.Count));
            });
            items.AddRange(found.OfType<CatalogItem>());
        }
        catch (OperationCanceledException) { items.Clear(); scratch.Cancelled = true; }
        catch (Exception ex) { scratch.Errors.Add($"{root}: {ex.Message}"); }
        return new MediaCatalog(root, method, items, scratch);
    }
}
