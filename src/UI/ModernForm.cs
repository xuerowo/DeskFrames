namespace DeskFrames;

/// <summary>
/// 無邊框現代化視窗：保留 DWM 陰影與 Win11 圓角（WS_THICKFRAME + WM_NCCALCSIZE=0），自繪標題列。
/// 內容放在 <see cref="Body"/>（垂直堆疊、可捲動）與 <see cref="Footer"/>（按鈕列）。
/// </summary>
public class ModernForm : Form
{
    protected readonly VStack Body = new();
    protected readonly ButtonBar Footer = new() { AlignRight = true, Visible = false };
    private readonly ModernButton _close = new() { Glyph = Glyph.Close, Kind = ButtonKind.Ghost, GlyphSize = 9f, Height96 = 32 };

    protected int CaptionHeight96 { get; set; } = 48;

    private bool _hasFooter;

    /// <summary>是否顯示底部按鈕列。（不可用 Footer.Visible 判斷：視窗顯示前子控制項一律回報不可見。）</summary>
    protected bool HasFooter
    {
        get => _hasFooter;
        set { _hasFooter = value; Footer.Visible = value; PerformLayout(); }
    }
    public bool Resizable { get; set; }

    public ModernForm()
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Font = Theme.Ui(9.5f);
        try { using var s = typeof(ModernForm).Assembly.GetManifestResourceStream("app.ico"); if (s != null) Icon = new Icon(s); } catch { }

        _close.Click += (_, _) => Close();
        Controls.Add(Body);
        Controls.Add(Footer);
        Controls.Add(_close);
        Body.HandleCreated += (_, _) => ThemeScrollbar();
        ApplyTheme();
        Theme.Changed += ApplyTheme;
    }

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Body.BackColor = Theme.Bg;
        Footer.BackColor = Theme.Bg;
        if (IsHandleCreated) Theme.ApplyWindowChrome(Handle);
        ThemeScrollbar();
        Invalidate(true);
    }

    /// <summary>深色模式下使用深色捲軸。</summary>
    private void ThemeScrollbar()
    {
        if (Body.IsHandleCreated) Native.SetWindowTheme(Body.Handle, Theme.Dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    protected override void Dispose(bool disposing)
    {
        Theme.Changed -= ApplyTheme;
        base.Dispose(disposing);
    }

    protected float S(float v) => v * DeviceDpi / 96f;

    /// <summary>離屏預覽用：把內容捲到指定位置。</summary>
    internal void ScrollBodyTo(int y) => Body.AutoScrollPosition = new Point(0, y);

    /// <summary>以 DIP 設定視窗大小。</summary>
    protected void SetSize96(int w, int h) => Size = new Size((int)S(w), (int)S(h));

    /// <summary>依內容自動決定高度（上限為工作區 85%）。</summary>
    protected void FitHeightToContent(int width96)
    {
        int h = CaptionHeight96 + Body.ContentHeight96() + (HasFooter ? Footer.Height96 + 24 : 0);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size((int)S(width96), Math.Min((int)S(h), (int)(wa.Height * 0.85)));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(Handle);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 正在文字框中輸入時，Esc 交給文字框（取消輸入），不關閉視窗
        if (e.KeyCode == Keys.Escape && ActiveControl is not TextBoxBase) { Close(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int cap = (int)S(CaptionHeight96);
        int b = (int)S(32);
        _close.SetBounds(ClientSize.Width - b - (int)S(10), (cap - b) / 2, b, b);
        int footerH = HasFooter ? (int)S(Footer.Height96) : 0;
        int pad = (int)S(20);
        if (HasFooter)
            Footer.SetBounds(pad, ClientSize.Height - footerH - (int)S(14), ClientSize.Width - pad * 2, footerH);
        Body.SetBounds(0, cap, ClientSize.Width, ClientSize.Height - cap - (HasFooter ? footerH + (int)S(24) : 0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);
        int cap = (int)S(CaptionHeight96);
        TextRenderer.DrawText(g, Text, Theme.Ui(11f, FontStyle.Bold), new Rectangle((int)S(20), 0, ClientSize.Width - (int)S(80), cap),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (HasFooter)
        {
            using var pen = new Pen(Theme.Border);
            int y = Footer.Top - (int)S(12);
            g.DrawLine(pen, 0, y, ClientSize.Width, y);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            m.Result = IntPtr.Zero; // 客戶區 = 整個視窗
            return;
        }
        if (m.Msg == Native.WM_NCHITTEST)
        {
            var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
            m.Result = HitTest(p);
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>非客戶區判定：邊緣縮放、標題列拖曳。</summary>
    protected virtual IntPtr HitTest(Point p)
    {
        int edge = (int)S(6);
        if (Resizable)
        {
            bool l = p.X < edge, r = p.X >= ClientSize.Width - edge, t = p.Y < edge, b = p.Y >= ClientSize.Height - edge;
            if (t && l) return Native.HTTOPLEFT;
            if (t && r) return Native.HTTOPRIGHT;
            if (b && l) return Native.HTBOTTOMLEFT;
            if (b && r) return Native.HTBOTTOMRIGHT;
            if (l) return Native.HTLEFT;
            if (r) return Native.HTRIGHT;
            if (t) return Native.HTTOP;
            if (b) return Native.HTBOTTOM;
        }
        if (p.Y < S(CaptionHeight96)) return Native.HTCAPTION;
        return Native.HTCLIENT;
    }
}
