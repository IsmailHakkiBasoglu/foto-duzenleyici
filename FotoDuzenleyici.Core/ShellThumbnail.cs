using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Gezgin'in kullandığı küçük resim (IShellItemImageFactory). WIC'in açamadığı videolar için: Windows'ta codec'i
/// olan her video. Kendi STA iş parçacığında çalışır; hata, codec yokluğu ya da dosya yoksa null döner, asla atmaz.
/// </summary>
public static class ShellThumbnail
{
    const int SIIGBF_BIGGERSIZEOK = 0x1;
    const int SIIGBF_THUMBNAILONLY = 0x8; // yalnız gerçek küçük resim: simge gelmesin, yer tutucu gösterilsin

    public static BitmapSource? Get(string path, int size)
    {
        if (!File.Exists(path)) return null;
        BitmapSource? result = null;
        var t = new Thread(() => result = GetOnSta(path, size)) { IsBackground = true, Name = "Kabuk küçük resmi" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return t.Join(TimeSpan.FromSeconds(15)) ? result : null;
    }

    static BitmapSource? GetOnSta(string path, int size)
    {
        IntPtr hbmp = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory is null) return null;
            try
            {
                if (factory.GetImage(new NativeSize { Cx = size, Cy = size }, SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY, out hbmp) != 0
                    || hbmp == IntPtr.Zero) return null;
                var b = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                b.Freeze();
                return b;
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        catch (Exception) { return null; }
        finally { if (hbmp != IntPtr.Zero) DeleteObject(hbmp); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize { public int Cx, Cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
}
