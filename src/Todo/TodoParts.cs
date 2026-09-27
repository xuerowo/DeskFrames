using System.Runtime.InteropServices;

namespace DeskFrames;

/// <summary>待辦小工具共用工具：依視窗 DPI 取得正確大小的字型。</summary>
internal static class TodoUi
{
    private static float _sysDpi;

    /// <summary>系統 DPI（GDI 以此將點數轉為像素）。</summary>
    public static float SystemDpi
    {
        get
        {
            if (_sysDpi <= 0)
            {
                try { using var g = Graphics.FromHwnd(IntPtr.Zero); _sysDpi = g.DpiX > 0 ? g.DpiX : 96f; }
                catch { _sysDpi = 96f; }
            }
            return _sysDpi;
        }
    }

    /// <summary>
    /// Theme 字型以「點」為單位並依系統 DPI 轉像素；視窗位於不同 DPI 的螢幕時需補償，
    /// 字級四捨五入到 0.25pt 以避免快取過多字型。
    /// </summary>
    private static float Pt(int dpi, float pt) => MathF.Round(pt * dpi / SystemDpi * 4f) / 4f;

    public static Font Font(int dpi, float pt, FontStyle st = FontStyle.Regular) => Theme.Ui(Pt(dpi, pt), st);
    public static Font Icon(int dpi, float pt) => Theme.Icon(Pt(dpi, pt));

    /// <summary>字型換算成 ModernButton 等使用 Theme 字型控制項的點數。</summary>
    public static float PtFor(int dpi, float pt) => Pt(dpi, pt);

    /// <summary>將多行文字壓成單行並去除前後空白。</summary>
    public static string Clean(string? s) =>
        string.Join(' ', (s ?? "").Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
}

/// <summary>
/// 無框輸入框：Enter → <see cref="Submit"/>、Esc → <see cref="Cancel"/>，
/// 並以主題色自行繪製提示文字（取得焦點時仍顯示）。
/// </summary>
internal sealed class TodoTextBox : TextBox
{
    private const int WM_PAINT = 0x000F, EM_SETMARGINS = 0x00D3, EC_LEFTRIGHT = 0x0003;

    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

    public event Action? Submit;
    public event Action? Cancel;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Hint { get; set; } = "";

    public TodoTextBox()
    {
        BorderStyle = BorderStyle.None;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter) { Submit?.Invoke(); return true; }
        if (keyData == Keys.Escape) { Cancel?.Invoke(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // 邊距歸零：讓編輯中的文字與自繪文字位置一致
    private void ZeroMargins()
    {
        if (IsHandleCreated) SendMessage(Handle, EM_SETMARGINS, (IntPtr)EC_LEFTRIGHT, IntPtr.Zero);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ZeroMargins(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ZeroMargins(); }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (Hint.Length > 0 && TextLength <= 1) Invalidate();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT && Hint.Length > 0 && TextLength == 0 && IsHandleCreated)
        {
            using var g = Graphics.FromHwnd(Handle);
            TextRenderer.DrawText(g, Hint, Font, ClientRectangle, TodoTheme.TextFaint,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>底部「新增待辦」圓角輸入列：左側 + 圖示、內嵌無框 TextBox。</summary>
internal sealed class TodoInputField : Control
{
    public readonly TodoTextBox Box = new() { Hint = "新增待辦事項…", MaxLength = 500 };

    /// <summary>點擊左側 + 圖示。</summary>
    public event Action? AddClicked;

    private bool _hover, _hoverGlyph;

    public TodoInputField()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.IBeam;
        Controls.Add(Box);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Box.TextChanged += (_, _) => { if (Box.TextLength <= 1) Invalidate(); };
        Box.MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        Box.MouseLeave += (_, _) => { _hover = ClientRectangle.Contains(PointToClient(Cursor.Position)); Invalidate(); };
        ApplyTheme();
    }

    private float S(float v) => v * DeviceDpi / 96f;
    private Rectangle GlyphRect => new(0, 0, (int)S(36), Height);

    public void ApplyTheme()
    {
        Box.BackColor = TodoTheme.Field;
        Box.ForeColor = TodoTheme.Text;
        Box.Invalidate();
        Invalidate();
    }

    /// <summary>DPI 改變時更新字型與排版。</summary>
    public void ApplyMetrics(Font font)
    {
        if (!ReferenceEquals(Box.Font, font)) Box.Font = font;
        PerformLayout();
        Invalidate();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = (int)S(36);
        Box.SetBounds(x, (Height - Box.Height) / 2, Math.Max(10, Width - x - (int)S(12)), Box.Height);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool g = GlyphRect.Contains(e.Location);
        if (g != _hoverGlyph || !_hover) { _hoverGlyph = g; _hover = true; Cursor = g ? Cursors.Hand : Cursors.IBeam; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = Box.ClientRectangle.Contains(Box.PointToClient(Cursor.Position));
        _hoverGlyph = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (GlyphRect.Contains(e.Location)) AddClicked?.Invoke();
        Box.Focus();
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Parent?.BackColor ?? TodoTheme.Bg);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        bool focused = Box.Focused;
        var r = new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f);
        using (var path = Theme.RoundRect(r, S(10)))
        {
            using (var b = new SolidBrush(TodoTheme.Field)) g.FillPath(b, path);
            var border = focused ? TodoTheme.Accent : _hover ? Theme.Blend(TodoTheme.Border, TodoTheme.Text, .18f) : TodoTheme.Border;
            using var pen = new Pen(border, focused ? Math.Max(1f, S(1.5f)) : 1f);
            g.DrawPath(pen, path);
        }

        // 左側 + 圖示（滑鼠停留時顯示圓角底色）
        var gr = new RectangleF(S(6), (Height - S(26)) / 2f, S(26), S(26));
        if (_hoverGlyph)
        {
            using var hp = Theme.RoundRect(gr, S(6));
            using var hb = new SolidBrush(TodoTheme.SurfaceHover);
            g.FillPath(hb, hp);
        }
        var gc = _hoverGlyph || focused || Box.TextLength > 0 ? TodoTheme.Accent : TodoTheme.TextDim;
        TextRenderer.DrawText(g, Glyph.Add, TodoUi.Icon(DeviceDpi, 10f), Rectangle.Round(gr), gc,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
