using System.Buffers.Binary;
using System.Text;

namespace FotoDuzenleyici.Tests;

/// <summary>
/// Testler için minik medya dosyaları üretir. Görüntü/ses verisi yoktur; tarayıcı yalnızca metadatayı okur.
/// Böylece depoya ikili örnek dosya eklemek gerekmez.
/// </summary>
static class TestMedia
{
    /// <summary>
    /// EXIF'inde kamera markası ve çekim tarihi (DateTimeOriginal) olan JPEG. İsteğe bağlı: model, saniye kesri,
    /// piksel boyutu, gövde seri no. <paramref name="salt"/> farklıysa aynı EXIF'le farklı baytlı dosya üretir
    /// (yeniden kaydedilmiş gibi).
    /// </summary>
    public static byte[] Jpeg(string make, DateTime taken, string? model = null, string? subSec = null,
        int? width = null, int? height = null, string? serial = null, int salt = 0)
    {
        const ushort Ascii = 2, Long = 4;
        var ifd0 = new List<(ushort tag, ushort type, byte[] value)> { (0x010F, Ascii, Str(make)) };
        if (model is not null) ifd0.Add((0x0110, Ascii, Str(model)));
        var exif = new List<(ushort tag, ushort type, byte[] value)> { (0x9003, Ascii, Str(taken.ToString("yyyy:MM:dd HH:mm:ss"))) };
        if (subSec is not null) exif.Add((0x9291, Ascii, Str(subSec)));
        if (width is { } w) exif.Add((0xA002, Long, U32(w)));
        if (height is { } h) exif.Add((0xA003, Long, U32(h)));
        if (serial is not null) exif.Add((0xA431, Ascii, Str(serial)));

        // TIFF (little-endian): başlık, IFD0 (+ Exif IFD işaretçisi), Exif IFD; uzun değerler her IFD'nin ardında.
        int exifOff = 8 + IfdSize(ifd0.Count + 1) + DataSize(ifd0);
        ifd0.Add((0x8769, Long, U32(exifOff)));
        var tiff = new MemoryStream();
        tiff.Write("II"u8); tiff.Write(U16(42)); tiff.Write(U32(8));
        WriteIfd(tiff, ifd0, 8);
        WriteIfd(tiff, exif, exifOff);

        var app1 = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff.ToArray()).ToArray();
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        Segment(ms, 0xE1, app1);
        if (salt != 0) Segment(ms, 0xFE, Encoding.ASCII.GetBytes($"yeniden kaydedildi {salt}")); // yorum (COM)
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    static byte[] Str(string s) => Encoding.ASCII.GetBytes(s + "\0");
    static byte[] U16(int v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); return b; }
    static byte[] U32(int v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)v); return b; }
    static int IfdSize(int entries) => 2 + 12 * entries + 4;
    static int DataSize(IEnumerable<(ushort tag, ushort type, byte[] value)> e) =>
        e.Where(x => x.value.Length > 4).Sum(x => x.value.Length + x.value.Length % 2);

    static void WriteIfd(MemoryStream ms, List<(ushort tag, ushort type, byte[] value)> entries, int start)
    {
        int data = start + IfdSize(entries.Count);
        ms.Write(U16(entries.Count));
        foreach (var (tag, type, value) in entries)
        {
            ms.Write(U16(tag)); ms.Write(U16(type));
            ms.Write(U32(type == 4 ? value.Length / 4 : value.Length));
            if (value.Length <= 4) { ms.Write(value); ms.Write(new byte[4 - value.Length]); }
            else { ms.Write(U32(data)); data += value.Length + value.Length % 2; }
        }
        ms.Write(U32(0));
        foreach (var (_, _, value) in entries.Where(x => x.value.Length > 4))
        {
            ms.Write(value);
            if (value.Length % 2 == 1) ms.WriteByte(0);
        }
    }

    static void Segment(MemoryStream ms, byte marker, byte[] payload)
    {
        ms.Write([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2)]);
        ms.Write(payload);
    }

    /// <summary>Film başlığına (mvhd) UTC oluşturma tarihi yazılmış MP4, kameraların yaptığı gibi.</summary>
    public static byte[] Mp4(DateTime utc)
    {
        var secs = (uint)(utc - new DateTime(1904, 1, 1)).TotalSeconds;
        var mvhd = new byte[108];
        var m = mvhd.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(m, 108);
        "mvhd"u8.CopyTo(m[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(m[12..], secs);   // oluşturma
        BinaryPrimitives.WriteUInt32BigEndian(m[16..], secs);   // değiştirme
        BinaryPrimitives.WriteUInt32BigEndian(m[20..], 1000);   // zaman ölçeği
        BinaryPrimitives.WriteUInt32BigEndian(m[28..], 0x00010000); // hız 1.0
        BinaryPrimitives.WriteUInt16BigEndian(m[32..], 0x0100);     // ses 1.0
        BinaryPrimitives.WriteUInt32BigEndian(m[44..], 0x00010000); // birim matris
        BinaryPrimitives.WriteUInt32BigEndian(m[60..], 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(m[76..], 0x40000000);
        BinaryPrimitives.WriteUInt32BigEndian(m[104..], 1);         // sonraki iz

        using var ms = new MemoryStream();
        ms.Write([0, 0, 0, 20]); ms.Write("ftypmp42"u8); ms.Write([0, 0, 0, 0]); ms.Write("mp42"u8);
        var moovSize = 8 + mvhd.Length;
        ms.Write([(byte)(moovSize >> 24), (byte)(moovSize >> 16), (byte)(moovSize >> 8), (byte)moovSize]);
        ms.Write("moov"u8);
        ms.Write(mvhd);
        return ms.ToArray();
    }
}
