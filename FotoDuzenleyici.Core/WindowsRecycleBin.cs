using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace FotoDuzenleyici.Core;

/// <summary>
/// Gerçek Geri Dönüşüm Kutusu. <see cref="CanRecycle"/> kutunun dosyayı alıp almayacağını önceden söyler.
/// <see cref="Send"/> Windows'un IFileOperation'ını kullanır: silmeden hemen önce Windows dosyayı kutuya mı
/// koyacağını, yoksa kalıcı mı sileceğini bildirir; kutuya gitmeyecekse işlem reddedilir ve dosya yerinde kalır
/// ("atılamadı"). Hiçbir Windows iletişim kutusu açılmaz; kalıcı silme hiçbir koşulda olmaz.
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
        catch (Exception) { return true; } // bilinmiyor: Send yine de kalıcı silmeye izin vermez
    }

    /// <summary>Windows silmeden önce bu bayrakla "kutuya koyacağım" der; yoksa silme kalıcı olurdu.</summary>
    internal static bool IsRecycle(uint preDeleteFlags) => (preDeleteFlags & TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0;

    public bool Send(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception) { return false; }
        if (!File.Exists(full)) return false;

        // Kabuk COM nesneleri tek iş parçacıklı (STA) bir iş parçacığı ister; arka plan havuzu MTA'dır.
        bool ok = false;
        var t = new Thread(() => ok = SendOnSta(full)) { IsBackground = true, Name = "Geri Dönüşüm Kutusu" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return ok && !File.Exists(full);
    }

    static bool SendOnSta(string full)
    {
        IFileOperation? op = null;
        IShellItem? item = null;
        try
        {
            op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FileOperation, throwOnError: true)!)!;
            op.SetOperationFlags(FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE);
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(full, IntPtr.Zero, ref iid, out item) != 0 || item is null) return false;
            var sink = new Sink();
            op.DeleteItem(item, sink);
            int hr = op.PerformOperations();
            op.GetAnyOperationsAborted(out bool aborted);
            return hr == 0 && !aborted && !sink.Vetoed;
        }
        catch (Exception) { return false; }
        finally
        {
            if (item is not null) Marshal.ReleaseComObject(item);
            if (op is not null) Marshal.ReleaseComObject(op);
        }
    }

    /// <summary>Kutuya gitmeyecek her silmeyi reddeder (kalıcı silme yerine "atılamadı").</summary>
    [ComVisible(true)]
    sealed class Sink : IFileOperationProgressSink
    {
        public bool Vetoed;
        public int StartOperations() => 0;
        public int FinishOperations(int hrResult) => 0;
        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName) => 0;
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string? pszNewName, int hrRename, IShellItem? psiNewlyCreated) => 0;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName) => 0;
        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName, int hrMove, IShellItem? psiNewlyCreated) => 0;
        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName) => 0;
        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated) => 0;
        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            if (IsRecycle(dwFlags)) return 0;
            Vetoed = true;
            return ERROR_CANCELLED_HR;
        }
        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated) => 0;
        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName) => 0;
        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string? pszNewName, string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem) => 0;
        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) => 0;
        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;
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

    static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    const uint FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;
    const uint FOFX_RECYCLEONDELETE = 0x00080000;
    const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;
    const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7); // HRESULT_FROM_WIN32(ERROR_CANCELLED)

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem { }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hrResult);
        [PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrRename, IShellItem? psiNewlyCreated);
        [PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrMove, IShellItem? psiNewlyCreated);
        [PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, int hrCopy, IShellItem? psiNewlyCreated);
        [PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);
        [PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem? psiNewlyCreated);
        [PreserveSig] int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName);
        [PreserveSig] int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem? psiNewItem);
        [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    /// <summary>Yalnız kullanılan yöntemlerin sırası önemli: vtable düzeni shobjidl_core.h ile aynı.</summary>
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
        [PreserveSig] int Unadvise(uint dwCookie);
        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        [PreserveSig] int SetProgressDialog(IntPtr popd);
        [PreserveSig] int SetProperties(IntPtr pproparray);
        [PreserveSig] int SetOwnerWindow(IntPtr hwndOwner);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        [PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);
        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);
        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);
        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        [PreserveSig] int DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);
        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IFileOperationProgressSink? pfopsItem);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder name, int size);
}
