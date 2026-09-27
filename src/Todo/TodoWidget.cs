namespace DeskFrames;

/// <summary>
/// 桌面待辦清單小工具：無邊框卡片（保留 DWM 陰影與 Win11 圓角），釘在桌面層，
/// 不出現在工作列與 Alt+Tab，不會蓋住其他視窗。
/// </summary>
public sealed class TodoWidget : Form
{
    // ---- 版面常數（DIP） ----
    private const int HeaderH = 46, FieldH = 38, Margin12 = 12, ListInset = 6, Edge = 6, MinW = 240, MinH = 200,
        DefaultW = 300, DefaultH = 400, ScreenMargin = 24;

    private const int WM_NCRBUTTONUP = 0x00A5, WM_SYSCOMMAND = 0x0112,
        SC_CLOSE = 0xF060, SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030;

    private readonly TodoConfig _cfg;
    private readonly Action _save;
    private readonly TodoListView _list;
    private readonly TodoInputField _input = new();
    private readonly ModernButton _btnAdd = new() { Glyph = Glyph.Add, Kind = ButtonKind.Ghost, Radius = 6f };
    private readonly ModernButton _btnMenu = new() { Glyph = Glyph.More, Kind = ButtonKind.Ghost, Radius = 6f };

    private ContextMenuStrip? _menu;
    private ToolStripMenuItem? _miHideDone, _miClear, _miLock, _miShowInput;
    private TodoTextBox? _titleEditor;

    private Rectangle _initialBounds;
    private bool _firstPlacement, _shownOnce;

    /// <summary>使用者選擇「隱藏清單」。呼叫端負責設定 cfg.Visible=false 並隱藏視窗。</summary>
    public event Action? HideRequested;

    public TodoWidget(TodoConfig cfg, Action save)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        TodoTheme.Configure(_cfg);
        _cfg.Items ??= new();
        _save = save ?? (() => { });

        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = string.IsNullOrWhiteSpace(_cfg.Title) ? "待辦事項" : _cfg.Title;
        Font = Theme.Ui(9.5f);

        _list = new TodoListView(_cfg);
        _list.DataChanged += OnDataChanged;
        _list.BackgroundMenu += ShowMenu;
        _list.MouseDown += (_, _) => EndRename(true);

        _input.Box.Submit += AddFromInput;
        // Esc：有文字先清空；已是空的就收起輸入框
        _input.Box.Cancel += () =>
        {
            if (_input.Box.TextLength > 0) _input.Box.Clear();
            else BeginInvoke(() => CloseInput(force: true));
        };
        // 點到別處：輸入框是空的就自動收起（有打到一半的文字則保留）
        _input.Box.LostFocus += (_, _) => { if (IsHandleCreated && !Disposing && !IsDisposed) BeginInvoke(() => CloseInput(force: false)); };
        _input.AddClicked += AddFromInput;

        _btnAdd.Click += (_, _) => FocusInput();
        _btnMenu.Click += (_, _) => ShowMenu(_btnMenu.PointToScreen(new Point(_btnMenu.Width, _btnMenu.Height + (int)S(4))), true);

        Controls.Add(_list);
        Controls.Add(_input);
        Controls.Add(_btnAdd);
        Controls.Add(_btnMenu);

        UpdateInputVisibility();
        PlaceInitial();
        ApplyOpacity();
        ApplyTheme();
        TodoTheme.Changed += ApplyTheme;
    }

    // ================= 公開 API =================

    /// <summary>重新釘選到桌面（Explorer 重新啟動後由呼叫端使用）。</summary>
    public void RePin()
    {
        if (IsHandleCreated && !IsDisposed) DesktopHost.Pin(Handle);
    }

    /// <summary>套用「清單樣式」的變更（色彩、透明度、圓角、字級、行距）。</summary>
    public void ApplyStyle()
    {
        if (IsDisposed) return;
        TodoTheme.Recompute(); // 會觸發 ApplyTheme
        ApplyOpacity();
        _list.ApplyStyle();
        ApplyMetrics();
    }

    /// <summary>樣式選單被點選（由呼叫端開啟樣式視窗）。</summary>
    public event Action? StyleRequested;

    /// <summary>重新整理清單（例如到期狀態改變時）。</summary>
    public void RefreshItems()
    {
        if (!IsDisposed) _list.Reload();
    }

    /// <summary>讓「新增項目」輸入框取得焦點。</summary>
    public void FocusInput()
    {
        if (IsDisposed || !Visible) return;
        _list.CommitEdit();
        if (!_inputOpen) { _inputOpen = true; UpdateInputVisibility(); }
        Activate();
        if (IsHandleCreated) Native.SetForegroundWindow(Handle);
        _input.Box.Focus();
        _input.Box.SelectionStart = _input.Box.TextLength;
    }

    // ================= 輸入框顯示／隱藏 =================

    /// <summary>暫時打開的輸入框（按 ＋ 後）。常駐模式由 _cfg.ShowInput 控制。</summary>
    private bool _inputOpen;

    private bool InputShown => _cfg.ShowInput || _inputOpen;

    private void UpdateInputVisibility()
    {
        _input.Visible = InputShown;
        _list.InputVisible = InputShown;
        PerformLayout();
        _list.Invalidate();
    }

    /// <summary>收起暫時打開的輸入框。force=false 時，只有在輸入框為空且已失去焦點時才收起。</summary>
    private void CloseInput(bool force)
    {
        if (IsDisposed || !_inputOpen || _cfg.ShowInput) return;
        if (!force && (_input.Box.Focused || _input.Box.TextLength > 0)) return;
        _input.Box.Clear();
        _inputOpen = false;
        UpdateInputVisibility();
    }

    // ================= 視窗設定 =================

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private float S(float v) => v * DeviceDpi / 96f;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TodoTheme.ApplyWindowChrome(Handle);
        DesktopHost.Pin(Handle);
        ApplyMetrics();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        DesktopHost.Pin(Handle);
        if (!_shownOnce)
        {
            _shownOnce = true;
            // 建立視窗時框架可能調整過大小，還原為計算好的位置
            if (Bounds != _initialBounds) Bounds = _initialBounds;
            if (_firstPlacement) SavePlacement();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && IsHandleCreated) DesktopHost.SendToBottom(Handle);
        if (!Visible) { _list.CommitEdit(); EndRename(true); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TodoTheme.Changed -= ApplyTheme;
            _menu?.Dispose();
            _titleEditor?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        BackColor = TodoTheme.Bg;
        ForeColor = TodoTheme.Text;
        foreach (var b in new[] { _btnAdd, _btnMenu })
        {
            b.ForeOverride = TodoTheme.Text;
            b.HoverOverride = TodoTheme.SurfaceHover;
            b.PressedOverride = TodoTheme.SurfacePressed;
            b.Invalidate();
        }
        _input.ApplyTheme();
        if (_titleEditor != null) { _titleEditor.BackColor = TodoTheme.SurfaceHover; _titleEditor.ForeColor = TodoTheme.Text; }
        if (IsHandleCreated) TodoTheme.ApplyWindowChrome(Handle);
        Invalidate(true);
    }

    private void ApplyOpacity()
    {
        double o = Math.Clamp(_cfg.Opacity <= 0 ? 100 : _cfg.Opacity, 20, 100) / 100.0;
        // 100% 時不設定，避免無謂地變成分層視窗
        if (o < 1.0 || Opacity < 1.0) Opacity = o;
    }

    /// <summary>依目前 DPI 更新最小尺寸、字型與排版。</summary>
    private void ApplyMetrics()
    {
        MinimumSize = new Size((int)S(MinW), (int)S(MinH));
        float gp = TodoUi.PtFor(DeviceDpi, 10f);
        _btnAdd.GlyphSize = gp;
        _btnMenu.GlyphSize = gp;
        _input.ApplyMetrics(TodoUi.Font(DeviceDpi, Math.Clamp(_cfg.FontSize, 8f, 16f)));
        if (_titleEditor != null) _titleEditor.Font = TitleFont;
        PerformLayout();
        _list.Relayout();
        Invalidate(true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        MinimumSize = Size.Empty;
        base.OnDpiChanged(e);
        if (!e.Cancel && Bounds != e.SuggestedRectangle) Bounds = e.SuggestedRectangle;
        ApplyMetrics();
    }

    // ================= 位置與大小 =================

    private void PlaceInitial()
    {
        var prim = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea;
        Rectangle r;
        float s;
        if (_cfg.X == int.MinValue || _cfg.Y == int.MinValue)
        {
            s = Native.ScaleAt(new Point(prim.Right - 1, prim.Top + 1));
            int w = (int)(DefaultW * s), h = (int)(DefaultH * s), m = (int)(ScreenMargin * s);
            r = new Rectangle(prim.Right - w - m, prim.Top + m, w, h);
            _firstPlacement = true;
        }
        else
        {
            s = Native.ScaleAt(new Point(_cfg.X, _cfg.Y));
            int w = _cfg.W > 0 ? _cfg.W : (int)(DefaultW * s);
            int h = _cfg.H > 0 ? _cfg.H : (int)(DefaultH * s);
            r = ClampToScreens(new Rectangle(_cfg.X, _cfg.Y, Math.Max(w, (int)(MinW * s)), Math.Max(h, (int)(MinH * s))), s);
        }
        MinimumSize = new Size((int)(MinW * s), (int)(MinH * s));
        Bounds = r;
        _initialBounds = r;
    }

    /// <summary>確保標題列至少有一部分落在某個螢幕的工作區內（可以拖回來）。</summary>
    private static Rectangle ClampToScreens(Rectangle r, float s)
    {
        var header = new Rectangle(r.X, r.Y, r.Width, (int)(HeaderH * s));
        foreach (var scr in Screen.AllScreens)
        {
            var i = Rectangle.Intersect(header, scr.WorkingArea);
            if (i.Width >= Math.Min(r.Width, (int)(80 * s)) && i.Height >= Math.Min(header.Height, (int)(20 * s)))
                return r;
        }
        var wa = Screen.FromRectangle(r).WorkingArea;
        int w = Math.Min(r.Width, wa.Width), h = Math.Min(r.Height, wa.Height);
        return new Rectangle(Math.Clamp(r.X, wa.Left, wa.Right - w), Math.Clamp(r.Y, wa.Top, wa.Bottom - h), w, h);
    }

    // 拖曳標題列或邊緣結束後（WM_EXITSIZEMOVE）都會觸發
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        SavePlacement();
    }

    private void SavePlacement()
    {
        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        if (_cfg.X == b.X && _cfg.Y == b.Y && _cfg.W == b.Width && _cfg.H == b.Height) return;
        _cfg.X = b.X; _cfg.Y = b.Y; _cfg.W = b.Width; _cfg.H = b.Height;
        _save();
    }

    // ================= 版面 =================

    private Font TitleFont => TodoUi.Font(DeviceDpi, 10.5f, FontStyle.Bold);
    private Font CountFont => TodoUi.Font(DeviceDpi, 8.5f);

    private Rectangle TitleRect
    {
        get
        {
            int x = (int)S(16), right = _btnAdd.Left - (int)S(8);
            return new Rectangle(x, (int)S(7), Math.Max(10, right - x), (int)S(20));
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_list == null) return; // 基底建構期間
        int w = ClientSize.Width, h = ClientSize.Height, hh = (int)S(HeaderH);
        int b = (int)S(30), by = (hh - b) / 2 + (int)S(1);
        _btnMenu.SetBounds(w - (int)S(10) - b, by, b, b);
        _btnAdd.SetBounds(_btnMenu.Left - b - (int)S(2), by, b, b);

        int m = (int)S(Margin12), fh = (int)S(FieldH);
        _input.SetBounds(m, h - m - fh, Math.Max(10, w - m * 2), fh);

        int li = (int)S(ListInset), listTop = hh;
        int listBottom = InputShown ? _input.Top - (int)S(8) : h - m;
        _list.SetBounds(li, listTop, Math.Max(10, w - li * 2), Math.Max(0, listBottom - listTop));

        if (_titleEditor != null)
        {
            var tr = TitleRect;
            _titleEditor.SetBounds(tr.X, tr.Y + (tr.Height - _titleEditor.Height) / 2, tr.Width, _titleEditor.Height);
        }
    }

    // ================= 繪製 =================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);
        var tr = TitleRect;
        const TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                  TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;

        if (_titleEditor != null)
        {
            var er = RectangleF.Inflate(_titleEditor.Bounds, S(6), S(3));
            using var p = Theme.RoundRect(er, S(6));
            using var b = new SolidBrush(TodoTheme.SurfaceHover);
            using var pen = new Pen(TodoTheme.Accent, Math.Max(1f, S(1.2f)));
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        else
            TextRenderer.DrawText(g, _cfg.Title, TitleFont, tr, TodoTheme.Text, f);

        // 數量摘要 + 鎖定圖示
        var cr = new Rectangle(tr.X, (int)S(28), tr.Width, (int)S(14));
        string summary = Summary();
        TextRenderer.DrawText(g, summary, CountFont, cr, TodoTheme.TextDim, f);
        if (_cfg.Locked)
        {
            int sw = TextRenderer.MeasureText(g, summary, CountFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            var lr = new Rectangle(cr.X + sw + (int)S(6), cr.Y, (int)S(14), cr.Height);
            if (lr.Right <= cr.Right)
                TextRenderer.DrawText(g, Glyph.Lock, TodoUi.Icon(DeviceDpi, 7.5f), lr, TodoTheme.TextFaint,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }

    private string Summary()
    {
        int total = _cfg.Items.Count, done = _cfg.Items.Count(i => i.Done), open = total - done;
        if (total == 0) return "清單是空的";
        if (open == 0) return $"全部完成 · {done} 項";
        return done > 0 ? $"{open} 項未完成 · {done} 項已完成" : $"{open} 項未完成";
    }

    private void InvalidateHeader() => Invalidate(new Rectangle(0, 0, ClientSize.Width, (int)S(HeaderH)));

    // ================= 訊息處理 =================

    protected override void WndProc(ref Message m)
    {
        DesktopHost.GuardZOrder(ref m);
        switch (m.Msg)
        {
            case Native.WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                m.Result = IntPtr.Zero; // 客戶區 = 整個視窗（保留 DWM 陰影與圓角）
                return;
            case Native.WM_NCHITTEST:
                {
                    long lp = m.LParam.ToInt64();
                    var p = PointToClient(new Point(unchecked((short)(lp & 0xFFFF)), unchecked((short)((lp >> 16) & 0xFFFF))));
                    m.Result = (IntPtr)HitTest(p);
                    return;
                }
            case Native.WM_NCLBUTTONDBLCLK when (int)m.WParam == Native.HTCAPTION:
                BeginRename();
                return;
            case WM_NCRBUTTONUP when (int)m.WParam == Native.HTCAPTION:
                ShowMenu(Cursor.Position);
                return;
            case WM_SYSCOMMAND:
                {
                    int cmd = (int)(m.WParam.ToInt64() & 0xFFF0);
                    if (cmd == SC_CLOSE) { HideRequested?.Invoke(); return; } // Alt+F4 → 隱藏而非關閉
                    if (cmd is SC_MINIMIZE or SC_MAXIMIZE) return;             // 桌面小工具不縮放視窗狀態
                    break;
                }
        }
        base.WndProc(ref m);
    }

    /// <summary>邊緣縮放、標題列拖曳（鎖定時皆停用）。</summary>
    private int HitTest(Point p)
    {
        if (!_cfg.Locked)
        {
            int e = (int)S(Edge), c = (int)S(Edge * 2);
            int w = ClientSize.Width, h = ClientSize.Height;
            bool l = p.X < e, r = p.X >= w - e, t = p.Y < e, b = p.Y >= h - e;
            bool lc = p.X < c, rc = p.X >= w - c, tc = p.Y < c, bc = p.Y >= h - c;
            if ((t && lc) || (l && tc)) return Native.HTTOPLEFT;
            if ((t && rc) || (r && tc)) return Native.HTTOPRIGHT;
            if ((b && lc) || (l && bc)) return Native.HTBOTTOMLEFT;
            if ((b && rc) || (r && bc)) return Native.HTBOTTOMRIGHT;
            if (l) return Native.HTLEFT;
            if (r) return Native.HTRIGHT;
            if (t) return Native.HTTOP;
            if (b) return Native.HTBOTTOM;
            if (p.Y < S(HeaderH)) return Native.HTCAPTION;
        }
        return Native.HTCLIENT;
    }

    // 鎖定時標題列是一般客戶區：雙擊改名、右鍵開選單
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && e.Y < S(HeaderH)) BeginRename();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) ShowMenu(PointToScreen(e.Location));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _list.CommitEdit();
    }

    // 焦點在輸入框時滾輪訊息會冒泡到這裡：轉給清單
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _list.ScrollByWheel(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    // ================= 資料 =================

    private void OnDataChanged()
    {
        InvalidateHeader();
        _save();
    }

    /// <summary>新增項目：放在最後一個未完成項目之後（已完成項目之前）。</summary>
    private void AddFromInput()
    {
        string text = TodoUi.Clean(_input.Box.Text);
        if (text.Length == 0) { _input.Box.Focus(); return; }
        var item = new TodoItem { Text = text };
        int at = _cfg.Items.FindLastIndex(i => !i.Done) + 1;
        _cfg.Items.Insert(at, item);
        _input.Box.Clear();
        _list.Reload();
        _list.ScrollToItem(item);
        OnDataChanged();
    }

    // ================= 選單 =================

    private void ShowMenu(Point screen) => ShowMenu(screen, false);

    private void ShowMenu(Point screen, bool alignRight)
    {
        _list.CommitEdit();
        EndRename(true);
        _menu ??= BuildMenu();
        if (alignRight) _menu.Show(screen, ToolStripDropDownDirection.BelowLeft);
        else _menu.Show(screen);
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = ModernMenuRenderer.Create();
        _miHideDone = m.Item("隱藏已完成", () =>
        {
            _cfg.HideDone = !_cfg.HideDone;
            _list.Reload();
            OnDataChanged();
        });
        _miClear = m.Item("清除已完成項目", () =>
        {
            if (_cfg.Items.RemoveAll(i => i.Done) == 0) return;
            _list.Reload();
            OnDataChanged();
        });


        m.Item("清單樣式…", () => StyleRequested?.Invoke());

        _miLock = m.Item("鎖定位置與大小", () =>
        {
            _cfg.Locked = !_cfg.Locked;
            InvalidateHeader();
            _save();
        });
        _miShowInput = m.Item("常駐顯示輸入框", () =>
        {
            _cfg.ShowInput = !_cfg.ShowInput;
            _inputOpen = false;
            UpdateInputVisibility();
            _save();
        });
        m.Item("重新命名清單", BeginRename);
        m.Sep();
        m.Item("隱藏清單", () => HideRequested?.Invoke());

        m.Opening += (_, _) =>
        {
            int done = _cfg.Items.Count(i => i.Done);
            _miHideDone!.Checked = _cfg.HideDone;
            _miClear!.Text = done > 0 ? $"清除已完成項目（{done}）" : "清除已完成項目";
            _miClear.Enabled = done > 0;
            _miLock!.Checked = _cfg.Locked;
            _miShowInput!.Checked = _cfg.ShowInput;
        };
        return m;
    }

    // ================= 重新命名 =================

    private void BeginRename()
    {
        if (_titleEditor != null || IsDisposed) return;
        _list.CommitEdit();
        var ed = new TodoTextBox
        {
            Font = TitleFont,
            Text = _cfg.Title,
            MaxLength = 60,
            BackColor = TodoTheme.SurfaceHover,
            ForeColor = TodoTheme.Text,
        };
        ed.Submit += () => BeginInvoke(() => EndRename(true));
        ed.Cancel += () => BeginInvoke(() => EndRename(false));
        ed.LostFocus += OnTitleLostFocus;
        _titleEditor = ed;
        Controls.Add(ed);
        ed.BringToFront();
        PerformLayout();
        Activate();
        ed.Focus();
        ed.SelectAll();
        InvalidateHeader();
    }

    private void OnTitleLostFocus(object? sender, EventArgs e) => EndRename(true);

    private void EndRename(bool commit)
    {
        var ed = _titleEditor;
        if (ed == null) return;
        _titleEditor = null;
        string text = TodoUi.Clean(ed.Text);
        ed.LostFocus -= OnTitleLostFocus;
        Controls.Remove(ed);
        ed.Dispose();
        if (commit && text.Length > 0 && text != _cfg.Title)
        {
            _cfg.Title = text;
            Text = text;
            _save();
        }
        InvalidateHeader();
    }
}
