using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Plan sekmesindeki önizleme panelinin metni ve hangi dosyanın gösterileceği.</summary>
public class PlanPreviewTests
{
    static MediaFile F(string path, FileKind kind = FileKind.Photo, long size = 0) => new() { Path = path, Kind = kind, Size = size };

    static PlanItem Item(MediaFile f, PlanAction a, string dir = @"D:\Arsiv\2024-03\2024-03-Foto", string? name = null,
        MediaFile? dupOf = null, string? note = null) =>
        new() { File = f, Action = a, TargetDir = dir, TargetName = name ?? Path.GetFileName(f.Path), DuplicateOf = dupOf, Note = note };

    [Theory]
    [InlineData(PlanAction.Move, "Taşı")]
    [InlineData(PlanAction.Duplicate, "Kopya")]
    [InlineData(PlanAction.AlreadyInPlace, "Yerinde")]
    [InlineData(PlanAction.Similar, "Benzer")]
    [InlineData(PlanAction.Flawed, "Kusurlu")]
    public void ActionName_IsTurkish(PlanAction a, string expected) => Assert.Equal(expected, PlanPreview.ActionName(a));

    [Fact]
    public void Describe_Move_ShowsFileFactsAndTarget()
    {
        var f = F(@"E:\DCIM\IMG_0001.JPG", size: 4_404_019);
        f.Width = 6000; f.Height = 4000; f.Taken = new DateTime(2024, 3, 5, 14, 22, 0); f.Model = "Canon EOS 250D";
        var text = PlanPreview.Describe(Item(f, PlanAction.Move, name: "2024-03-05 IMG_0001.JPG"), null, null);
        Assert.Equal(
            "IMG_0001.JPG\n" +
            "4,2 MB · 6000×4000\n" +
            "2024-03-05 14:22 · Canon EOS 250D\n" +
            "Taşınacak →\n" +
            @"D:\Arsiv\2024-03\2024-03-Foto\2024-03-05 IMG_0001.JPG", text);
    }

    [Fact]
    public void Describe_AlreadyInPlace_HasNoTargetLine()
    {
        var text = PlanPreview.Describe(Item(F(@"D:\Arsiv\a.jpg", size: 1000), PlanAction.AlreadyInPlace), null, null);
        Assert.Contains("Zaten yerinde, değişmeyecek", text);
        Assert.DoesNotContain("→", text);
        Assert.Contains("Tarihsiz", text);
    }

    [Fact]
    public void Describe_Duplicate_NamesKeeper()
    {
        var keeper = F(@"D:\Arsiv\2024-03\a.jpg");
        var text = PlanPreview.Describe(Item(F(@"E:\a (1).jpg"), PlanAction.Duplicate, @"D:\Arsiv\_Tekrarlar", dupOf: keeper), null, null);
        Assert.Contains("Kopya, ayrılacak →", text);
        Assert.Contains(@"Korunan: D:\Arsiv\2024-03\a.jpg", text);
    }

    [Fact]
    public void Describe_QualityAndFlaw()
    {
        var text = PlanPreview.Describe(Item(F(@"E:\b.jpg"), PlanAction.Flawed, @"D:\Arsiv\_Kusurlu\2024-03"),
            new QualityScore(12.4, 18.6, 0.8, false), null);
        Assert.Contains("Kusurlu, ayrılacak →", text);
        Assert.Contains("Netlik 12 · Parlaklık 19 · BULANIK · KARANLIK", text);
    }

    [Fact]
    public void Describe_GoodQuality_HasNoFlawText()
    {
        var text = PlanPreview.Describe(Item(F(@"E:\b.jpg"), PlanAction.Move), new QualityScore(400, 120, 0.01, false), null);
        Assert.Contains("Netlik 400 · Parlaklık 120", text);
        Assert.DoesNotContain("BULANIK", text);
    }

    [Fact]
    public void Describe_PairAndNote()
    {
        var photo = F(@"E:\IMG_1.JPG");
        var raw = F(@"E:\IMG_1.CR3", FileKind.Raw);
        Assert.Contains("RAW eşi: IMG_1.CR3", PlanPreview.Describe(Item(photo, PlanAction.Move), null, raw));
        Assert.Contains("JPG eşi: IMG_1.JPG",
            PlanPreview.Describe(Item(raw, PlanAction.Move, note: "isim çakışması: tarih eklendi"), null, photo));
        Assert.Contains("Not: isim çakışması: tarih eklendi",
            PlanPreview.Describe(Item(raw, PlanAction.Move, note: "isim çakışması: tarih eklendi"), null, photo));
    }

    // Önizleme kaynağı: video/PDF çözülmez, RAW açılamazsa JPG eşi gösterilir.
    [Fact]
    public void Load_Photo_Decodes()
    {
        var f = F(@"E:\a.jpg");
        var (img, note) = PlanPreview.Load(f, null, x => x == f ? "resim" : null);
        Assert.Equal("resim", img);
        Assert.Null(note);
    }

    [Fact]
    public void Load_VideoAndPdf_AreNotDecoded()
    {
        var decoded = 0;
        string? Decode(MediaFile _) { decoded++; return "x"; }
        Assert.Equal((null, "Önizleme yok — video"), PlanPreview.Load(F(@"E:\v.mp4", FileKind.Video), null, Decode));
        Assert.Equal((null, "Önizleme yok — PDF"), PlanPreview.Load(F(@"E:\p.pdf", FileKind.Pdf), null, Decode));
        Assert.Equal(0, decoded);
    }

    [Fact]
    public void Load_RawWithoutCodec_ShowsPhotoPair()
    {
        var raw = F(@"E:\IMG_1.RAF", FileKind.Raw);
        var photo = F(@"E:\IMG_1.JPG");
        var (img, note) = PlanPreview.Load(raw, photo, x => x == photo ? "jpg" : null);
        Assert.Equal("jpg", img);
        Assert.Equal("JPG eşi gösteriliyor", note);
    }

    [Fact]
    public void Load_RawDecodable_ShowsRawItself()
    {
        var raw = F(@"E:\IMG_1.CR3", FileKind.Raw);
        Assert.Equal(("raw", (string?)null), PlanPreview.Load(raw, F(@"E:\IMG_1.JPG"), x => x == raw ? "raw" : "jpg"));
    }

    [Fact]
    public void Load_Failures_Explain()
    {
        Assert.Equal((null, "Görüntü açılamadı"), PlanPreview.Load<string>(F(@"E:\a.jpg"), null, _ => null));
        Assert.Equal((null, "RAW açılamadı (Windows'ta bu RAW için codec yok)"),
            PlanPreview.Load<string>(F(@"E:\a.raf", FileKind.Raw), null, _ => null));
    }
}
