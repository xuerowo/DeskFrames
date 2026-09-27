namespace DeskFrames;

/// <summary>框線樣式編輯控制項組（加入到 VStack 中）。修改即時寫回 FrameStyle 並觸發 Changed。</summary>
public sealed class StyleEditor
{
    private static readonly string[] LineKeys = { "solid", "dash", "dot" };
    private static readonly string[] TitleKeys = { "tab", "pill", "text" };
    private static readonly string[] AlignKeys = { "left", "center", "right" };

    private FrameStyle _style;
    private bool _loading;

    private readonly ColorRow _color = new() { Text = "框線顏色" };
    private readonly SliderRow _width = new() { Text = "框線粗細", Minimum = 1, Maximum = 8, Step = 0.5f, Format = "{0:0.#} px" };
    private readonly SliderRow _radius = new() { Text = "圓角", Minimum = 0, Maximum = 32, Step = 1, Format = "{0:0} px" };
    private readonly SegmentedRow _line = new() { Text = "線條樣式", Items = new[] { "實線", "虛線", "點線" } };
    private readonly SliderRow _lineOp = new() { Text = "框線不透明度", Minimum = 20, Maximum = 100, Step = 5, Format = "{0:0}%" };
    private readonly SliderRow _fill = new() { Text = "底色濃度", Minimum = 0, Maximum = 40, Step = 1, Format = "{0:0}%" };
    private readonly ToggleRow _glow = new() { Text = "柔光效果", Description = "框線外圍加上柔和光暈，任何桌布上都清楚可見" };
    private readonly SegmentedRow _title = new() { Text = "標題樣式", Items = new[] { "資料夾標籤", "膠囊", "純文字" } };
    private readonly SegmentedRow _align = new() { Text = "標題位置", Items = new[] { "靠左", "置中", "靠右" } };
    private readonly SliderRow _tsize = new() { Text = "標題字級", Minimum = 8, Maximum = 20, Step = 0.5f, Format = "{0:0.#} pt" };

    /// <summary>任一值即將改變（尚未寫入）。</summary>
    public event Action? Changing;
    /// <summary>任一值改變（拖曳中也會觸發，用於即時預覽）。</summary>
    public event Action? Changed;

    public StyleEditor(VStack stack, FrameStyle style, string? sectionPrefix = null)
    {
        _style = style;
        stack.Add(new SectionLabel { Text = (sectionPrefix ?? "") + "框線" });
        stack.Add(_color);
        stack.Add(_width);
        stack.Add(_radius);
        stack.Add(_line);
        stack.Add(_lineOp);
        stack.Add(_fill);
        stack.Add(_glow);
        stack.Add(new SectionLabel { Text = (sectionPrefix ?? "") + "標題" });
        stack.Add(_title);
        stack.Add(_align);
        stack.Add(_tsize);

        _color.ColorChanged += (_, _) => Apply(s => s.ColorValue = _color.SelectedColor);
        _width.ValueChanged += (_, _) => Apply(s => s.LineWidth = _width.Value);
        _radius.ValueChanged += (_, _) => Apply(s => s.CornerRadius = _radius.Value);
        _line.SelectedIndexChanged += (_, _) => Apply(s => s.LineStyle = LineKeys[_line.SelectedIndex]);
        _lineOp.ValueChanged += (_, _) => Apply(s => s.LineOpacity = (int)_lineOp.Value);
        _fill.ValueChanged += (_, _) => Apply(s => s.FillOpacity = (int)_fill.Value);
        _glow.CheckedChanged += (_, _) => Apply(s => s.Glow = _glow.Checked);
        _title.SelectedIndexChanged += (_, _) => Apply(s => s.TitleStyle = TitleKeys[_title.SelectedIndex]);
        _align.SelectedIndexChanged += (_, _) => Apply(s => s.TitleAlign = AlignKeys[_align.SelectedIndex]);
        _tsize.ValueChanged += (_, _) => Apply(s => s.TitleSize = _tsize.Value);
        Load(style);
    }

    public FrameStyle Style => _style;

    /// <summary>把目前樣式整組改成 src（視為一次修改：會觸發 Changing / Changed）。</summary>
    public void ResetTo(FrameStyle src)
    {
        Changing?.Invoke();
        _style.CopyFrom(src);
        Load(_style);
        Changed?.Invoke();
    }

    public void Load(FrameStyle style)
    {
        _style = style;
        _loading = true;
        _color.SelectedColor = style.ColorValue;
        _width.Value = style.LineWidth;
        _radius.Value = style.CornerRadius;
        _line.SelectedIndex = Math.Max(0, Array.IndexOf(LineKeys, style.LineStyle));
        _lineOp.Value = style.LineOpacity;
        _fill.Value = style.FillOpacity;
        _glow.Checked = style.Glow;
        _title.SelectedIndex = Math.Max(0, Array.IndexOf(TitleKeys, style.TitleStyle));
        _align.SelectedIndex = Math.Max(0, Array.IndexOf(AlignKeys, style.TitleAlign));
        _tsize.Value = style.TitleSize;
        _loading = false;
    }

    private void Apply(Action<FrameStyle> set)
    {
        if (_loading) return;
        Changing?.Invoke();
        set(_style);
        Changed?.Invoke();
    }
}

/// <summary>單一框線的樣式視窗：即時預覽於桌面上的框線。</summary>
public sealed class StyleForm : ModernForm
{
    private readonly AppController _c;
    private readonly StyleEditor _editor;
    private FrameModel? _frame;
    private string _snapshot = "";
    private bool _dirty;

    public StyleForm(AppController c)
    {
        _c = c;
        Text = "框線樣式";
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _editor = new StyleEditor(Body, new FrameStyle());
        // 第一次修改前記下快照；之後連續調整（例如拖曳滑桿）合併為一個復原步驟
        _editor.Changing += () => { if (!_dirty && _frame != null) { _snapshot = _c.SnapshotFrames(); _dirty = true; } };
        _editor.Changed += () => { if (_frame != null) _c.RefreshFrame(_frame); };

        var setDefault = new ModernButton { Text = "設為預設樣式", TabStop = false };
        setDefault.Click += (_, _) =>
        {
            if (_frame == null) return;
            _c.Config.DefaultStyle.CopyFrom(_frame.Style);
            _c.Save();
            setDefault.Text = "已設為預設 ✓";
        };
        var applyAll = new ModernButton { Text = "套用到全部框線", TabStop = false };
        applyAll.Click += (_, _) => { if (_frame != null) { CommitPending(); _c.ApplyStyleToAll(_frame.Style); } };
        var done = new ModernButton { Text = "完成", Kind = ButtonKind.Primary, TabStop = false };
        done.Click += (_, _) => Close();
        Footer.Controls.AddRange(new Control[] { setDefault, applyAll, done });

        // 恢復：我的預設樣式／原廠樣式（皆可 Ctrl+Z 復原）
        var reset = new ModernButton { Text = "恢復預設", Glyph = Glyph.Undo, TabStop = false };
        reset.Click += (_, _) => { if (_frame != null) _editor.ResetTo(_c.Config.DefaultStyle); };
        var factory = new ModernButton { Text = "恢復原廠樣式", Glyph = Glyph.Undo, TabStop = false };
        factory.Click += (_, _) => { if (_frame != null) _editor.ResetTo(new FrameStyle()); };
        var resetBar = Body.Add(new ButtonBar());
        resetBar.Controls.AddRange(new Control[] { reset, factory });
        HasFooter = true;
        FitHeightToContent(420);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Right - Width - (int)S(24), wa.Top + (int)S(96));
    }

    /// <summary>切換要編輯的框線（先提交上一個框線的變更）。</summary>
    public void Bind(FrameModel f)
    {
        if (_frame == f) return;
        CommitPending();
        Rebind(f);
    }

    /// <summary>不提交變更，直接改綁到（復原後重建的）框線物件。</summary>
    public void Rebind(FrameModel f)
    {
        _frame = f;
        _dirty = false;
        _editor.Load(f.Style);
        Text = $"框線樣式 — {(string.IsNullOrWhiteSpace(f.Title) ? "未命名" : f.Title)}";
        Invalidate();
    }

    /// <summary>把尚未記錄的樣式修改寫入復原堆疊。</summary>
    public void CommitPending()
    {
        if (!_dirty) return;
        _dirty = false;
        _c.CommitStyleChange(_snapshot);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CommitPending();
        base.OnFormClosed(e);
    }
}
