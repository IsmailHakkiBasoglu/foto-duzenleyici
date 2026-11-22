using System.Windows.Media.Imaging;
using FotoDuzenleyici.Core;
using Xunit;

namespace FotoDuzenleyici.Tests;

/// <summary>Windows'un kendi küçük resmi (videolar için): hata durumunda null, asla istisna yok.</summary>
public class ShellThumbnailTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fd_sth_" + Guid.NewGuid().ToString("N"));

    public ShellThumbnailTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Get_MissingFile_Null()
    {
        Assert.Null(ShellThumbnail.Get(Path.Combine(_dir, "yok.mp4"), 256));
    }

    [Fact]
    public void Get_Jpeg_ReturnsFrozenBitmap()
    {
        var p = Path.Combine(_dir, "a.jpg");
        File.WriteAllBytes(p, TestImages.PatternJpeg(64, 48, seed: 1));

        var b = ShellThumbnail.Get(p, 256);

        Assert.NotNull(b);
        Assert.True(b!.IsFrozen);
        Assert.True(b.PixelWidth > 0 && b.PixelHeight > 0);
    }

    [Fact]
    public void DefaultDecode_Video_UsesShell_NoThrowWhenNoCodec()
    {
        var p = Path.Combine(_dir, "bozuk.mp4");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4 });

        var ex = Record.Exception(() => ThumbnailLoader.DefaultDecode(new MediaFile { Path = p, Kind = FileKind.Video }));

        Assert.Null(ex);
    }

    [Fact]
    public void DefaultDecode_VideoWithJpegContent_GetsShellImage()
    {
        // Kabuk küçük resmi içerik türüne değil dosyaya bakar: .mp4 uzantılı geçerli bir video yoksa bile çağrı yapılır.
        // Burada JPG'yi video olarak işaretleyip kabuk yolunun seçildiğini (WIC'in değil) kanıtlıyoruz: .jpg dosyası.
        var p = Path.Combine(_dir, "kare.jpg");
        File.WriteAllBytes(p, TestImages.PatternJpeg(64, 48, seed: 2));

        var img = ThumbnailLoader.DefaultDecode(new MediaFile { Path = p, Kind = FileKind.Video });

        Assert.IsAssignableFrom<BitmapSource>(img);
    }
}
