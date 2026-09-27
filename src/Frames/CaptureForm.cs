namespace DeskFrames;

/// <summary>繪製模式時覆蓋單一螢幕的半透明遮罩，負責接收拖曳（每個螢幕一個，避免混合 DPI 問題）。</summary>
public sealed class CaptureForm : Form
{
    private readonly Rectangle _bounds;

    public CaptureForm(Rectangle screenBounds)
    {
        _bounds = screenBounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        BackColor = Color.FromArgb(8, 10, 20);
        Opacity = 0.28;
        Cursor = Cursors.Cross;
        Bounds = screenBounds;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
        base.WndProc(ref m);
        if (m.Msg == Native.WM_DPICHANGED) Bounds = _bounds; // 保持覆蓋整個螢幕
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _bounds;
    }
}
