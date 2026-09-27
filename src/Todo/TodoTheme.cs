namespace DeskFrames;

/// <summary>
/// 待辦清單專用色彩：預設跟隨全域主題，可在「清單樣式」中改為深色/淺色、自訂背景色與強調色。
/// 待辦清單的程式碼一律讀取這裡的色彩（選單與對話框仍使用全域主題）。
/// </summary>
internal static class TodoTheme
{
    private static TodoConfig? _cfg;

    public static bool Dark { get; private set; }
    public static Color Bg, Surface, SurfaceHover, SurfacePressed, Border, Text, TextDim, TextFaint,
        Accent, AccentHover, OnAccent, Danger, Field;

    public static event Action? Changed;

    static TodoTheme()
    {
        Recompute();
        Theme.Changed += Recompute;
    }

    public static void Configure(TodoConfig cfg)
    {
        _cfg = cfg;
        Recompute();
    }

    /// <summary>依設定重新計算色彩並通知重繪。</summary>
    public static void Recompute()
    {
        var cfg = _cfg;
        bool dark = cfg?.StyleTheme switch { "dark" => true, "light" => false, _ => Theme.Dark };
        Color? bg = TryColor(cfg?.Background);
        if (bg is Color b) dark = Luma(b) < 0.5; // 自訂背景：文字深淺依背景亮度決定
        var p = dark ? Theme.DarkSet : Theme.LightSet;
        Dark = dark;
        Text = p.Text; TextDim = p.TextDim; TextFaint = p.TextFaint; Danger = p.Danger;

        if (bg is Color c)
        {
            var toward = dark ? Color.White : Color.Black;
            Bg = c;
            Surface = Theme.Blend(c, toward, dark ? 0.06f : 0.03f);
            SurfaceHover = Theme.Blend(c, toward, dark ? 0.10f : 0.06f);
            SurfacePressed = Theme.Blend(c, toward, dark ? 0.15f : 0.10f);
            Border = Theme.Blend(c, toward, dark ? 0.16f : 0.13f);
            Field = dark ? Theme.Blend(c, Color.Black, 0.25f) : Theme.Blend(c, Color.White, 0.7f);
        }
        else
        {
            Bg = p.Bg; Surface = p.Surface; SurfaceHover = p.SurfaceHover; SurfacePressed = p.SurfacePressed;
            Border = p.Border; Field = p.Field;
        }

        if (TryColor(cfg?.Accent) is Color a)
        {
            Accent = a;
            AccentHover = Theme.Blend(a, dark ? Color.White : Color.Black, 0.15f);
            OnAccent = Theme.ContrastText(a);
        }
        else
        {
            Accent = p.Accent; AccentHover = p.AccentHover; OnAccent = p.OnAccent;
        }
        Changed?.Invoke();
    }

    /// <summary>DWM 外觀：深色標題屬性、邊框色與圓角大小。</summary>
    public static void ApplyWindowChrome(IntPtr hwnd)
    {
        try
        {
            int dark = Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
            int corner = _cfg?.Corner switch { "none" => 1, "small" => 3, _ => 2 };
            Native.DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
            int border = ColorTranslator.ToWin32(Border);
            Native.DwmSetWindowAttribute(hwnd, 34, ref border, 4);
        }
        catch { }
    }

    private static Color? TryColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return ColorRow.TryParseColor(s, out var c) ? c : null;
    }

    private static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
