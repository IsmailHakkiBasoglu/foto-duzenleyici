using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FotoDuzenleyici.Core;

/// <param name="Sharpness">En net 2 parçanın Laplace varyansı ortalaması; büyük = net.</param>
/// <param name="MeanLuma">Ortalama parlaklık (0–255).</param>
/// <param name="DarkFraction">Parlaklığı <see cref="QualityMeter.DarkPixel"/> altında kalan piksellerin oranı.</param>
/// <param name="Flat">Ayrıntısız sahne (bulutsuz gökyüzü, düz duvar): netliği yargılanamaz.</param>
public readonly record struct QualityScore(double Sharpness, double MeanLuma, double DarkFraction, bool Flat);

/// <summary>Bir fotoğrafın netliğini ve karanlığını yerelde ölçer. Dosyaya dokunmaz.</summary>
public static class QualityMeter
{
    public const int MeasureWidth = 1024;
    public const int MinWidth = 320;
    public const byte DarkPixel = 20;
    const int Tiles = 4;
    /// <summary>Parlaklığın standart sapması bunun altındaysa sahne düz sayılır.</summary>
    const double FlatStd = 12;

    public static QualityScore? Measure(string path, int orientation, out string? error)
    {
        error = null;
        try
        {
            int width;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                width = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation,
                    BitmapCacheOption.None).Frames[0].PixelWidth;
            if (width < MinWidth) { error = "çok küçük"; return null; }

            // Dar görüntü büyütülmez: büyütme yapay bulanıklık üretir.
            var src = ImageHasher.TryDecode(path, orientation, Math.Min(MeasureWidth, width), out error);
            if (src is null) return null;
            var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            int w = gray.PixelWidth, h = gray.PixelHeight, stride = (w + 3) & ~3;
            var p = new byte[stride * h];
            gray.CopyPixels(p, stride, 0);
            return Score(p, stride, w, h);
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    static QualityScore Score(byte[] p, int stride, int w, int h)
    {
        // Parça başına Laplace toplamları (kenar pikseller hariç).
        var sum = new double[Tiles * Tiles];
        var sq = new double[Tiles * Tiles];
        var n = new long[Tiles * Tiles];
        for (int y = 1; y < h - 1; y++)
        {
            int ty = y * Tiles / h, row = y * stride;
            for (int x = 1; x < w - 1; x++)
            {
                int i = row + x;
                double lap = 4 * p[i] - p[i - 1] - p[i + 1] - p[i - stride] - p[i + stride];
                int t = ty * Tiles + x * Tiles / w;
                sum[t] += lap; sq[t] += lap * lap; n[t]++;
            }
        }
        var vars = Enumerable.Range(0, sum.Length)
            .Select(t => n[t] == 0 ? 0 : sq[t] / n[t] - (sum[t] / n[t]) * (sum[t] / n[t]))
            .OrderByDescending(v => v).ToList();
        double sharp = (vars[0] + vars[1]) / 2;

        double lsum = 0, lsq = 0; long dark = 0, count = (long)w * h;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte v = p[y * stride + x];
            lsum += v; lsq += (double)v * v;
            if (v < DarkPixel) dark++;
        }
        double mean = lsum / count;
        double std = Math.Sqrt(Math.Max(0, lsq / count - mean * mean));
        return new QualityScore(sharp, mean, (double)dark / count, std < FlatStd);
    }
}
