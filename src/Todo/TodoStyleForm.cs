namespace DeskFrames;

/// <summary>待辦清單樣式：主題、背景色、強調色、透明度、文字大小、行距、圓角。所有變更即時套用。</summary>
internal sealed class TodoStyleForm : ModernForm
{
    private static readonly string[] ThemeKeys = { "app", "dark", "light" };
    private static readonly string[] DensityKeys = { "compact", "normal", "loose" };
    private static readonly string[] CornerKeys = { "round", "small", "none" };

    private readonly TodoConfig _cfg;
    private readonly Action _changed;
    private readonly SegmentedRow _theme = new() { Text = "主題", Items = new[] { "跟隨介面", "深色", "淺色" } };
    private readonly ToggleRow _useBg = new() { Text = "自訂背景顏色" };
    private readonly ColorRow _bg = new() { Text = "背景顏色" };
    private readonly ToggleRow _useAccent = new() { Text = "自訂強調色", Description = "勾選圈、今天到期的時間等" };
    private readonly ColorRow _accent = new() { Text = "強調色" };
    private readonly SliderRow _opacity = new() { Text = "不透明度", Minimum = 40, Maximum = 100, Step = 5, Format = "{0:0}%" };
    private readonly SliderRow _font = new() { Text = "文字大小", Minimum = 9, Maximum = 14, Step = 0.5f, Format = "{0:0.#} pt" };
    private readonly SegmentedRow _density = new() { Text = "行距", Items = new[] { "緊湊", "標準", "寬鬆" } };
    private readonly SegmentedRow _corner = new() { Text = "圓角", Items = new[] { "大", "小", "無" } };
    private bool _loading;

    public TodoStyleForm(TodoConfig cfg, Action changed)
    {
        _cfg = cfg;
        _changed = changed;
        Text = "待辦清單樣式";
        ShowInTaskbar = false;
        TopMost = true;

        Body.Add(new SectionLabel { Text = "外觀" });
        Body.Add(_theme);
        Body.Add(_useBg);
        Body.Add(_bg);
        Body.Add(_useAccent);
        Body.Add(_accent);
        Body.Add(_opacity);
        Body.Add(new SectionLabel { Text = "版面" });
        Body.Add(_font);
        Body.Add(_density);
        Body.Add(_corner);
        var bar = Body.Add(new ButtonBar());
        var reset = new ModernButton { Text = "恢復預設", Glyph = Glyph.Undo, TabStop = false };
        reset.Click += (_, _) => ResetDefaults();
        bar.Controls.Add(reset);

        var done = new ModernButton { Text = "完成", Kind = ButtonKind.Primary, TabStop = false };
        done.Click += (_, _) => Close();
        Footer.Controls.Add(done);
        HasFooter = true;

        LoadValues();

        _theme.SelectedIndexChanged += (_, _) => Apply(() => _cfg.StyleTheme = ThemeKeys[_theme.SelectedIndex]);
        _useBg.CheckedChanged += (_, _) => Apply(() => _cfg.Background = _useBg.Checked ? ToHex(_bg.SelectedColor) : null);
        _bg.ColorChanged += (_, _) => Apply(() => { _cfg.Background = ToHex(_bg.SelectedColor); SetSilently(_useBg, true); });
        _useAccent.CheckedChanged += (_, _) => Apply(() => _cfg.Accent = _useAccent.Checked ? ToHex(_accent.SelectedColor) : null);
        _accent.ColorChanged += (_, _) => Apply(() => { _cfg.Accent = ToHex(_accent.SelectedColor); SetSilently(_useAccent, true); });
        _opacity.ValueChanged += (_, _) => Apply(() => _cfg.Opacity = (int)_opacity.Value);
        _font.ValueChanged += (_, _) => Apply(() => _cfg.FontSize = _font.Value);
        _density.SelectedIndexChanged += (_, _) => Apply(() => _cfg.Density = DensityKeys[_density.SelectedIndex]);
        _corner.SelectedIndexChanged += (_, _) => Apply(() => _cfg.Corner = CornerKeys[_corner.SelectedIndex]);

        FitHeightToContent(400);
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void LoadValues()
    {
        _loading = true;
        _theme.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeKeys, _cfg.StyleTheme));
        _useBg.Checked = _cfg.Background != null;
        _bg.SelectedColor = ColorRow.TryParseColor(_cfg.Background, out var bg) ? bg : TodoTheme.Bg;
        _useAccent.Checked = _cfg.Accent != null;
        _accent.SelectedColor = ColorRow.TryParseColor(_cfg.Accent, out var ac) ? ac : TodoTheme.Accent;
        _opacity.Value = Math.Clamp(_cfg.Opacity, 40, 100);
        _font.Value = _cfg.FontSize;
        _density.SelectedIndex = Math.Max(0, Array.IndexOf(DensityKeys, _cfg.Density));
        _corner.SelectedIndex = Math.Max(0, Array.IndexOf(CornerKeys, _cfg.Corner));
        _loading = false;
    }

    private void SetSilently(ToggleRow t, bool value)
    {
        bool old = _loading;
        _loading = true;
        t.Checked = value;
        _loading = old;
    }

    private void Apply(Action set)
    {
        if (_loading) return;
        set();
        _changed();
    }

    private void ResetDefaults()
    {
        _cfg.StyleTheme = "app";
        _cfg.Background = null;
        _cfg.Accent = null;
        _cfg.Opacity = 100;
        _cfg.FontSize = 10f;
        _cfg.Density = "normal";
        _cfg.Corner = "round";
        _changed();
        LoadValues();
    }
}
