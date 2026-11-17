using System.Globalization;
using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

public class TimelineTableTests : IDisposable
{
    readonly CultureInfo _old = CultureInfo.CurrentCulture;

    public TimelineTableTests() => CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
    public void Dispose() => CultureInfo.CurrentCulture = _old;

    static TimelineEntry E(int y, int m, FileKind k = FileKind.Photo, long size = 1, string folder = @"C:\k\A",
        DateSource src = DateSource.Exif) => new(y, m, k, size, folder, src);

    static IEnumerable<TimelineEntry> Many(int n, int y, int m, string folder = @"C:\k\A") =>
        Enumerable.Range(0, n).Select(_ => E(y, m, folder: folder));

    static TimelineCell Cell(TimelineTable t, int y, int m) => t.Rows.Single(r => r.Year == y && !r.IsCollapsed).Months[m - 1];

    static readonly TimelineEntry[] Nov =
    {
        E(2022, 11, size: 3), E(2022, 11, size: 5), E(2022, 11, FileKind.Raw, 7), E(2022, 11, FileKind.Video, 9),
    };

    [Fact]
    public void Build_PhotoFilter_CountsOnlyPhotos_ButCellKeepsKindBreakdown()
    {
        var c = Cell(TimelineTable.Build(Nov, KindFilter.Photo), 2022, 11);
        Assert.Equal((2, 8L), (c.Count, c.Size));
        Assert.Equal((2, 1, 1), (c.Photo, c.Raw, c.Video));
    }

    [Fact]
    public void Build_AllFilter_CountsPhotoRawVideo()
    {
        var t = TimelineTable.Build(Nov, KindFilter.All);
        Assert.Equal(4, Cell(t, 2022, 11).Count);
        Assert.Equal(24L, t.Size);
    }

    [Fact]
    public void Build_RowsSpanFirstToLastYear_TwelveCellsEach_WithYearTotal()
    {
        var t = TimelineTable.Build(new[] { E(2021, 7), E(2022, 11), E(2022, 11) }, KindFilter.Photo);
        Assert.Equal(new[] { 2021, 2022 }, t.Rows.Select(r => r.Year));
        Assert.All(t.Rows, r => Assert.Equal(12, r.Months.Count));
        Assert.Equal(2, t.Rows[1].Total);
        Assert.Equal("2022", t.Rows[1].Label);
    }

    [Fact]
    public void Build_SingleEmptyYear_IsNormalRow()
    {
        var t = TimelineTable.Build(new[] { E(2019, 1), E(2021, 1) }, KindFilter.Photo);
        Assert.Equal(new[] { 2019, 2020, 2021 }, t.Rows.Select(r => r.Year));
        Assert.False(t.Rows[1].IsCollapsed);
        Assert.All(t.Rows[1].Months, c => Assert.True(c.IsEmpty));
    }

    [Fact]
    public void Build_TwoOrMoreEmptyYears_CollapseToOneRow()
    {
        var t = TimelineTable.Build(new[] { E(1990, 1), E(2022, 11) }, KindFilter.Photo);
        Assert.Equal(3, t.Rows.Count);
        Assert.True(t.Rows[1].IsCollapsed);
        Assert.Equal((1991, (int?)2021), (t.Rows[1].Year, t.Rows[1].EndYear));
        Assert.Equal("1991 – 2021: boş", t.Rows[1].Label);
        Assert.Empty(t.Rows[1].Months);
    }

    [Fact]
    public void Build_EmptyMonths_CountedBetweenFirstAndLast_ExcludingCollapsedYears()
    {
        var t = TimelineTable.Build(new[] { E(2022, 10), E(2022, 12) }, KindFilter.Photo);
        Assert.Equal((2, 1), (t.FilledMonths, t.EmptyMonths));

        var gap = TimelineTable.Build(new[] { E(1990, 12), E(2022, 1) }, KindFilter.Photo);
        Assert.Equal(0, gap.EmptyMonths);
    }

    [Fact]
    public void Build_Intensity_IsSqrtOfShareOfMax_ZeroWhenEmpty()
    {
        var t = TimelineTable.Build(Many(100, 2022, 1).Concat(Many(25, 2022, 2)), KindFilter.Photo);
        Assert.Equal(1.0, Cell(t, 2022, 1).Intensity, 6);
        Assert.Equal(0.5, Cell(t, 2022, 2).Intensity, 6);
        Assert.Equal(0.0, Cell(t, 2022, 3).Intensity);
    }

    [Fact]
    public void Build_Folders_GroupedByFolder_MostFirst()
    {
        var t = TimelineTable.Build(Many(1, 2022, 1, @"C:\k\B").Concat(Many(3, 2022, 1, @"C:\k\A")), KindFilter.Photo);
        Assert.Equal(new[] { new FolderCount(@"C:\k\A", 3), new FolderCount(@"C:\k\B", 1) }, Cell(t, 2022, 1).Folders);
    }

    [Fact]
    public void Build_NotFromMetadata_CountsFileNameAndFileTimeSources()
    {
        var t = TimelineTable.Build(new[]
        {
            E(2022, 1), E(2022, 1, src: DateSource.FileName), E(2022, 1, src: DateSource.FileTime),
            E(2022, 1, src: DateSource.VideoMetadata),
        }, KindFilter.Photo);
        Assert.Equal(2, Cell(t, 2022, 1).NotFromMetadata);
    }

    [Fact]
    public void Build_NoEntriesForFilter_EmptyTable_NoDivideByZero()
    {
        var t = TimelineTable.Build(new[] { E(2022, 1, FileKind.Video) }, KindFilter.Photo);
        Assert.Empty(t.Rows);
        Assert.Equal(0, t.Total);
        Assert.Null(t.First);
        Assert.Equal("Bu klasörde bu türde dosya bulunamadı.", t.Summary);
    }

    [Fact]
    public void Summary_And_Tooltip_Text()
    {
        var entries = Nov.Concat(new[] { E(2023, 3) }).ToList();
        var t = TimelineTable.Build(entries, KindFilter.Photo);
        Assert.Equal("3 dosya · 9 B · 2022 Kasım – 2023 Mart · 2 ay dolu, 3 ay boş", t.Summary);
        Assert.Equal("Kasım 2022", Cell(t, 2022, 11).Title);
        Assert.Equal("Kasım 2022 — 2 dosya · 8 B\nFoto 2 · Raw 1 · Video 1", Cell(t, 2022, 11).Tooltip);

        var multi = TimelineTable.Build(Many(3, 2022, 1, @"C:\k\A").Concat(Many(1, 2022, 1, @"C:\k\B"))
            .Append(E(2022, 1, folder: @"C:\k\C", src: DateSource.FileTime)), KindFilter.Photo);
        Assert.Equal("Ocak 2022 — 5 dosya · 5 B\nFoto 5 · Raw 0 · Video 0\nKlasörler: A (3), B (1), C (1)"
            + "\n1 tanesinin tarihi dosya adından / dosya zamanından", Cell(multi, 2022, 1).Tooltip);
    }
}
