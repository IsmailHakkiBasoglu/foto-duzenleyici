namespace FotoDuzenleyici.ViewModels;

/// <summary>Galeride bir ay başlığı satırı.</summary>
public sealed record GalleryHeaderRow(GalleryMonth Month);

/// <summary>Galeride bir sıra karo (pencere genişliğine sığan kadar).</summary>
public sealed record GalleryTileRow(GalleryMonth Month, IReadOnlyList<MonthPhoto> Photos);

/// <summary>
/// Galerinin satırları ve dikey konumları: görünüm yalnız görünen satırları kurar, zaman çubuğu bir konumun hangi
/// aya düştüğünü ve yılların nerede başladığını buradan bilir. WPF'siz; sütun sayısı değişince yeniden kurulur.
/// </summary>
public sealed class GalleryLayout
{
    readonly double[] _offsets;
    readonly GalleryMonth[] _monthOfRow;

    GalleryLayout(List<object> rows, double[] offsets, GalleryMonth[] monthOfRow, double total, List<(int, double)> years)
    {
        Rows = rows;
        _offsets = offsets;
        _monthOfRow = monthOfRow;
        TotalHeight = total;
        YearMarks = years;
    }

    public IReadOnlyList<object> Rows { get; }
    public double TotalHeight { get; }
    /// <summary>Her yılın ilk (en yeni) ayının başlığının üst kenarı; yeniden eskiye.</summary>
    public IReadOnlyList<(int Year, double Offset)> YearMarks { get; }

    public static GalleryLayout Build(IReadOnlyList<GalleryMonth> months, int columns, double headerHeight, double rowHeight)
    {
        columns = Math.Max(1, columns);
        var rows = new List<object>();
        var offsets = new List<double>();
        var owner = new List<GalleryMonth>();
        var years = new List<(int, double)>();
        double y = 0;
        foreach (var m in months)
        {
            if (years.Count == 0 || years[^1].Item1 != m.Year) years.Add((m.Year, y));
            rows.Add(new GalleryHeaderRow(m)); offsets.Add(y); owner.Add(m); y += headerHeight;
            for (int i = 0; i < m.Photos.Count; i += columns)
            {
                rows.Add(new GalleryTileRow(m, m.Photos.Skip(i).Take(columns).ToList()));
                offsets.Add(y); owner.Add(m); y += rowHeight;
            }
        }
        return new GalleryLayout(rows, offsets.ToArray(), owner.ToArray(), y, years);
    }

    public double OffsetOfRow(int row) => _offsets[row];

    /// <summary>Konumu içeren satır (sınırlara kırpılır); boş düzende -1.</summary>
    public int RowAtOffset(double y)
    {
        if (_offsets.Length == 0) return -1;
        int i = Array.BinarySearch(_offsets, y);
        if (i < 0) i = ~i - 1;
        return Math.Clamp(i, 0, _offsets.Length - 1);
    }

    public GalleryMonth? MonthAtOffset(double y) => RowAtOffset(y) is var i and >= 0 ? _monthOfRow[i] : null;
}
