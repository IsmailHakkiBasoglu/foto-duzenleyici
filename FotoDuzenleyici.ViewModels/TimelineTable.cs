using System.Globalization;
using System.Text;
using FotoDuzenleyici.Core;

namespace FotoDuzenleyici.ViewModels;

/// <summary>Zaman çizelgesi süzgeci; Hepsi = Foto + Raw + Video.</summary>
public enum KindFilter { Photo, Raw, Video, All }

public sealed record FolderCount(string Folder, int Count);

/// <summary>
/// Bir ay kutusu. <paramref name="Count"/>/<paramref name="Size"/> süzgece göre; Photo/Raw/Video dağılımı süzgeçsiz.
/// <paramref name="NotFromMetadata"/>: tarihi dosya adından ya da dosya zamanından gelenler.
/// <paramref name="Intensity"/>: √(sayı / en büyük ay), boşsa 0.
/// </summary>
public sealed record TimelineCell(int Year, int Month, int Count, long Size, int Photo, int Raw, int Video,
    int NotFromMetadata, IReadOnlyList<FolderCount> Folders, double Intensity)
{
    public bool IsEmpty => Count == 0;

    public string Title => $"{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Month)} {Year}";

    public string Tooltip
    {
        get
        {
            var sb = new StringBuilder($"{Title} — {Count:N0} dosya · {Sizes.Text(Size)}\nFoto {Photo:N0} · Raw {Raw:N0} · Video {Video:N0}");
            if (Folders.Count > 1)
                sb.Append("\nKlasörler: ").Append(string.Join(", ", Folders.Take(3).Select(f => $"{Name(f.Folder)} ({f.Count:N0})")));
            if (NotFromMetadata > 0)
                sb.Append($"\n{NotFromMetadata:N0} tanesinin tarihi dosya adından / dosya zamanından");
            return sb.ToString();
        }
    }

    static string Name(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } n ? n : folder;
}

/// <summary>Bir yıl satırı; <paramref name="EndYear"/> doluysa art arda boş yıllar tek satıra toplanmıştır.</summary>
public sealed record TimelineRow(int Year, int? EndYear, IReadOnlyList<TimelineCell> Months, int Total)
{
    public bool IsCollapsed => EndYear is not null;
    public string Label => IsCollapsed ? $"{Year} – {EndYear}: boş" : Year.ToString(CultureInfo.InvariantCulture);
}

public sealed record TimelineTable(IReadOnlyList<TimelineRow> Rows, int Total, long Size,
    DateOnly? First, DateOnly? Last, int FilledMonths, int EmptyMonths)
{
    public string Summary => First is not { } f || Last is not { } l
        ? "Bu klasörde bu türde dosya bulunamadı."
        : $"{Total:N0} dosya · {Sizes.Text(Size)} · {Month(f)} – {Month(l)} · {FilledMonths:N0} ay dolu, {EmptyMonths:N0} ay boş";

    static string Month(DateOnly d) => $"{d.Year} {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(d.Month)}";

    static bool Matches(FileKind k, KindFilter f) => f switch
    {
        KindFilter.Photo => k == FileKind.Photo,
        KindFilter.Raw => k == FileKind.Raw,
        KindFilter.Video => k == FileKind.Video,
        _ => k is FileKind.Photo or FileKind.Raw or FileKind.Video,
    };

    public static TimelineTable Build(IEnumerable<TimelineEntry> entries, KindFilter filter)
    {
        var all = entries.ToList();
        var byMonth = all.GroupBy(e => (e.Year, e.Month)).ToDictionary(g => g.Key, g => g.ToList());
        var shown = all.Where(e => Matches(e.Kind, filter)).ToList();
        if (shown.Count == 0) return new TimelineTable(Array.Empty<TimelineRow>(), 0, 0, null, null, 0, 0);

        var counts = shown.GroupBy(e => (e.Year, e.Month)).ToDictionary(g => g.Key, g => g.Count());
        double max = counts.Values.Max();
        var first = counts.Keys.Min();
        var last = counts.Keys.Max();

        var rows = new List<TimelineRow>();
        for (int y = first.Year; y <= last.Year; y++)
        {
            if (!counts.Keys.Any(k => k.Year == y))
            {
                int end = y;
                while (end + 1 <= last.Year && !counts.Keys.Any(k => k.Year == end + 1)) end++;
                if (end > y)
                {
                    rows.Add(new TimelineRow(y, end, Array.Empty<TimelineCell>(), 0));
                    y = end;
                    continue;
                }
            }
            var months = new List<TimelineCell>(12);
            for (int m = 1; m <= 12; m++)
            {
                var items = byMonth.TryGetValue((y, m), out var l) ? l : new List<TimelineEntry>();
                var hit = items.Where(e => Matches(e.Kind, filter)).ToList();
                months.Add(new TimelineCell(y, m, hit.Count, hit.Sum(e => e.Size),
                    items.Count(e => e.Kind == FileKind.Photo), items.Count(e => e.Kind == FileKind.Raw),
                    items.Count(e => e.Kind == FileKind.Video),
                    hit.Count(e => e.Source is DateSource.FileName or DateSource.FileTime),
                    hit.GroupBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new FolderCount(g.Key, g.Count()))
                        .OrderByDescending(f => f.Count).ThenBy(f => f.Folder, StringComparer.OrdinalIgnoreCase).ToList(),
                    hit.Count == 0 ? 0 : Math.Sqrt(hit.Count / max)));
            }
            rows.Add(new TimelineRow(y, null, months, months.Sum(c => c.Count)));
        }

        int filled = counts.Count, empty = 0;
        var collapsed = rows.Where(r => r.IsCollapsed).ToList();
        for (var d = new DateOnly(first.Year, first.Month, 1); d <= new DateOnly(last.Year, last.Month, 1); d = d.AddMonths(1))
            if (!counts.ContainsKey((d.Year, d.Month)) && !collapsed.Any(r => d.Year >= r.Year && d.Year <= r.EndYear))
                empty++;

        return new TimelineTable(rows, shown.Count, shown.Sum(e => e.Size),
            new DateOnly(first.Year, first.Month, 1), new DateOnly(last.Year, last.Month, 1), filled, empty);
    }
}
