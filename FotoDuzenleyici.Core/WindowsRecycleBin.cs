using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Gerçek Geri Dönüşüm Kutusu. <see cref="CanRecycle"/> kutunun dosyayı alıp almayacağını önceden söyler;
/// <see cref="Send"/> Windows'tan "kalıcı silinecekse sor" diye ister (FOF_WANTNUKEWARNING). Hayır denirse dosya
/// yerinde kalır ve gönderim başarısız sayılır.
/// </summary>
public sealed class WindowsRecycleBin : IRecycleBin
{
    public static WindowsRecycleBin Instance { get; } = new();

    WindowsRecycleBin() { }

    public bool CanRecycle(string path, long size)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false; // ağ yolu: kutu yok
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            var type = new DriveInfo(root).DriveType;
            if (type is DriveType.Removable or DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory or DriveType.Unknown)
                return false;

            var guid = VolumeGuid(root);
            if (guid is null) return true;
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid);
            if (key is null) return true;
            if (key.GetValue("NukeOnDelete") is int nuke && nuke != 0) return false;
            if (key.GetValue("MaxCapacity") is int maxMb)
            {
                long used = 0;
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
                if (SHQueryRecycleBinW(root, ref info) == 0) used = info.i64Size;
                if ((long)maxMb * 1024 * 1024 - used < size) return false;
            }
            return true;
        }
        catch (Exception) { return true; } // bilinmiyor: Send yine de sormadan kalıcı silmez
    }

    public bool Send(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = Path.GetFullPath(path) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING | FOF_SILENT,
        };
        int rc = SHFileOperationW(ref op);
        return rc == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
    }

    /// <summary>"C:\" → "{guid}" (BitBucket kayıt anahtarının adı).</summary>
    static string? VolumeGuid(string root)
    {
        var sb = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(root, sb, sb.Capacity)) return null;
        var s = sb.ToString(); // \\?\Volume{...}\
        int a = s.IndexOf('{'), b = s.IndexOf('}');
        return a >= 0 && b > a ? s[a..(b + 1)] : null;
    }

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder name, int size);
}
