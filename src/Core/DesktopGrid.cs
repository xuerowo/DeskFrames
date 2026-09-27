using System.Runtime.InteropServices;

namespace DeskFrames;

/// <summary>
/// 讀取桌面圖示格線（透過 Shell 的 IFolderView：圖示間距 + 桌面清單原點）。
/// 桌面圖示排在「原點 + k × 間距」的格子內，格子邊界即圖示之間的縫隙；框線吸附在這些縫隙上就不會切過圖示或文字。
/// 只讀取，不會移動任何圖示。
/// </summary>
public static class DesktopGrid
{
    public static Point Origin { get; private set; }
    public static Size Spacing { get; private set; }
    public static bool Available => Spacing.Width >= 32 && Spacing.Height >= 32;

    /// <summary>重新讀取格線（桌面圖示大小或間距可能已改變）。失敗時 Available = false。</summary>
    public static void Refresh()
    {
        try
        {
            var shellWindows = (IShellWindows)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!)!;
            object loc = 0 /*CSIDL_DESKTOP*/, root = null!;
            var disp = shellWindows.FindWindowSW(ref loc, ref root, 8 /*SWC_DESKTOP*/, out _, 1 /*SWFO_NEEDDISPATCH*/);
            var sp = (IServiceProvider)disp;
            Guid sid = new("4C96BE40-915C-11CF-99D3-00AA004AE837"), iidBrowser = typeof(IShellBrowser).GUID;
            if (sp.QueryService(ref sid, ref iidBrowser, out IntPtr pBrowser) != 0) { Spacing = Size.Empty; return; }
            var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pBrowser);
            Marshal.Release(pBrowser);
            if (browser.QueryActiveShellView(out IntPtr pView) != 0) { Spacing = Size.Empty; return; }
            Guid iidFv = typeof(IFolderView).GUID;
            int hr = Marshal.QueryInterface(pView, in iidFv, out IntPtr pFv);
            Marshal.Release(pView);
            if (hr != 0) { Spacing = Size.Empty; return; }
            var fv = (IFolderView)Marshal.GetObjectForIUnknown(pFv);
            Marshal.Release(pFv);
            var pt = new FvPoint();
            if (fv.GetSpacing(ref pt) != 0) { Spacing = Size.Empty; return; }
            Spacing = new Size(pt.X, pt.Y);

            // 原點：桌面圖示清單（SysListView32）客戶區的左上角
            var origin = new Native.POINT(0, 0);
            var defView = Native.FindWindowEx(DesktopHost.Host, IntPtr.Zero, "SHELLDLL_DefView", null);
            var list = defView != IntPtr.Zero ? Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null) : IntPtr.Zero;
            if (list != IntPtr.Zero) Native.ClientToScreen(list, ref origin);
            Origin = new Point(origin.X, origin.Y);
        }
        catch
        {
            Spacing = Size.Empty;
        }
    }

    /// <summary>最接近的直向格線（x）。</summary>
    public static int SnapX(int x) => Origin.X + (int)Math.Round((x - Origin.X) / (double)Spacing.Width) * Spacing.Width;
    /// <summary>最接近的橫向格線（y）。</summary>
    public static int SnapY(int y) => Origin.Y + (int)Math.Round((y - Origin.Y) / (double)Spacing.Height) * Spacing.Height;
    /// <summary>不大於 v 的格線。</summary>
    public static int FloorX(int x) => Origin.X + (int)Math.Floor((x - Origin.X) / (double)Spacing.Width) * Spacing.Width;
    public static int FloorY(int y) => Origin.Y + (int)Math.Floor((y - Origin.Y) / (double)Spacing.Height) * Spacing.Height;

    // ---------------- COM 介面 ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct FvPoint { public int X, Y; }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
        int RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass);
        void Revoke(int cookie);
        void OnNavigate(int cookie, ref object loc);
        void OnActivated(int cookie, bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options);
    }

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid sid, ref Guid iid, out IntPtr obj);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // 前 12 個方法本程式不使用，僅為保持 vtable 順序
        void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB();
        void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject();
        void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        [PreserveSig] int QueryActiveShellView(out IntPtr view);
    }

    [ComImport, Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        [PreserveSig] int GetCurrentViewMode(out uint mode);
        [PreserveSig] int SetCurrentViewMode(uint mode);
        [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
        [PreserveSig] int Item(int index, out IntPtr pidl);
        [PreserveSig] int ItemCount(uint flags, out int count);
        [PreserveSig] int Items(uint flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetSelectionMarkedItem(out int item);
        [PreserveSig] int GetFocusedItem(out int item);
        [PreserveSig] int GetItemPosition(IntPtr pidl, out FvPoint pt);
        [PreserveSig] int GetSpacing(ref FvPoint pt);
    }
}
