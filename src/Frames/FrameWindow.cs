namespace DeskFrames;

/// <summary>接收框線視窗滑鼠事件的控制器。</summary>
public interface IFrameHost
{
    bool EditMode { get; }
    void FrameMouseDown(FrameWindow w, Point screen, bool doubleClick, bool shift);
    void FrameMouseMove(FrameWindow w, Point screen);
    void FrameMouseUp(FrameWindow w, Point screen);
    void FrameRightClick(FrameWindow w, Point screen);
    void FrameMouseLeave(FrameWindow w);
    int CursorFor(FrameWindow w, Point screen);
}

/// <summary>
/// 單一框線的原生分層視窗。一般狀態：WS_EX_TRANSPARENT（完全點穿，不影響桌面操作）；
/// 編輯狀態：可接收滑鼠（透明像素仍會點穿）。視窗大小僅涵蓋框線本身，記憶體占用極小。
/// </summary>
public sealed class FrameWindow : NativeWindow, IDisposable
{
    private readonly IFrameHost _host;
    private readonly LayeredSurface _surface = new();
    private readonly bool _preview;
    private bool _edit, _tracking, _visible = true;

    public FrameModel Model { get; set; }
    public FrameLayout Layout { get; private set; } = new();
    public bool Selected { get; set; }
    public bool Hovered { get; set; }
    public HitPart Hover { get; set; } = HitPart.None;

    public FrameWindow(FrameModel model, IFrameHost host, bool preview = false)
    {
        Model = model;
        _host = host;
        _preview = preview;
        Layout = FrameLayout.Compute(model);
        var w = Layout.Window;
        int ex = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
        if (preview) ex |= Native.WS_EX_TOPMOST;
        var cp = new CreateParams
        {
            Caption = "DeskFrames",
            Style = Native.WS_POPUP,
            ExStyle = ex,
            ClassStyle = Native.CS_DBLCLKS,
            X = w.X, Y = w.Y, Width = w.Width, Height = w.Height,
            Parent = !preview && DesktopHost.Ready ? DesktopHost.Host : IntPtr.Zero,
        };
        CreateHandle(cp);
        Render();
        if (preview) Native.ShowWindow(Handle, Native.SW_SHOWNA);
        else DesktopHost.ShowAtBottom(Handle);
    }

    public bool EditMode
    {
        get => _edit;
        set
        {
            if (_edit == value) return;
            _edit = value;
            var ex = (long)Native.GetWindowLongPtr(Handle, Native.GWL_EXSTYLE);
            ex = value ? ex & ~Native.WS_EX_TRANSPARENT : ex | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(Handle, Native.GWL_EXSTYLE, new IntPtr(ex));
            if (!value) { Hover = HitPart.None; Hovered = false; Selected = false; }
            Render();
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            if (!value) Native.ShowWindow(Handle, Native.SW_HIDE);
            else if (_preview) Native.ShowWindow(Handle, Native.SW_SHOWNA);
            else DesktopHost.ShowAtBottom(Handle);
        }
    }

    /// <summary>重新計算配置並重繪。</summary>
    public void Render()
    {
        if (Handle == IntPtr.Zero) return;
        Layout = FrameLayout.Compute(Model);
        var w = Layout.Window;
        using (var g = _surface.Begin(w.Width, w.Height))
        {
            g.TranslateTransform(-w.X, -w.Y);
            var rs = new RenderState
            {
                Edit = _edit,
                Selected = Selected,
                ShowHandles = Selected || Hovered,
                Hover = Hover,
            };
            FrameRenderer.Draw(g, Model, Layout, rs);
        }
        _surface.Commit(Handle, w.X, w.Y);
        if (!_edit) _surface.Release(); // 靜態顯示時不保留畫布
    }

    /// <summary>重新套用桌面擁有者與置底（Explorer 重啟後）。</summary>
    public void RePin()
    {
        if (_preview || Handle == IntPtr.Zero) return;
        DesktopHost.Pin(Handle);
    }

    /// <summary>滑鼠訊息的 lParam（客戶區座標）轉為螢幕座標。</summary>
    private Point MsgPoint(Message m)
    {
        long lp = (long)m.LParam;
        return new Point(Layout.Window.X + unchecked((short)(lp & 0xFFFF)), Layout.Window.Y + unchecked((short)((lp >> 16) & 0xFFFF)));
    }

    public bool Alive => Handle != IntPtr.Zero && Native.IsWindow(Handle);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_DPICHANGED:
                Render();
                m.Result = IntPtr.Zero;
                return;
            case Native.WM_MOUSEACTIVATE:
                m.Result = new IntPtr(Native.MA_NOACTIVATE);
                return;
            case Native.WM_WINDOWPOSCHANGING:
                if (!_preview) DesktopHost.GuardZOrder(ref m);
                break;
            case Native.WM_SETCURSOR:
                if (_edit)
                {
                    Native.SetCursorId(_host.CursorFor(this, Native.CursorPos()));
                    m.Result = new IntPtr(1);
                    return;
                }
                break;
            case Native.WM_MOUSEMOVE:
                if (_edit)
                {
                    if (!_tracking)
                    {
                        var tme = new Native.TRACKMOUSEEVENT
                        {
                            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.TRACKMOUSEEVENT>(),
                            dwFlags = Native.TME_LEAVE, hwndTrack = Handle,
                        };
                        _tracking = Native.TrackMouseEvent(ref tme);
                    }
                    _host.FrameMouseMove(this, MsgPoint(m));
                }
                return;
            case Native.WM_MOUSELEAVE:
                _tracking = false;
                if (_edit) _host.FrameMouseLeave(this);
                return;
            case Native.WM_LBUTTONDOWN:
                if (_edit) _host.FrameMouseDown(this, MsgPoint(m), false, ((long)m.WParam & Native.MK_SHIFT) != 0);
                return;
            case Native.WM_LBUTTONDBLCLK:
                if (_edit) _host.FrameMouseDown(this, MsgPoint(m), true, ((long)m.WParam & Native.MK_SHIFT) != 0);
                return;
            case Native.WM_LBUTTONUP:
                if (_edit) _host.FrameMouseUp(this, MsgPoint(m));
                return;
            case Native.WM_CAPTURECHANGED:
                if (_edit) _host.FrameMouseUp(this, Native.CursorPos());
                break;
            case Native.WM_RBUTTONUP:
                if (_edit) _host.FrameRightClick(this, MsgPoint(m));
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
        _surface.Dispose();
    }
}
