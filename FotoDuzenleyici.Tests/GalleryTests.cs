using FotoDuzenleyici.Core;
using FotoDuzenleyici.ViewModels;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Sürekli galeri: aylar ve fotoğraflar yeniden eskiye; satır düzeni ve zaman çubuğu hesabı.</summary>
public class GalleryTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "fd_gal_" + Guid.NewGuid().ToString("N"));
    readonly Fakes _f = new();

    public GalleryTests()
    {
        Put(@"2025-12\IMG_20251224_x.jpg");
        Put(@"2026-08\IMG_20260803_a.jpg");
        Put(@"2026-08\IMG_20260803_a.cr2");
        Put(@"2026-08\IMG_20260810_b.jpg");
        Put(@"2026-08\IMG_20260915_baska.jpg");   // klasör adlarında: başka aydan
        Put(@"2026-08\VID_20260805_v.mp4");
        Put(@"2026-09\IMG_20260901_d.jpg");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    void Put(string rel)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[3]);
    }

    MediaCatalog Cat() => CatalogReader.Read(_root, TimelineMethod.FolderNames);
    GalleryViewModel Gal(KindFilter f = KindFilter.Photo) => GalleryViewModel.Build(Cat(), f, _f.Services);

    static string N(MonthPhoto p) => Path.GetFileName(p.File.Path);

    [Fact]
    public void Build_MonthsNewestFirst()
    {
        Assert.Equal(new[] { (2026, 9), (2026, 8), (2025, 12) }, Gal().Months.Select(m => (m.Year, m.Month)));
    }

    [Fact]
    public void Build_PhotosInMonthNewestFirst_OtherMonthAtEnd()
    {
        var aug = Gal().Months.Single(m => m.Month == 8);
        Assert.Equal(new[] { "IMG_20260810_b.jpg", "IMG_20260803_a.jpg", "IMG_20260915_baska.jpg" }, aug.Photos.Select(N));
    }

    [Fact]
    public void Build_RawPairShownOnceWithBadge()
    {
        var aug = Gal().Months.Single(m => m.Month == 8);
        Assert.True(aug.Photos.Single(p => N(p) == "IMG_20260803_a.jpg").HasRaw);
        Assert.DoesNotContain(aug.Photos, p => N(p).EndsWith(".cr2"));
    }

    [Theory]
    [InlineData(KindFilter.Photo)]
    [InlineData(KindFilter.Raw)]
    [InlineData(KindFilter.Video)]
    [InlineData(KindFilter.All)]
    public void Build_CountsEqualTable_ForEachFilter(KindFilter f)
    {
        var c = Cat();
        var table = TimelineTable.Build(c.ToResult().Entries, f);
        var cells = table.Rows.SelectMany(r => r.Months).Where(x => !x.IsEmpty).ToDictionary(x => (x.Year, x.Month), x => x.Count);

        var gal = GalleryViewModel.Build(c, f, _f.Services);

        // Tablo RAW eşlerini ayrı sayar; galeri JPG'nin altında gösterir. Karşılaştırma dosya sayısıyla.
        Assert.Equal(cells, gal.Months.ToDictionary(m => (m.Year, m.Month), m => m.Files));
    }

    [Fact]
    public void Build_FilterLeavesNothing_IsEmpty()
    {
        Put(@"2026-10\IMG_20261001_e.jpg");
        var g = GalleryViewModel.Build(CatalogReader.Read(Path.Combine(_root, "2026-10"), TimelineMethod.CaptureDate), KindFilter.Video, _f.Services);
        Assert.True(g.IsEmpty);
        Assert.Empty(g.Months);
    }

    [Fact]
    public void Title_TurkishMonthAndCount()
    {
        var aug = Gal(KindFilter.All).Months.Single(m => m.Month == 8);
        Assert.Equal("Ağustos 2026", aug.ShortTitle);
        Assert.Equal("Ağustos 2026 · 5", aug.Title);
    }

    [Fact]
    public void OpenMonth_RaisesMonthRequested()
    {
        var g = Gal();
        (int, int)? asked = null;
        g.MonthRequested += (y, m) => asked = (y, m);

        g.OpenMonthCommand.Execute(g.Months[1]);

        Assert.Equal((2026, 8), asked);
    }

    [Fact]
    public void OpenFile_CallsShell()
    {
        var g = Gal();
        var p = g.Months[0].Photos[0];
        g.OpenFileCommand.Execute(p);
        Assert.Equal(p.File.Path, Assert.Single(_f.Shell.Files));
    }

    // ── Düzen ──

    static GalleryMonth M(int y, int m, int n) => new(y, m,
        Enumerable.Range(0, n).Select(i => new MonthPhoto(
            new MediaFile { Path = $"{y}-{m}-{i}.jpg", Kind = FileKind.Photo, Taken = new DateTime(y, m, 1) },
            Array.Empty<MediaFile>(), false)).ToList());

    [Fact]
    public void Layout_RowsAndOffsets()
    {
        var l = GalleryLayout.Build(new[] { M(2026, 8, 5), M(2026, 7, 3) }, columns: 4, headerHeight: 40, rowHeight: 104);

        Assert.Equal(new[] { typeof(GalleryHeaderRow), typeof(GalleryTileRow), typeof(GalleryTileRow), typeof(GalleryHeaderRow), typeof(GalleryTileRow) },
            l.Rows.Select(r => r.GetType()));
        Assert.Equal(new[] { 0.0, 40, 144, 248, 288 }, Enumerable.Range(0, 5).Select(l.OffsetOfRow));
        Assert.Equal(392, l.TotalHeight);
        Assert.Single(((GalleryTileRow)l.Rows[2]).Photos);
    }

    [Fact]
    public void Layout_RowAtOffset_AndMonthAtOffset()
    {
        var l = GalleryLayout.Build(new[] { M(2026, 8, 5), M(2026, 7, 3) }, 4, 40, 104);

        Assert.Equal(0, l.RowAtOffset(0));
        Assert.Equal(1, l.RowAtOffset(40));
        Assert.Equal(2, l.RowAtOffset(200));
        Assert.Equal(4, l.RowAtOffset(10_000));
        Assert.Equal(8, l.MonthAtOffset(247)!.Month);
        Assert.Equal(7, l.MonthAtOffset(248)!.Month);
    }

    [Fact]
    public void Layout_YearMarks_FirstMonthOfEachYear()
    {
        var l = GalleryLayout.Build(new[] { M(2026, 8, 1), M(2026, 2, 1), M(2025, 12, 1), M(2024, 1, 1) }, 4, 40, 104);

        Assert.Equal(new[] { (2026, 0.0), (2025, 288.0), (2024, 432.0) }, l.YearMarks);
    }

    [Fact]
    public void Layout_ColumnsChange_MonthStartsRecomputed()
    {
        var months = new[] { M(2026, 8, 6), M(2026, 7, 1) };

        var wide = GalleryLayout.Build(months, 6, 40, 104);
        var narrow = GalleryLayout.Build(months, 2, 40, 104);

        Assert.Equal(7, wide.MonthAtOffset(144)!.Month);
        Assert.Equal(8, narrow.MonthAtOffset(144)!.Month);
        Assert.Equal(40 + 3 * 104 + 40 + 104, narrow.TotalHeight);
    }

    [Fact]
    public void Layout_Empty()
    {
        var l = GalleryLayout.Build(Array.Empty<GalleryMonth>(), 4, 40, 104);
        Assert.Empty(l.Rows);
        Assert.Equal(0, l.TotalHeight);
        Assert.Null(l.MonthAtOffset(0));
    }
}
