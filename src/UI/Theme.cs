using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace DeskFrames;

/// <summary>全域主題：深色/淺色色盤、字型、圖示字型與共用繪圖工具。</summary>
public static class Theme
{
    public static bool Dark { get; private set; } = true;

    public static Color Bg, Surface, SurfaceHover, SurfacePressed, Border, Text, TextDim, TextFaint,
        Accent, AccentHover, OnAccent, Danger, Field, Track;

    public const string UiFontName = "Microsoft JhengHei UI";
    public static readonly string IconFontName =
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "SegoeIcons.ttf"))
            ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static event Action? Changed;

    private static readonly Dictionary<(string, float, FontStyle), Font> _fonts = new();

    public static readonly Color[] Palette =
    {
        ColorTranslator.FromHtml("#5B8CFF"), ColorTranslator.FromHtml("#22C55E"), ColorTranslator.FromHtml("#F59E0B"),
        ColorTranslator.FromHtml("#EF4444"), ColorTranslator.FromHtml("#A855F7"), ColorTranslator.FromHtml("#EC4899"),
        ColorTranslator.FromHtml("#14B8A6"), ColorTranslator.FromHtml("#F8FAFC"), ColorTranslator.FromHtml("#64748B"),
    };

    static Theme() => Apply("system");

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    /// <summary>一組完整的介面色彩（全域主題與待辦清單自訂樣式共用）。</summary>
    public sealed record ColorSet(bool Dark, Color Bg, Color Surface, Color SurfaceHover, Color SurfacePressed, Color Border,
        Color Text, Color TextDim, Color TextFaint, Color Accent, Color AccentHover, Color OnAccent, Color Danger,
        Color Field, Color Track);

    public static readonly ColorSet DarkSet = new(true,
        Hex("#1B1C21"), Hex("#26272E"), Hex("#30323A"), Hex("#3A3C45"), Hex("#383A44"),
        Hex("#ECEDF1"), Hex("#A2A5AF"), Hex("#6E717C"), Hex("#5B8CFF"), Hex("#7AA2FF"), Color.White, Hex("#F2626E"),
        Hex("#15161A"), Hex("#3A3C46"));

    public static readonly ColorSet LightSet = new(false,
        Hex("#F6F7F9"), Hex("#FFFFFF"), Hex("#ECEEF2"), Hex("#E1E4EA"), Hex("#DCDFE5"),
        Hex("#1A1B1F"), Hex("#636772"), Hex("#9A9EA8"), Hex("#3D6DF2"), Hex("#2F5BD6"), Color.White, Hex("#E0404F"),
        Hex("#FFFFFF"), Hex("#D5D9E0"));

    public static void Apply(string mode)
    {
        bool dark = mode switch { "dark" => true, "light" => false, _ => SystemIsDark() };
        var p = dark ? DarkSet : LightSet;
        Dark = p.Dark;
        Bg = p.Bg; Surface = p.Surface; SurfaceHover = p.SurfaceHover; SurfacePressed = p.SurfacePressed;
        Border = p.Border; Text = p.Text; TextDim = p.TextDim; TextFaint = p.TextFaint;
        Accent = p.Accent; AccentHover = p.AccentHover; OnAccent = p.OnAccent; Danger = p.Danger;
        Field = p.Field; Track = p.Track;
        Changed?.Invoke();
    }

    public static Color Hex(string s) => ColorTranslator.FromHtml(s);

    public static Font Ui(float pt, FontStyle st = FontStyle.Regular) => GetFont(UiFontName, pt, st);
    public static Font Icon(float pt) => GetFont(IconFontName, pt, FontStyle.Regular);

    private static Font GetFont(string name, float pt, FontStyle st)
    {
        var key = (name, pt, st);
        if (!_fonts.TryGetValue(key, out var f))
        {
            try { f = new Font(name, pt, st, GraphicsUnit.Point); }
            catch { f = new Font(FontFamily.GenericSansSerif, pt, st, GraphicsUnit.Point); }
            _fonts[key] = f;
        }
        return f;
    }

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(Color c, int a) => Color.FromArgb(Math.Clamp(a, 0, 255), c.R, c.G, c.B);

    /// <summary>依背景亮度選擇黑或白文字。</summary>
    public static Color ContrastText(Color bg)
    {
        double l = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return l > 0.62 ? Color.FromArgb(24, 25, 30) : Color.White;
    }

    public static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f));
        if (rad < 0.5f) { p.AddRectangle(r); return p; }
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>上方圓角、下方直角（資料夾標籤）。</summary>
    public static GraphicsPath TabPath(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        rad = Math.Max(0, Math.Min(rad, Math.Min(r.Width / 2f, r.Height)));
        float d = rad * 2;
        if (rad < 0.5f) { p.AddRectangle(r); return p; }
        p.AddLine(r.X, r.Bottom, r.X, r.Y + rad);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
        p.CloseFigure();
        return p;
    }

    /// <summary>Win11 圓角、深色邊框等 DWM 外觀。</summary>
    public static void ApplyWindowChrome(IntPtr hwnd, bool round = true)
    {
        try
        {
            int dark = Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref dark, 4);
            int corner = round ? 2 /*ROUND*/ : 1 /*DONOTROUND*/;
            Native.DwmSetWindowAttribute(hwnd, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref corner, 4);
            int border = ColorTranslator.ToWin32(Border);
            Native.DwmSetWindowAttribute(hwnd, 34 /*DWMWA_BORDER_COLOR*/, ref border, 4);
        }
        catch { }
    }

    public static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}

/// <summary>Segoe Fluent Icons / MDL2 字符。</summary>
public static class Glyph
{
    public const string Add = "", Edit = "", Delete = "", Undo = "", Redo = "",
        Check = "", Close = "", More = "", Settings = "", Color = "",
        Desktop = "", Rename = "", Copy = "", Shape = "", Merge = "",
        Cut = "", List = "", Pin = "", Lock = "", Hide = "", View = "",
        Grip = "", Drag = "", ChevronDown = "", Accept = "", Paint = "",
        CheckBox = "", Frame = "";
}

/// <summary>現代化深/淺色右鍵選單外觀。</summary>
public sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernMenuRenderer() : base(new MenuColors()) { RoundedEdges = true; }

    public static ContextMenuStrip Create()
    {
        var m = new ContextMenuStrip
        {
            Renderer = new ModernMenuRenderer(),
            ShowImageMargin = true,
            Font = Theme.Ui(9.5f),
            Padding = new Padding(4),
        };
        m.Opening += (_, _) => { m.BackColor = Theme.Surface; m.ForeColor = Theme.Text; };
        m.HandleCreated += (_, _) => Theme.ApplyWindowChrome(m.Handle);
        return m;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        var r = e.AffectedBounds; r.Width -= 1; r.Height -= 1;
        e.Graphics.DrawRectangle(pen, r);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = Theme.RoundRect(r, 5);
        using var b = new SolidBrush(Theme.SurfaceHover);
        g.FillPath(b, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.TextFaint
            : e.Item.Tag as string == "danger" ? Theme.Danger : Theme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.TextDim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);
        var r = e.ImageRectangle;
        TextRenderer.DrawText(g, Glyph.Check, Theme.Icon(9f), r, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}

public static class MenuExt
{
    public static ToolStripMenuItem Item(this ToolStrip menu, string text, Action onClick, string? shortcut = null, bool danger = false)
    {
        var it = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut, Tag = danger ? "danger" : null };
        it.Click += (_, _) => onClick();
        menu.Items.Add(it);
        return it;
    }

    public static ToolStripMenuItem Item(this ToolStripMenuItem parent, string text, Action onClick)
    {
        var it = new ToolStripMenuItem(text);
        it.Click += (_, _) => onClick();
        parent.DropDownItems.Add(it);
        return it;
    }

    public static void Sep(this ToolStrip menu) => menu.Items.Add(new ToolStripSeparator());
}
