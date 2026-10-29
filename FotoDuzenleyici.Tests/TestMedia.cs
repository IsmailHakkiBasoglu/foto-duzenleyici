using System.Buffers.Binary;
using System.Text;

namespace FotoDuzenleyici.Tests;

/// <summary>
/// Testler için minik medya dosyaları üretir. Görüntü/ses verisi yoktur; tarayıcı yalnızca metadatayı okur.
/// Böylece depoya ikili örnek dosya eklemek gerekmez.
/// </summary>
static class TestMedia
{
    /// <summary>EXIF'inde kamera markası ve çekim tarihi (DateTimeOriginal) olan JPEG.</summary>
    public static byte[] Jpeg(string make, DateTime taken)
    {
        var makeBytes = Encoding.ASCII.GetBytes(make + "\0");
        var date = Encoding.ASCII.GetBytes(taken.ToString("yyyy:MM:dd HH:mm:ss") + "\0");

        // TIFF (little-endian): başlık 8 + IFD0 (2 kayıt) 30 + marka + Exif IFD (1 kayıt) 18 + tarih 20
        int makeOff = 8 + 30;
        int exifOff = makeOff + makeBytes.Length + (makeBytes.Length % 2);
        int dateOff = exifOff + 18;
        var tiff = new byte[dateOff + date.Length];
        var t = tiff.AsSpan();
        "II"u8.CopyTo(t);
        BinaryPrimitives.WriteUInt16LittleEndian(t[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(t[4..], 8);
        BinaryPrimitives.WriteUInt16LittleEndian(t[8..], 2);
        Entry(t[10..], 0x010F, 2, makeBytes.Length, makeOff);   // Make
        Entry(t[22..], 0x8769, 4, 1, exifOff);                  // Exif IFD işaretçisi
        makeBytes.CopyTo(t[makeOff..]);
        BinaryPrimitives.WriteUInt16LittleEndian(t[exifOff..], 1);
        Entry(t[(exifOff + 2)..], 0x9003, 2, date.Length, dateOff); // DateTimeOriginal
        date.CopyTo(t[dateOff..]);

        var app1 = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).ToArray();
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8, 0xFF, 0xE1]);
        ms.Write([(byte)((app1.Length + 2) >> 8), (byte)(app1.Length + 2)]);
        ms.Write(app1);
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    static void Entry(Span<byte> s, ushort tag, ushort type, int count, int value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(s, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(s[2..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], (uint)count);
        BinaryPrimitives.WriteUInt32LittleEndian(s[8..], (uint)value);
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
