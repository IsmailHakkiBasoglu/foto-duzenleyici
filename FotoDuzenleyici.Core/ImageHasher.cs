using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Görüntünün parmak izi: 64 bitlik dHash (hızlı aday bulma) + 16×16 gri küçük resim (doğrulama). Ayrıntısı az
/// görüntülerde iz çakışabilir; küçük resimler piksel piksel yakın değilse kopya sayılmaz.
/// </summary>
public readonly record struct ImagePrint(ulong Hash, byte[] Thumb);

/// <summary>
/// Algısal iz (dHash): fotoğraf Windows'un görüntü bileşeniyle (WIC) küçük boyutta çözülür, EXIF yönü uygulanır,
/// gri tonda 9×8'e küçültülür; her satırda komşu farkının işareti 64 bit verir. Küçültme ve yeniden sıkıştırma
/// izi pek değiştirmez, farklı görüntüler çok farklı iz verir. Dosyaya yazmaz.
/// </summary>
public static class ImageHasher
{
    const int HashDecodeWidth = 72;
    const int FlatRange = 8; // 9×8 ızgarada en açık ile en koyu arasındaki fark bundan azsa görüntü düzdür
    const int DegenerateBits = 4; // izde en fazla bu kadar 1 (ya da 0) varsa görüntüde ayrıntı yoktur

    /// <summary>Fotoğrafı en fazla <paramref name="decodePixelWidth"/> genişlikte çözer ve EXIF yönünü uygular.
    /// Dosya yoksa ya da çözülemezse null.</summary>
    public static BitmapSource? Decode(string path, int orientation, int decodePixelWidth) =>
        TryDecode(path, orientation, decodePixelWidth, out _);

    public static ulong? DHash(string path, int orientation, out string? error) => Print(path, orientation, out error)?.Hash;

    /// <summary>Ortalama mutlak gri farkı (0–255): küçültülmüş kopyada birkaç, alakasız görüntüde onlarca.</summary>
    public static double ThumbDiff(byte[] a, byte[] b)
    {
        long sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }

    public static ImagePrint? Print(string path, int orientation, out string? error)
    {
        var src = TryDecode(path, orientation, HashDecodeWidth, out error);
        if (src is null) return null;
        try
        {
            var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight, stride = (w + 3) & ~3;
            var buf = new byte[stride * h];
            gray.CopyPixels(buf, stride, 0);

            var g = Grid(buf, stride, w, h, 9, 8);
            var t = Grid(buf, stride, w, h, 16, 16);

            double min = double.MaxValue, max = double.MinValue;
            foreach (var v in g) { min = Math.Min(min, v); max = Math.Max(max, v); }
            // Düz görüntülerin (siyah kare, boş gökyüzü) izi neredeyse sıfırdır; birbirleriyle "kopya" sanılmasınlar.
            if (max - min < FlatRange) { error = "düz görüntü"; return null; }

            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++, bit++)
                if (g[y, x] < g[y, x + 1]) hash |= 1UL << bit;
            // Yumuşak gradyan (gökyüzü, gün batımı): iz neredeyse hep 1 ya da 0; alakasız görüntüler eşleşir.
            if (BitOperations.PopCount(hash) is <= DegenerateBits or >= 64 - DegenerateBits)
            { error = "ayrıntısız görüntü"; return null; }

            var thumb = new byte[256];
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++) thumb[y * 16 + x] = (byte)Math.Round(t[y, x]);
            return new ImagePrint(hash, thumb);
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>Alan ortalamasıyla cols×rows ızgara (küçük görüntülerde her hücre en az bir piksel).</summary>
    static double[,] Grid(byte[] buf, int stride, int w, int h, int cols, int rows)
    {
        var g = new double[rows, cols];
        for (int cy = 0; cy < rows; cy++)
        for (int cx = 0; cx < cols; cx++)
        {
            int x0 = Math.Min(cx * w / cols, w - 1), x1 = Math.Max(x0 + 1, (cx + 1) * w / cols);
            int y0 = Math.Min(cy * h / rows, h - 1), y1 = Math.Max(y0 + 1, (cy + 1) * h / rows);
            long sum = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) sum += buf[y * stride + x];
            g[cy, cx] = (double)sum / ((x1 - x0) * (y1 - y0));
        }
        return g;
    }

    public static int Distance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);

    static BitmapSource? TryDecode(string path, int orientation, int decodePixelWidth, out string? error)
    {
        error = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = fs;
            bi.DecodePixelWidth = decodePixelWidth;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.EndInit();
            bi.Freeze();
            return Orient(bi, orientation);
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    /// <summary>EXIF yönü (1–8): fotoğrafın ekranda nasıl görünmesi gerektiği.</summary>
    static BitmapSource Orient(BitmapSource src, int orientation)
    {
        var (mirror, angle) = orientation switch
        {
            2 => (true, 0), 3 => (false, 180), 4 => (true, 180), 5 => (true, 270),
            6 => (false, 90), 7 => (true, 90), 8 => (false, 270), _ => (false, 0),
        };
        if (!mirror && angle == 0) return src;
        var t = new TransformGroup();
        if (mirror) t.Children.Add(new ScaleTransform(-1, 1));
        if (angle != 0) t.Children.Add(new RotateTransform(angle));
        var tb = new TransformedBitmap(src, t);
        tb.Freeze();
        return tb;
    }
}
