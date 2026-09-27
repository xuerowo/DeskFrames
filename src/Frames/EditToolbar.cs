namespace DeskFrames;

/// <summary>編輯模式的浮動工具列（置頂、可拖曳），同時負責接收編輯快捷鍵。</summary>
public sealed class EditToolbar : Form
{
    private readonly AppController _c;
    private readonly List<Control> _items = new();
    private readonly ModernButton _new, _add, _cut, _undo, _redo, _style, _rename, _dup, _del, _align, _desk, _settings, _done;
    private readonly ToolTip _tip = new() { InitialDelay = 350, ShowAlways = true };
    private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 4000 };
    private string? _flash;
    private bool _flashError;

    private const int RowH = 46, StatusH = 28, Pad = 8;

    public EditToolbar(AppController c)
    {
        _c = c;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ShowIcon = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Text = "DeskFrames 編輯";

        _new = Btn(Glyph.Edit, "繪製框線", "在桌面上拖曳繪製新框線", () => Toggle(DrawMode.New));
        _add = Btn(Glyph.Add, "加入區塊", "為選取的框線加上一塊範圍（可做出 L 形、凸字形）", () => Toggle(DrawMode.Add));
        _cut = Btn("", "挖除區塊", "從選取的框線挖掉一塊範圍", () => Toggle(DrawMode.Cut));
        Sep();
        _undo = Btn(Glyph.Undo, "", "復原 (Ctrl+Z)", _c.Undo);
        _redo = Btn(Glyph.Redo, "", "重做 (Ctrl+Y)", _c.Redo);
        Sep();
        _style = Btn(Glyph.Color, "樣式", "調整選取框線的顏色、線條與標題樣式", _c.ShowStyleForSelected);
        _rename = Btn(Glyph.Rename, "", "重新命名 (F2)", _c.RenameSelected);
        _dup = Btn(Glyph.Copy, "", "複製框線 (Ctrl+D)", _c.DuplicateSelected);
        _del = Btn(Glyph.Delete, "", "刪除框線 (Del)", _c.DeleteSelected);
        Sep();
        _align = Btn("\uE80A", "對齊格線", "把所有框線對齊到桌面圖示格線（可復原）", _c.AlignAllToIconGrid);
        Sep();
        _desk = Btn(Glyph.Desktop, "", "顯示桌面（最小化所有視窗）", () => { DesktopHost.ToggleDesktop(); BeginInvoke(RaiseTopmost); });
        _settings = Btn(Glyph.Settings, "", "設定", _c.ShowSettings);
        _done = Btn(Glyph.Check, "完成", "結束編輯 (Esc)", _c.ExitEdit);
        _done.Kind = ButtonKind.Primary;

        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flash = null; Invalidate(); };
        Theme.Changed += OnTheme;
        OnTheme();
        LayoutItems();
        PlaceInitial();
    }

    private ModernButton Btn(string glyph, string text, string tip, Action click)
    {
        var b = new ModernButton { Glyph = glyph, Text = text, Kind = ButtonKind.Ghost, TabStop = false, Height96 = 34 };
        b.Click += (_, _) => click();
        _tip.SetToolTip(b, tip);
        Controls.Add(b);
        _items.Add(b);
        return b;
    }

    private void Sep()
    {
        var s = new Panel { Width = 1, Tag = "sep" };
        Controls.Add(s);
        _items.Add(s);
    }

    private void Toggle(DrawMode m)
    {
        if (_c.CurrentDrawMode == m) _c.CancelDraw();
        else _c.StartDraw(m);
    }

    private void OnTheme()
    {
        if (IsDisposed) return;
        BackColor = Theme.Surface;
        foreach (var c in _items) if (c.Tag as string == "sep") c.BackColor = Theme.Border;
        if (IsHandleCreated) Theme.ApplyWindowChrome(Handle);
        Invalidate(true);
    }

    private float S(float v) => v * DeviceDpi / 96f;

    private void LayoutItems()
    {
        int x = (int)S(Pad), rowH = (int)S(RowH);
        foreach (var c in _items)
        {
            if (c is ModernButton b)
            {
                int w = (int)S(b.PreferredWidth96()), h = (int)S(b.Height96);
                b.SetBounds(x, (rowH - h) / 2 + (int)S(2), w, h);
                x += w + (int)S(2);
            }
            else
            {
                c.SetBounds(x + (int)S(5), (int)S(14), 1, rowH - (int)S(22));
                x += (int)S(11);
            }
        }
        Size = new Size(x + (int)S(Pad), rowH + (int)S(StatusH));
    }

    private void PlaceInitial()
    {
        var pos = _c.Config.ToolbarPos;
        if (pos is { Length: 2 } && Screen.AllScreens.Any(s => s.WorkingArea.Contains(pos[0] + 40, pos[1] + 20)))
        {
            Location = new Point(pos[0], pos[1]);
            return;
        }
        var wa = Screen.PrimaryScreen!.WorkingArea;
        float s = Native.ScaleAt(new Point(wa.X + wa.Width / 2, wa.Y + 10));
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (int)(16 * s));
    }

    /// <summary>出現時不搶走其他程式的鍵盤焦點；點選框線時才接手快捷鍵。</summary>
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(Handle);
    }

    private bool _closingByController;

    /// <summary>由控制器關閉（離開編輯模式）。</summary>
    public void CloseByController() { _closingByController = true; Close(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 等使用者關閉：改為正常離開編輯模式，避免框線卡在可編輯狀態
        if (!_closingByController && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            BeginInvoke(_c.ExitEdit);
        }
        base.OnFormClosing(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutItems();
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _c.Config.ToolbarPos = new[] { Left, Top };
        _c.Save();
    }

    protected override void Dispose(bool disposing)
    {
        Theme.Changed -= OnTheme;
        if (disposing) { _flashTimer.Dispose(); _tip.Dispose(); }
        base.Dispose(disposing);
    }

    // ================= 對外 =================

    public void UpdateState()
    {
        if (IsDisposed) return;
        bool sel = _c.Selected != null;
        var mode = _c.CurrentDrawMode;
        _new.Checked = mode == DrawMode.New;
        _add.Checked = mode == DrawMode.Add;
        _cut.Checked = mode == DrawMode.Cut;
        _add.Enabled = _cut.Enabled = _style.Enabled = _rename.Enabled = _dup.Enabled = _del.Enabled = sel;
        _undo.Enabled = _c.CanUndo;
        _redo.Enabled = _c.CanRedo;
        Invalidate();
    }

    /// <summary>在狀態列暫時顯示訊息。</summary>
    public void Flash(string text, bool error)
    {
        _flash = text; _flashError = error;
        _flashTimer.Stop(); _flashTimer.Start();
        Invalidate();
        if (error) System.Media.SystemSounds.Beep.Play();
    }

    public void RaiseTopmost()
    {
        if (IsDisposed || !IsHandleCreated) return;
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>取得鍵盤焦點，讓 Del、方向鍵、Ctrl+Z 等快捷鍵生效。</summary>
    public void TakeKeyboard()
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (Form.ActiveForm != this) { Native.SetForegroundWindow(Handle); Activate(); }
    }

    // ================= 繪製與互動 =================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Prepare(g);
        int rowH = (int)S(RowH);
        using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, S(12), rowH + S(1), ClientSize.Width - S(12), rowH + S(1));
        var text = _flash ?? _c.HintText();
        var color = _flash != null ? (_flashError ? Theme.Danger : Theme.Accent) : Theme.TextDim;
        TextRenderer.DrawText(g, text, Theme.Ui(8.75f), new Rectangle((int)S(14), rowH, ClientSize.Width - (int)S(28), (int)S(StatusH)),
            color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_NCCALCSIZE && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == Native.WM_NCHITTEST) { m.Result = new IntPtr(Native.HTCAPTION); return; }
        if (m.Msg == Native.WM_NCLBUTTONDBLCLK) return; // 不允許最大化
        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool fine = keyData.HasFlag(Keys.Control);
        switch (keyData & ~Keys.Control)
        {
            case Keys.Escape:
                if (_c.CurrentDrawMode != DrawMode.None) _c.CancelDraw();
                else _c.ExitEdit();
                return true;
            case Keys.Delete:
                _c.DeleteSelected(); return true;
            case Keys.F2:
                _c.RenameSelected(); return true;
            case Keys.Left: _c.Nudge(-_c.NudgeStep(true, fine), 0); return true;
            case Keys.Right: _c.Nudge(_c.NudgeStep(true, fine), 0); return true;
            case Keys.Up: _c.Nudge(0, -_c.NudgeStep(false, fine)); return true;
            case Keys.Down: _c.Nudge(0, _c.NudgeStep(false, fine)); return true;
        }
        switch (keyData)
        {
            case Keys.Control | Keys.Z: _c.Undo(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z: _c.Redo(); return true;
            case Keys.Control | Keys.D: _c.DuplicateSelected(); return true;
            case Keys.Control | Keys.N: _c.StartDraw(DrawMode.New); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
