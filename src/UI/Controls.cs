using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace DeskFrames;

/// <summary>自繪控制項基底：雙緩衝、DPI 縮放、主題切換自動重繪。</summary>
public abstract partial class ModernControl : Control
{
    protected bool Hover, Pressed;

    protected ModernControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Theme.Changed += OnThemeChanged;
        Cursor = Cursors.Hand;
    }

    /// <summary>此控制項在 96 DPI 下的建議高度，供 VStack 排版使用。</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public virtual int DipHeight => 36;

    protected float S(float v) => v * DeviceDpi / 96f;

    private void OnThemeChanged() { if (!IsDisposed) Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        Theme.Changed -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var bg = Parent?.BackColor ?? Theme.Bg;
        e.Graphics.Clear(bg);
    }

    protected static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags)
        => TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
}

public enum ButtonKind { Secondary, Primary, Ghost, Danger }

internal static class Measure96Holder
{
    /// <summary>96 DPI 的量測用 Graphics（量得的即為 DIP）。</summary>
    public static readonly Graphics G = Create();

    private static Graphics Create()
    {
        var bmp = new Bitmap(1, 1);
        bmp.SetResolution(96, 96); // 新點陣圖預設沿用螢幕 DPI，必須固定為 96 才能量出 DIP
        return Graphics.FromImage(bmp);
    }
}

public abstract partial class ModernControl
{
    protected static Graphics Measure96 => Measure96Holder.G;
}

/// <summary>圓角按鈕，可含圖示字符與文字。</summary>
public class ModernButton : ModernControl
{
    private string _glyph = "";
    private ButtonKind _kind = ButtonKind.Secondary;
    private bool _checked;

    [DefaultValue("")]
    public string Glyph { get => _glyph; set { _glyph = value; Invalidate(); } }

    [DefaultValue(ButtonKind.Secondary)]
    public ButtonKind Kind { get => _kind; set { _kind = value; Invalidate(); } }

    /// <summary>切換狀態（用於工具列的模式按鈕）。</summary>
    [DefaultValue(false)]
    public bool Checked { get => _checked; set { _checked = value; Invalidate(); } }

    [DefaultValue(8f)] public float Radius { get; set; } = 8f;
    /// <summary>覆寫色彩（Ghost 按鈕用；例如待辦清單的自訂樣式）。</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? ForeOverride { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? HoverOverride { get; set; }
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? PressedOverride { get; set; }
    [DefaultValue(10.5f)] public float GlyphSize { get; set; } = 10.5f;
    [DefaultValue(9.5f)] public float TextSize { get; set; } = 9.5f;
    [DefaultValue(34)] public int Height96 { get; set; } = 34;
    public override int DipHeight => Height96;

    /// <summary>依內容計算寬度（DIP）。</summary>
    public int PreferredWidth96()
    {
        int w = 0;
        if (!string.IsNullOrEmpty(Text))
            w += (int)Math.Ceiling(Measure96.MeasureString(Text, Theme.Ui(TextSize)).Width);
        if (!string.IsNullOrEmpty(Glyph)) w += 16 + (string.IsNullOrEmpty(Text) ? 0 : 7);
        return w + (string.IsNullOrEmpty(Text) ? 18 : 26);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        Color bg, fg;
        switch (Kind)
        {
            case ButtonKind.Primary:
                bg = Pressed ? Theme.Blend(Theme.Accent, Color.Black, .15f) : Hover ? Theme.AccentHover : Theme.Accent;
                fg = Theme.OnAccent; break;
            case ButtonKind.Danger:
                bg = Pressed ? Theme.Blend(Theme.Danger, Color.Black, .15f) : Hover ? Theme.Blend(Theme.Danger, Color.White, .12f) : Theme.Danger;
                fg = Color.White; break;
            case ButtonKind.Ghost:
                bg = Checked ? Theme.WithAlpha(Theme.Accent, Theme.Dark ? 60 : 40)
                    : Pressed ? PressedOverride ?? Theme.SurfacePressed : Hover ? HoverOverride ?? Theme.SurfaceHover : Color.Transparent;
                fg = Checked ? (Theme.Dark ? Theme.AccentHover : Theme.Accent) : ForeOverride ?? Theme.Text; break;
            default:
                bg = Pressed ? Theme.SurfacePressed : Hover ? Theme.SurfaceHover : Theme.Surface;
                fg = Theme.Text; break;
        }
        if (!Enabled) { fg = Theme.TextFaint; if (Kind is ButtonKind.Primary or ButtonKind.Danger) bg = Theme.SurfaceHover; }

        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        if (bg.A > 0)
        {
            using var path = Theme.RoundRect(r, S(Radius));
            using var b = new SolidBrush(bg);
            g.FillPath(b, path);
            if (Kind == ButtonKind.Secondary)
            {
                using var pen = new Pen(Theme.Border, 1f);
                g.DrawPath(pen, path);
            }
        }

        bool hasText = !string.IsNullOrEmpty(Text), hasGlyph = !string.IsNullOrEmpty(Glyph);
        var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        if (hasGlyph && hasText)
        {
            var tsz = TextRenderer.MeasureText(g, Text, Theme.Ui(TextSize), Size.Empty, TextFormatFlags.NoPadding);
            int gw = (int)S(16), gap = (int)S(7);
            int total = gw + gap + tsz.Width;
            int x = (Width - total) / 2;
            DrawText(g, Glyph, Theme.Icon(GlyphSize), fg, new Rectangle(x, 0, gw, Height), tf | TextFormatFlags.HorizontalCenter);
            DrawText(g, Text, Theme.Ui(TextSize), fg, new Rectangle(x + gw + gap, 0, tsz.Width + 4, Height), tf);
        }
        else if (hasGlyph)
            DrawText(g, Glyph, Theme.Icon(GlyphSize), fg, ClientRectangle, tf | TextFormatFlags.HorizontalCenter);
        else
            DrawText(g, Text, Theme.Ui(TextSize), fg, ClientRectangle, tf | TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>整列的切換開關：左側標題 + 說明，右側開關。</summary>
public class ToggleRow : ModernControl
{
    private bool _checked;
    private float _anim;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

    public event EventHandler? CheckedChanged;
    [DefaultValue("")] public string Description { get; set; } = "";

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated && Visible) _timer.Start(); else { _anim = value ? 1 : 0; Invalidate(); }
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override int DipHeight => string.IsNullOrEmpty(Description) ? 38 : 52;

    public ToggleRow()
    {
        _timer.Tick += (_, _) =>
        {
            float target = _checked ? 1 : 0;
            _anim += Math.Sign(target - _anim) * 0.2f;
            if (Math.Abs(_anim - target) < 0.2f) { _anim = target; _timer.Stop(); }
            Invalidate();
        };
    }

    protected override void Dispose(bool disposing) { _timer.Dispose(); base.Dispose(disposing); }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        float sw = S(40), sh = S(20);
        var track = new RectangleF(Width - sw - S(2), (Height - sh) / 2f, sw, sh);
        int textW = (int)(track.X - S(12));
        if (string.IsNullOrEmpty(Description))
            DrawText(g, Text, Theme.Ui(9.75f), Theme.Text, new Rectangle(0, 0, textW, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        else
        {
            DrawText(g, Text, Theme.Ui(9.75f), Theme.Text, new Rectangle(0, (int)S(7), textW, (int)S(20)), TextFormatFlags.EndEllipsis);
            DrawText(g, Description, Theme.Ui(8.5f), Theme.TextDim, new Rectangle(0, (int)S(28), textW, (int)S(18)), TextFormatFlags.EndEllipsis);
        }
        var on = Theme.Accent;
        var off = Hover ? Theme.Blend(Theme.Track, Theme.Text, .12f) : Theme.Track;
        using (var path = Theme.RoundRect(track, sh / 2))
        using (var b = new SolidBrush(Theme.Blend(off, on, _anim)))
            g.FillPath(b, path);
        float kd = sh - S(6);
        float kx = track.X + S(3) + (sw - kd - S(6)) * _anim;
        using (var kb = new SolidBrush(Color.White))
            g.FillEllipse(kb, kx, track.Y + S(3), kd, kd);
    }
}

/// <summary>滑桿列：標題、數值、滑軌。</summary>
public class SliderRow : ModernControl
{
    private float _value;
    private bool _drag;
    public event EventHandler? ValueChanged;
    /// <summary>拖曳結束（放開滑鼠）時觸發。</summary>
    public event EventHandler? Committed;

    [DefaultValue(0f)] public float Minimum { get; set; }
    [DefaultValue(100f)] public float Maximum { get; set; } = 100;
    [DefaultValue(1f)] public float Step { get; set; } = 1;
    [DefaultValue("{0}")] public string Format { get; set; } = "{0}";
    public override int DipHeight => 52;

    [DefaultValue(0f)]
    public float Value
    {
        get => _value;
        set
        {
            float v = Math.Clamp(value, Minimum, Maximum);
            if (Step > 0) v = MathF.Round((v - Minimum) / Step) * Step + Minimum;
            v = Math.Clamp(v, Minimum, Maximum);
            if (Math.Abs(v - _value) < 1e-4f) return;
            _value = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private RectangleF Track => new(S(8), S(32), Width - S(16), S(4));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _drag = true; SetFromX(e.X);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) SetFromX(e.X); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag) { _drag = false; Committed?.Invoke(this, EventArgs.Empty); }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        // 未取得焦點時交給父容器捲動，避免捲動頁面時誤改數值
        if (!Focused) { base.OnMouseWheel(e); return; }
        if (e is HandledMouseEventArgs h) h.Handled = true;
        Value += Math.Sign(e.Delta) * (Step > 0 ? Step : 1);
        Committed?.Invoke(this, EventArgs.Empty);
    }

    private void SetFromX(int x)
    {
        var t = Track;
        Value = Minimum + (Maximum - Minimum) * Math.Clamp((x - t.X) / t.Width, 0, 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        DrawText(g, Text, Theme.Ui(9.75f), Theme.Text, new Rectangle(0, (int)S(4), Width / 2 + 40, (int)S(20)), TextFormatFlags.EndEllipsis);
        string val = string.Format(Format, Value);
        DrawText(g, val, Theme.Ui(9f), Theme.TextDim, new Rectangle(Width / 2, (int)S(4), Width / 2, (int)S(20)), TextFormatFlags.Right);
        var t = Track;
        float frac = Maximum > Minimum ? (Value - Minimum) / (Maximum - Minimum) : 0;
        using (var p = Theme.RoundRect(t, t.Height / 2)) using (var b = new SolidBrush(Theme.Track)) g.FillPath(b, p);
        var fill = new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * frac), t.Height);
        using (var p = Theme.RoundRect(fill, t.Height / 2)) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
        float kr = S(Hover || _drag ? 8 : 7);
        float cx = t.X + t.Width * frac, cy = t.Y + t.Height / 2;
        using (var b = new SolidBrush(Theme.Dark ? Color.White : Theme.Surface)) g.FillEllipse(b, cx - kr, cy - kr, kr * 2, kr * 2);
        using (var pen = new Pen(Theme.Accent, S(2.5f))) g.DrawEllipse(pen, cx - kr, cy - kr, kr * 2, kr * 2);
    }
}

/// <summary>分段選擇器（類似 iOS segmented control）。</summary>
public class SegmentedRow : ModernControl
{
    private int _sel;
    public event EventHandler? SelectedIndexChanged;
    public string[] Items { get; set; } = Array.Empty<string>();
    public override int DipHeight => string.IsNullOrEmpty(Text) ? 34 : 68;

    [DefaultValue(0)]
    public int SelectedIndex
    {
        get => _sel;
        set { if (_sel == value) return; _sel = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }

    private int _hoverIdx = -1;

    private RectangleF Bar => string.IsNullOrEmpty(Text)
        ? new RectangleF(0.5f, 0.5f, Width - 1, Height - 1)
        : new RectangleF(0.5f, S(28), Width - 1, Height - S(34) - 1);

    private int IndexAt(Point p)
    {
        var b = Bar;
        if (!b.Contains(p) || Items.Length == 0) return -1;
        return Math.Clamp((int)((p.X - b.X) / (b.Width / Items.Length)), 0, Items.Length - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != _hoverIdx) { _hoverIdx = i; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hoverIdx = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); int i = IndexAt(e.Location); if (i >= 0) SelectedIndex = i; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        if (!string.IsNullOrEmpty(Text))
            DrawText(g, Text, Theme.Ui(9.75f), Theme.Text, new Rectangle(0, (int)S(4), Width, (int)S(20)), TextFormatFlags.EndEllipsis);
        var b = Bar;
        using (var p = Theme.RoundRect(b, S(8))) using (var br = new SolidBrush(Theme.Surface)) using (var pen = new Pen(Theme.Border))
        { g.FillPath(br, p); g.DrawPath(pen, p); }
        if (Items.Length == 0) return;
        float w = b.Width / Items.Length;
        for (int i = 0; i < Items.Length; i++)
        {
            var cell = new RectangleF(b.X + w * i + S(3), b.Y + S(3), w - S(6), b.Height - S(6));
            if (i == _sel || i == _hoverIdx)
            {
                using var p = Theme.RoundRect(cell, S(6));
                using var br = new SolidBrush(i == _sel ? Theme.Accent : Theme.SurfaceHover);
                g.FillPath(br, p);
            }
            DrawText(g, Items[i], Theme.Ui(9f), i == _sel ? Theme.OnAccent : Theme.Text, Rectangle.Round(cell),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

/// <summary>色票選擇列：預設色盤 + 自訂顏色 + 色號輸入框。</summary>
public class ColorRow : ModernControl
{
    private Color _color = Theme.Palette[0];
    private int _hoverIdx = -1;
    private bool _hexError;
    private readonly TextBox _hex = new() { BorderStyle = BorderStyle.None, MaxLength = 20, TabStop = true };
    public event EventHandler? ColorChanged;
    public override int DipHeight => 100;

    public ColorRow()
    {
        Controls.Add(_hex);
        _hex.Text = ToHex(_color);
        _hex.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitHex(); _hex.SelectAll(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; e.Handled = true; _hex.Text = ToHex(_color); _hex.SelectAll(); }
        };
        _hex.Leave += (_, _) => CommitHex();
        _hex.GotFocus += (_, _) => { Invalidate(); BeginInvoke(_hex.SelectAll); };
        _hex.LostFocus += (_, _) => Invalidate();
        _hex.TextChanged += (_, _) =>
        {
            if (_hexError) { _hexError = false; Invalidate(); }
            // 輸入滿 6 碼即套用（不必按 Enter）
            if (_hex.Focused)
            {
                var s = _hex.Text.Trim().TrimStart('#');
                if (s.Length == 6 && TryParseColor(s, out var c)) SelectedColor = c;
            }
        };
    }

    public Color SelectedColor
    {
        get => _color;
        set
        {
            if (_color.ToArgb() == value.ToArgb()) return;
            _color = value;
            if (!_hex.Focused) _hex.Text = ToHex(value);
            Invalidate();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>解析色號：#RRGGBB、RRGGBB、#RGB，或「R, G, B」。</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = Color.Empty;
        var s = (text ?? "").Trim();
        var parts = s.Split(new[] { ',', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3)
        {
            if (parts.All(p => int.TryParse(p, out int v) && v is >= 0 and <= 255))
            {
                color = Color.FromArgb(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                return true;
            }
            return false;
        }
        s = s.TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(ch => $"{ch}{ch}"));
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
        color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return true;
    }

    private void CommitHex()
    {
        if (TryParseColor(_hex.Text, out var c))
        {
            _hexError = false;
            SelectedColor = c;
            _hex.Text = ToHex(c);
        }
        else
        {
            _hexError = true;              // 格式錯誤：紅框提示並恢復原色號
            _hex.Text = ToHex(_color);
            System.Media.SystemSounds.Beep.Play();
        }
        Invalidate();
    }

    /// <summary>色號輸入框的外框位置（第三列）。</summary>
    private RectangleF HexField => new(S(46), S(66), S(120), S(28));

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _hex.Font = Theme.Ui(9.5f);
        var f = HexField;
        int h = _hex.PreferredHeight;
        _hex.SetBounds((int)(f.X + S(10)), (int)(f.Y + (f.Height - h) / 2), (int)(f.Width - S(20)), h);
    }

    private RectangleF Swatch(int i)
    {
        int count = Theme.Palette.Length + 1;
        float gap = S(8);
        float d = Math.Max(S(16), Math.Min(S(26), (Width - S(8) - gap * (count - 1)) / count));
        return new RectangleF(S(2) + i * (d + gap), S(30), d, d);
    }

    private int IndexAt(Point p)
    {
        for (int i = 0; i <= Theme.Palette.Length; i++)
        {
            var r = Swatch(i); r.Inflate(S(3), S(3));
            if (r.Contains(p)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e) { int i = IndexAt(e.Location); if (i != _hoverIdx) { _hoverIdx = i; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _hoverIdx = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int i = IndexAt(e.Location);
        if (i < 0) return;
        if (i < Theme.Palette.Length) { SelectedColor = Theme.Palette[i]; return; }
        // 自製調色盤：拖曳時即時預覽，取消則恢復原色
        var original = _color;
        var owner = FindForm();
        using var dlg = new ColorPickerForm(_color) { TopMost = owner?.TopMost ?? false };
        dlg.Preview += c => SelectedColor = c;
        SelectedColor = dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Color : original;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        DrawText(g, Text, Theme.Ui(9.75f), Theme.Text, new Rectangle(0, (int)S(4), Width, (int)S(20)), TextFormatFlags.EndEllipsis);
        bool matched = false;
        for (int i = 0; i <= Theme.Palette.Length; i++)
        {
            var r = Swatch(i);
            if (r.Right > Width) break;
            bool custom = i == Theme.Palette.Length;
            var c = custom ? _color : Theme.Palette[i];
            bool sel = custom ? !matched : c.ToArgb() == _color.ToArgb();
            if (!custom && sel) matched = true;
            if (sel || i == _hoverIdx)
            {
                var ring = RectangleF.Inflate(r, S(3), S(3));
                using var pen = new Pen(sel ? Theme.Text : Theme.TextFaint, S(2));
                g.DrawEllipse(pen, ring);
            }
            if (custom)
            {
                // 自訂色：彩虹圓 + 中央目前顏色
                using var path = new GraphicsPath();
                path.AddEllipse(r);
                using var pgb = new PathGradientBrush(path)
                {
                    CenterColor = Color.White,
                    SurroundColors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta },
                };
                g.FillEllipse(pgb, r);
                var inner = RectangleF.Inflate(r, -S(7), -S(7));
                using var b = new SolidBrush(c);
                g.FillEllipse(b, inner);
            }
            else
            {
                using var b = new SolidBrush(c);
                g.FillEllipse(b, r);
                if (c.GetBrightness() > 0.9f) { using var pen = new Pen(Theme.Border); g.DrawEllipse(pen, r); }
            }
        }

        // 第三列：色號輸入框
        var field = HexField;
        DrawText(g, "色號", Theme.Ui(9f), Theme.TextDim, new Rectangle(0, (int)field.Y, (int)S(44), (int)field.Height), TextFormatFlags.VerticalCenter);
        if (_hex.BackColor != Theme.Field) _hex.BackColor = Theme.Field;
        if (_hex.ForeColor != Theme.Text) _hex.ForeColor = Theme.Text;
        using (var path = Theme.RoundRect(field, S(6)))
        using (var bg = new SolidBrush(Theme.Field))
        using (var pen = new Pen(_hexError ? Theme.Danger : _hex.Focused ? Theme.Accent : Theme.Border, S(_hexError || _hex.Focused ? 1.5f : 1f)))
        {
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }
        DrawText(g, "例如 #5B8CFF 或 91, 140, 255", Theme.Ui(8.5f), Theme.TextFaint,
            new Rectangle((int)(field.Right + S(10)), (int)field.Y, Width - (int)(field.Right + S(10)), (int)field.Height),
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>小標題（區段名稱）。</summary>
public class SectionLabel : ModernControl
{
    public SectionLabel() { Cursor = Cursors.Default; }
    public override int DipHeight => 34;
    protected override void OnPaint(PaintEventArgs e)
    {
        DrawText(e.Graphics, Text, Theme.Ui(9f, FontStyle.Bold), Theme.Accent,
            new Rectangle(0, (int)S(12), Width, (int)S(20)), TextFormatFlags.EndEllipsis);
    }
}

/// <summary>多行說明文字。</summary>
public class NoteLabel : ModernControl
{
    public NoteLabel() { Cursor = Cursors.Default; }
    [DefaultValue(20)] public int Lines { get; set; } = 1;
    public override int DipHeight => 8 + Lines * 20;
    protected override void OnPaint(PaintEventArgs e)
    {
        DrawText(e.Graphics, Text, Theme.Ui(9f), Theme.TextDim, new Rectangle(0, (int)S(4), Width, Height - (int)S(4)),
            TextFormatFlags.WordBreak);
    }
}

/// <summary>水平排列的一組按鈕。</summary>
public class ButtonBar : Panel
{
    public ButtonBar() { DoubleBuffered = true; }
    [DefaultValue(38)] public int Height96 { get; set; } = 38;
    [DefaultValue(false)] public bool AlignRight { get; set; }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float s = DeviceDpi / 96f;
        int gap = (int)(8 * s);
        var btns = Controls.OfType<ModernButton>().ToList();
        var widths = btns.Select(b => (int)(b.PreferredWidth96() * s)).ToList();
        int total = widths.Sum() + gap * Math.Max(0, btns.Count - 1);
        int x = AlignRight ? Width - total : 0;
        for (int i = 0; i < btns.Count; i++)
        {
            btns[i].SetBounds(x, (Height - (int)(btns[i].Height96 * s)) / 2, widths[i], (int)(btns[i].Height96 * s));
            x += widths[i] + gap;
        }
    }
}

/// <summary>垂直堆疊排版容器（可捲動）。子控制項高度取自 ModernControl.DipHeight。</summary>
public class VStack : Panel
{
    [DefaultValue(4)] public int Spacing96 { get; set; } = 4;
    [DefaultValue(20)] public int Pad96 { get; set; } = 20;

    public VStack() { DoubleBuffered = true; AutoScroll = true; }

    public T Add<T>(T c) where T : Control { Controls.Add(c); return c; }

    public int ContentHeight96()
    {
        int h = Pad96;
        // 注意：視窗顯示前 c.Visible 一律為 false，因此這裡計入全部子項（本程式不使用隱藏列）
        foreach (Control c in Controls)
            h += HeightOf96(c) + Spacing96;
        return h + Pad96 - Spacing96;
    }

    private static int HeightOf96(Control c) => c switch
    {
        ModernControl m => m.DipHeight,
        ButtonBar b => b.Height96,
        _ => (int)(c.Height * 96f / c.DeviceDpi),
    };

    protected override void OnLayout(LayoutEventArgs e)
    {
        float s = DeviceDpi / 96f;
        int pad = (int)(Pad96 * s), y = pad + AutoScrollPosition.Y;
        int w = ClientSize.Width - pad * 2;
        foreach (Control c in Controls)
        {
            int h = (int)(HeightOf96(c) * s);
            c.SetBounds(pad, y, w, h);
            y += h + (int)(Spacing96 * s);
        }
        AutoScrollMinSize = new Size(0, y - AutoScrollPosition.Y - (int)(Spacing96 * s) + pad);
        base.OnLayout(e);
    }
}
