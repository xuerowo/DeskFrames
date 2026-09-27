namespace DeskFrames;

/// <summary>
/// 將視窗「釘」在桌面層：
///  1. 擁有者（owner）設為承載桌面圖示的 Explorer 視窗 → Win+D「顯示桌面」後仍可見。
///  2. Z 順序置底 → 位於桌面圖示之上、所有一般視窗之下，絕不覆蓋其他視窗。
///  3. 攔截 WM_WINDOWPOSCHANGING，防止被點擊/啟用時跑到其他視窗前面。
/// </summary>
public static class DesktopHost
{
    public static IntPtr Host { get; private set; }

    /// <summary>為 true 時允許本程式自行調整 Z 順序（由本類別內部設定）。</summary>
    private static bool _allowZ;

    /// <summary>尋找承載 SHELLDLL_DefView（桌面圖示清單）的頂層視窗。找不到回傳 IntPtr.Zero。</summary>
    public static IntPtr Find()
    {
        IntPtr progman = Native.FindWindow("Progman", null);
        if (progman != IntPtr.Zero && Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return progman;

        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, _) =>
        {
            if (Native.ClassNameOf(h) == "WorkerW" && Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found != IntPtr.Zero ? found : progman;
    }

    /// <summary>重新尋找桌面承載視窗；回傳是否有變更。</summary>
    public static bool Refresh()
    {
        var h = Find();
        bool changed = h != Host;
        Host = h;
        return changed;
    }

    public static bool Ready => Host != IntPtr.Zero && Native.IsWindow(Host);

    /// <summary>設定擁有者為桌面並置底。</summary>
    public static void Pin(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        if (Ready) Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, Host);
        SendToBottom(hwnd);
    }

    /// <summary>
    /// 放到桌面承載視窗的正上方（= 所有一般視窗之下）。
    /// 注意：對「被擁有」的視窗使用 HWND_BOTTOM 無效，必須以桌面正上方的視窗作為插入點。
    /// </summary>
    public static void SendToBottom(IntPtr hwnd) => SendToBottom(hwnd, false);

    /// <summary>顯示並同時置底（ShowWindow 會把被擁有的視窗提到最上層，因此必須一次完成）。</summary>
    public static void ShowAtBottom(IntPtr hwnd) => SendToBottom(hwnd, true);

    private static void SendToBottom(IntPtr hwnd, bool show)
    {
        IntPtr after = Native.HWND_BOTTOM;
        if (Ready)
        {
            after = Native.GetWindow(Host, Native.GW_HWNDPREV);
            while (after == hwnd) after = Native.GetWindow(after, Native.GW_HWNDPREV);
            if (after == IntPtr.Zero) after = Native.HWND_TOP; // 桌面已是最上層（例如 Win+D 狀態）
        }
        _allowZ = true;
        try
        {
            Native.SetWindowPos(hwnd, after, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER |
                (show ? Native.SWP_SHOWWINDOW : 0));
        }
        finally { _allowZ = false; }
    }

    /// <summary>將 hwnd 放到 target 的正上方。</summary>
    public static void PlaceAbove(IntPtr hwnd, IntPtr target)
    {
        var after = Native.GetWindow(target, Native.GW_HWNDPREV);
        if (after == hwnd) return; // 已經在正上方
        PlaceBelow(hwnd, after == IntPtr.Zero ? Native.HWND_TOP : after);
    }

    /// <summary>將 hwnd 放到 insertAfter 的正下方。</summary>
    public static void PlaceBelow(IntPtr hwnd, IntPtr insertAfter)
    {
        _allowZ = true;
        try
        {
            Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
        finally { _allowZ = false; }
    }

    /// <summary>
    /// 在釘選視窗的 WndProc 中呼叫：阻止視窗被提升到最上層（點擊、啟用時）。
    /// 回傳 true 表示已修改訊息參數。
    /// </summary>
    public static unsafe void GuardZOrder(ref Message m)
    {
        if (m.Msg != Native.WM_WINDOWPOSCHANGING || _allowZ || m.LParam == IntPtr.Zero) return;
        var wp = (Native.WINDOWPOS*)m.LParam;
        if ((wp->flags & Native.SWP_NOZORDER) != 0) return;
        if (wp->hwndInsertAfter == Native.HWND_TOP || wp->hwndInsertAfter == Native.HWND_TOPMOST)
            wp->flags |= Native.SWP_NOZORDER;
    }

    /// <summary>切換「顯示桌面」（等同 Win+D）。</summary>
    public static void ToggleDesktop()
    {
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return;
            object? shell = Activator.CreateInstance(t);
            t.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            if (shell != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        catch { }
    }
}
