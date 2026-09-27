namespace DeskFrames;

/// <summary>設定視窗：一般、編輯、預設框線樣式、快捷鍵說明。所有變更即時生效並自動儲存。</summary>
public sealed class SettingsForm : ModernForm
{
    private static readonly string[] ThemeKeys = { "system", "dark", "light" };
    private readonly AppController _c;
    private readonly ToggleRow _auto, _frames, _todo, _snap, _iconGrid;
    private readonly SegmentedRow _theme;
    private readonly SliderRow _grid;
    private readonly StyleEditor _styleEditor;
    private bool _loading;

    public SettingsForm(AppController c)
    {
        _c = c;
        var cfg = c.Config;
        Text = "DeskFrames 設定";
        Body.Pad96 = 22;

        Body.Add(new SectionLabel { Text = "一般" });
        _auto = Body.Add(new ToggleRow { Text = "開機時自動啟動", Description = "登入 Windows 後自動顯示框線與待辦清單" });
        _frames = Body.Add(new ToggleRow { Text = "顯示桌面框線" });
        _todo = Body.Add(new ToggleRow { Text = "顯示待辦清單" });
        var todoBar = Body.Add(new ButtonBar());
        var todoStyle = new ModernButton { Text = "待辦清單樣式…", Glyph = Glyph.Color, TabStop = false };
        todoStyle.Click += (_, _) => c.ShowTodoStyle();
        todoBar.Controls.Add(todoStyle);
        _theme = Body.Add(new SegmentedRow { Text = "介面主題", Items = new[] { "跟隨系統", "深色", "淺色" } });

        Body.Add(new SectionLabel { Text = "編輯" });
        _snap = Body.Add(new ToggleRow { Text = "自動吸附對齊", Description = "拖曳時對齊格線與其他框線（按住 Alt 可暫時停用）" });
        _iconGrid = Body.Add(new ToggleRow { Text = "對齊桌面圖示格線", Description = "框線邊緣落在圖示之間的縫隙，不會切過圖示或文字" });
        var alignBar = Body.Add(new ButtonBar());
        var alignNow = new ModernButton { Text = "立即對齊所有框線", Glyph = "\uE80A", TabStop = false };
        alignNow.Click += (_, _) => { c.EnterEdit(); c.AlignAllToIconGrid(); };
        alignBar.Controls.Add(alignNow);
        _grid = Body.Add(new SliderRow { Text = "格線間距（未對齊圖示時）", Minimum = 4, Maximum = 40, Step = 2, Format = "{0:0} px" });

        _styleEditor = new StyleEditor(Body, cfg.DefaultStyle, "新框線預設 · ");
        _styleEditor.Changed += () => c.Save();
        var factory = new ModernButton { Text = "恢復原廠樣式", Glyph = Glyph.Undo, TabStop = false };
        factory.Click += (_, _) => _styleEditor.ResetTo(new FrameStyle());
        var resetBar = Body.Add(new ButtonBar());
        resetBar.Controls.Add(factory);

        Body.Add(new SectionLabel { Text = "快捷鍵與操作" });
        Body.Add(new NoteLabel
        {
            Lines = 7,
            Text = $"{Hotkey.Display(cfg.HotkeyEdit)}　進入／離開編輯模式（亦可雙擊系統匣圖示）\n" +
                   $"{Hotkey.Display(cfg.HotkeyTodo)}　顯示／隱藏待辦清單（可在 config.json 修改）\n" +
                   "編輯時：拖曳框線移動；拖曳邊或角調整大小\n" +
                   "Shift + 拖曳邊線拉出凸角；Shift + 拖曳角落切出缺角\n" +
                   "雙擊標題改名；右鍵可套用 L 形、凸字形等形狀\n" +
                   "Del 刪除、Ctrl+Z 復原、方向鍵微調（Ctrl 為 1 像素）",
        });

        var folder = new ModernButton { Text = "開啟資料夾", Glyph = "", TabStop = false };
        folder.Click += (_, _) => c.OpenDataFolder();
        var edit = new ModernButton { Text = "編輯框線", Glyph = Glyph.Edit, TabStop = false };
        edit.Click += (_, _) => { c.EnterEdit(); };
        var done = new ModernButton { Text = "完成", Kind = ButtonKind.Primary, TabStop = false };
        done.Click += (_, _) => Close();
        Footer.Controls.AddRange(new Control[] { folder, edit, done });
        HasFooter = true;

        RefreshValues();

        _auto.CheckedChanged += (_, _) => { if (!_loading) c.SetAutoStart(_auto.Checked); };
        _frames.CheckedChanged += (_, _) => { if (!_loading) c.SetFramesVisible(_frames.Checked); };
        _todo.CheckedChanged += (_, _) => { if (_loading) return; if (_todo.Checked) c.ShowTodo(); else c.HideTodo(); };
        _theme.SelectedIndexChanged += (_, _) => { if (!_loading) c.SetTheme(ThemeKeys[_theme.SelectedIndex]); };
        _snap.CheckedChanged += (_, _) => { if (!_loading) { cfg.SnapEnabled = _snap.Checked; c.Save(); } };
        _iconGrid.CheckedChanged += (_, _) => { if (!_loading) { cfg.SnapToIconGrid = _iconGrid.Checked; c.Save(); } };
        _grid.ValueChanged += (_, _) => { if (!_loading) { cfg.GridSize = (int)_grid.Value; c.Save(); } };

        SetSize96(460, 720);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (Height > wa.Height * 0.9) Height = (int)(wa.Height * 0.9);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _styleEditor.Load(_c.Config.DefaultStyle); // 可能已由樣式視窗「設為預設」修改
    }

    public void RefreshValues()
    {
        if (IsDisposed) return;
        var cfg = _c.Config;
        _loading = true;
        _auto.Checked = cfg.AutoStart;
        _frames.Checked = cfg.FramesVisible;
        _todo.Checked = cfg.Todo.Visible;
        _theme.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeKeys, cfg.Theme));
        _snap.Checked = cfg.SnapEnabled;
        _iconGrid.Checked = cfg.SnapToIconGrid;
        _grid.Value = cfg.GridSize;
        _loading = false;
    }
}
