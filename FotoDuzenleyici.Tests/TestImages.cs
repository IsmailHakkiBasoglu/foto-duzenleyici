using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FotoDuzenleyici.Tests;

/// <summary>
/// Benzerlik testleri için gerçek piksel verisi olan görüntüler (WPF ile). Desen kesirli koordinatlarla
/// tanımlıdır; aynı seed farklı çözünürlükte aynı görüntüyü verir (küçültülmüş kopya gibi).
/// </summary>
static class TestImages
{
    /// <param name="rotate90">Pikselleri saat yönünde 90° döndürür (fiziksel döndürme).</param>
    /// <param name="orientation">EXIF yön etiketi (1–8); null ise yazılmaz.</param>
    public static byte[] PatternJpeg(int width, int height, int seed, int quality = 90,
        ushort? orientation = null, bool rotate90 = false)
    {
        var px = Pattern(width, height, seed);
        int w = width, h = height;
        if (rotate90) (px, w, h) = (RotateCw(px, width, height), height, width);
        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr24, null, px, w * 3);

        BitmapMetadata? meta = null;
        if (orientation is { } o)
        {
            meta = new BitmapMetadata("jpg");
            meta.SetQuery("/app1/ifd/{ushort=274}", o);
        }
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(src, null, meta, null));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static byte[] SolidJpeg(byte r, byte g, byte b)
    {
        var px = new byte[64 * 64 * 3];
        for (int i = 0; i < px.Length; i += 3) { px[i] = b; px[i + 1] = g; px[i + 2] = r; }
        var enc = new JpegBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr24, null, px, 64 * 3)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    /// <summary>8×8 dama deseni PNG; istenirse yarısı saydam.</summary>
    public static byte[] TinyPng(bool transparent)
    {
        var px = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            int i = (y * 8 + x) * 4;
            byte v = (byte)((x + y) % 2 == 0 ? 240 : 20);
            px[i] = px[i + 1] = px[i + 2] = v;
            px[i + 3] = (byte)(transparent && x < 4 ? 0 : 255);
        }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, px, 8 * 4)));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    static byte[] Pattern(int w, int h, int seed)
    {
        var rnd = new Random(seed);
        var rects = Enumerable.Range(0, 8).Select(_ => (
            x0: rnd.NextDouble() * 0.8, y0: rnd.NextDouble() * 0.8, sw: 0.1 + rnd.NextDouble() * 0.3,
            sh: 0.1 + rnd.NextDouble() * 0.3, c: (b: (byte)rnd.Next(256), g: (byte)rnd.Next(256), r: (byte)rnd.Next(256))))
            .ToList();
        var gx = rnd.NextDouble() < 0.5;
        var px = new byte[w * h * 3];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double fx = (x + 0.5) / w, fy = (y + 0.5) / h;
            byte v = (byte)(255 * (gx ? fx : fy));
            (byte b, byte g, byte r) c = (v, v, v);
            foreach (var q in rects)
                if (fx >= q.x0 && fx < q.x0 + q.sw && fy >= q.y0 && fy < q.y0 + q.sh) c = q.c;
            int i = (y * w + x) * 3;
            px[i] = c.b; px[i + 1] = c.g; px[i + 2] = c.r;
        }
        return px;
    }

    static byte[] RotateCw(byte[] px, int w, int h)
    {
        var dst = new byte[px.Length];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int nx = h - 1 - y, ny = x; // yeni genişlik h
            Array.Copy(px, (y * w + x) * 3, dst, (ny * h + nx) * 3, 3);
        }
        return dst;
    }
}
