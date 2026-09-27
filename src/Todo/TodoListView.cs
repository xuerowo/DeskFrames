using System.Drawing.Drawing2D;

namespace DeskFrames;

/// <summary>
/// 自繪待辦清單：圓形核取、自動換行、停留高亮、刪除鈕、雙擊就地編輯、拖曳排序、平滑捲動與覆蓋式捲軸。
/// 動畫計時器只在捲動/拖曳時執行，閒置時完全停止。
/// </summary>
internal sealed class TodoListView : Control
{
    // ---- 版面常數（DIP） ----
    private const float PadTop = 4, PadBottom = 8, Gap = 2, RowInset = 4, CheckLeft = 12, CheckD = 18,
        TextLeft = 40, DelArea = 34, DelSize = 26, ThumbStrip = 10, WheelStep = 64;

    // 依「清單樣式」變化的版面值（DIP）
    private float FontPt => Math.Clamp(_cfg.FontSize, 8f, 16f);
    private float TextPadY => _cfg.Density switch { "compact" => 5, "loose" => 13, _ => 9 };
    private float MinRowH => _cfg.Density switch { "compact" => 30, "loose" => 46, _ => 38 };

    /// <summary>清單樣式（字級、行距）改變後重新排版。</summary>
    public void ApplyStyle()
    {
        if (_editor != null) _editor.Font = ItemFont(false);
        Relayout();
    }

    private enum Part { None, Row, Check, Delete, Thumb, Due }

    private sealed class Row
    {
        public required TodoItem Item;
        public int Y, H, TextH, DueH;
    }

    private readonly TodoConfig _cfg;
    private readonly List<Row> _rows = new();
    private int _contentH, _lineH, _textW, _dueH;

    // 捲動
    private float _scroll, _scrollTarget;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 15 };

    // 滑鼠狀態
    private int _hover = -1;
    private Part _hoverPart;
    private int _press = -1;
    private Part _pressPart;
    private Point _pressPt;

    // 拖曳排序
    private bool _dragging;
    private int _dragY, _dragGrab, _dropSlot = -1;

    // 捲軸拖曳
    private bool _thumbDrag;
    private int _thumbGrabY;
    private float _thumbGrabScroll;

    // 就地編輯
    private TodoTextBox? _editor;
    private TodoItem? _editItem;

    private ContextMenuStrip? _rowMenu;
    private ToolStripMenuItem? _miToggle, _miDue;
    private TodoItem? _menuItem;

    /// <summary>項目內容/順序/狀態被使用者修改。</summary>
    public event Action? DataChanged;

    /// <summary>底部輸入框是否顯示（影響空清單的提示文字）。</summary>
    public bool InputVisible { get; set; } = true;

    /// <summary>在空白處按右鍵（螢幕座標）。</summary>
    public event Action<Point>? BackgroundMenu;

    public TodoListView(TodoConfig cfg)
    {
        _cfg = cfg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false); // 點清單不搶走輸入框焦點
        _anim.Tick += (_, _) => AnimTick();
        TodoTheme.Changed += OnThemeChanged;
        Reload();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TodoTheme.Changed -= OnThemeChanged;
            _anim.Dispose();
            _rowMenu?.Dispose();
            _editor?.Dispose();
        }
        base.Dispose(disposing);
    }

    private float S(float v) => v * DeviceDpi / 96f;
    private Font ItemFont(bool done) => TodoUi.Font(DeviceDpi, FontPt, done ? FontStyle.Strikeout : FontStyle.Regular);
    private Font DueFont => TodoUi.Font(DeviceDpi, 8.5f);
    private float MaxScroll => Math.Max(0, _contentH - Height);
    private bool Overflow => _contentH > Height;

    private const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
                                              TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private void OnThemeChanged()
    {
        if (IsDisposed) return;
        if (_editor != null) { _editor.BackColor = TodoTheme.SurfaceHover; _editor.ForeColor = TodoTheme.Text; }
        Invalidate();
    }

    // ================= 資料與版面 =================

    /// <summary>依設定重建可見列（資料變更、切換隱藏已完成後呼叫）。</summary>
    public void Reload()
    {
        _rows.Clear();
        foreach (var it in _cfg.Items)
            if (!(_cfg.HideDone && it.Done)) _rows.Add(new Row { Item = it });
        _press = -1;
        if (_editItem != null && !_rows.Exists(r => r.Item == _editItem)) CancelEdit();
        Relayout();
        RefreshHover();
    }

    /// <summary>依目前游標位置重新判定停留列（資料變動或捲動後）。</summary>
    private void RefreshHover()
    {
        if (_dragging || _thumbDrag) return;
        var p = IsHandleCreated ? PointToClient(Cursor.Position) : new Point(-1, -1);
        (_hover, _hoverPart) = ClientRectangle.Contains(p) ? HitTest(p) : (-1, Part.None);
    }

    /// <summary>重新量測每列高度（寬度、DPI、字型改變時）。</summary>
    public void Relayout()
    {
        var font = ItemFont(false);
        _lineH = TextRenderer.MeasureText("國Ag", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
        _textW = Math.Max(20, (int)(Width - S(RowInset) * 2 - S(TextLeft) - S(DelArea)));
        _dueH = TextRenderer.MeasureText("今天 09:00", DueFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height + (int)S(4);
        int y = (int)S(PadTop);
        foreach (var r in _rows)
        {
            r.TextH = MeasureText(r.Item.Text, font);
            r.DueH = r.Item.Due != null ? _dueH : 0;
            r.H = Math.Max((int)S(MinRowH), r.TextH + r.DueH + (int)(S(TextPadY) * 2));
            r.Y = y;
            y += r.H + (int)S(Gap);
        }
        _contentH = _rows.Count == 0 ? 0 : y - (int)S(Gap) + (int)S(PadBottom);
        _scrollTarget = Math.Clamp(_scrollTarget, 0, MaxScroll);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        UpdateEditorBounds();
        Invalidate();
    }

    private int MeasureText(string text, Font font) =>
        Math.Max(_lineH, TextRenderer.MeasureText(text.Length == 0 ? " " : text, font, new Size(_textW, int.MaxValue), TextFlags).Height);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        if (_editor != null) _editor.Font = ItemFont(false);
        Relayout();
    }

    // ================= 幾何 =================

    private RectangleF RowRect(Row r, float top) => new(S(RowInset), top, Width - S(RowInset) * 2, r.H);

    /// <summary>第一行文字的垂直中心（核取圈、刪除鈕對齊用）。</summary>
    private float FirstLineCenter(Row r, float top) =>
        r.TextH + r.DueH + S(TextPadY) * 2 <= S(MinRowH) ? top + r.H / 2f : top + S(TextPadY) + _lineH / 2f;

    private RectangleF CheckRect(Row r, float top)
    {
        float d = S(CheckD), cy = FirstLineCenter(r, top);
        return new RectangleF(S(RowInset) + S(CheckLeft), cy - d / 2f, d, d);
    }

    private RectangleF DeleteRect(Row r, float top)
    {
        float d = S(DelSize), cy = FirstLineCenter(r, top);
        return new RectangleF(Width - S(RowInset) - S(4) - d, cy - d / 2f, d, d);
    }

    private Rectangle TextRect(Row r, float top)
    {
        int ty = r.TextH + r.DueH + (int)(S(TextPadY) * 2) <= (int)S(MinRowH)
            ? (int)(top + (r.H - r.TextH) / 2f)
            : (int)(top + S(TextPadY));
        return new Rectangle((int)(S(RowInset) + S(TextLeft)), ty, _textW, r.TextH);
    }

    /// <summary>到期時間那一行（文字下方）；未設定時間時為空。</summary>
    private RectangleF DueRect(Row r, float top)
    {
        if (r.DueH == 0 || r.Item.Due is not DateTime due) return RectangleF.Empty;
        var tr = TextRect(r, top);
        int w = TextRenderer.MeasureText(DueLabel(r.Item, due), DueFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        return new RectangleF(tr.X, tr.Bottom + S(3), S(16) + w + S(4), r.DueH - S(3));
    }

    private static string DueLabel(TodoItem item, DateTime due)
    {
        var now = DateTime.Now;
        string s = TodoDue.Format(due, now);
        return TodoDue.StateOf(item, now) == TodoDue.State.Overdue ? "已逾期 · " + s : s;
    }

    private (float y, float h) ThumbGeom()
    {
        float view = Height, h = Math.Max(S(28), view * view / Math.Max(1, _contentH));
        float y = MaxScroll <= 0 ? 0 : (view - h) * _scroll / MaxScroll;
        return (y, h);
    }

    private (int index, Part part) HitTest(Point p)
    {
        if (Overflow && p.X >= Width - S(ThumbStrip)) return (-1, Part.Thumb);
        float cy = p.Y + _scroll;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            if (cy < r.Y || cy >= r.Y + r.H) continue;
            float top = r.Y - _scroll;
            if (p.X < S(RowInset) + S(TextLeft) - S(2)) return (i, Part.Check);
            var del = DeleteRect(r, top);
            del.Inflate(S(3), S(3));
            if (del.Contains(p)) return (i, Part.Delete);
            var due = DueRect(r, top);
            if (!due.IsEmpty && RectangleF.Inflate(due, S(2), S(2)).Contains(p)) return (i, Part.Due);
            return (i, Part.Row);
        }
        return (-1, Part.None);
    }

    // ================= 捲動 =================

    /// <summary>滾輪捲動（亦供外層視窗轉送）。</summary>
    public void ScrollByWheel(int delta)
    {
        if (!Overflow) return;
        ScrollTo(_scrollTarget - delta / 120f * S(WheelStep), true);
    }

    private void ScrollTo(float target, bool animate)
    {
        _scrollTarget = Math.Clamp(target, 0, MaxScroll);
        if (animate) { if (!_anim.Enabled) _anim.Start(); return; }
        _scroll = _scrollTarget;
        RefreshHover();
        UpdateEditorBounds();
        Invalidate();
    }

    /// <summary>平滑捲動到讓指定項目完整可見。</summary>
    public void ScrollToItem(TodoItem item, bool animate = true)
    {
        var r = _rows.Find(x => x.Item == item);
        if (r == null) return;
        float t = _scrollTarget;
        if (r.Y - S(PadTop) < t) t = r.Y - S(PadTop);
        else if (r.Y + r.H + S(PadBottom) > t + Height) t = r.Y + r.H + S(PadBottom) - Height;
        if (Math.Abs(t - _scrollTarget) > 0.5f) ScrollTo(t, animate);
    }

    private void AnimTick()
    {
        bool busy = false;
        if (_dragging)
        {
            // 拖曳到上下邊緣時自動捲動
            var p = PointToClient(Cursor.Position);
            float edge = S(28), speed = 0;
            if (p.Y < edge) speed = -(edge - p.Y);
            else if (p.Y > Height - edge) speed = p.Y - (Height - edge);
            if (speed != 0 && Overflow)
            {
                float v = Math.Clamp(speed, -edge, edge) * 0.5f;
                float old = _scroll;
                _scroll = _scrollTarget = Math.Clamp(_scroll + v, 0, MaxScroll);
                if (_scroll != old) UpdateDropSlot();
                busy = true;
            }
        }
        float d = _scrollTarget - _scroll;
        if (Math.Abs(d) < 0.5f) _scroll = _scrollTarget;
        else { _scroll += d * 0.3f; busy = true; }
        RefreshHover();
        UpdateEditorBounds();
        Invalidate();
        if (!busy) _anim.Stop();
    }

    // ================= 滑鼠 =================

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollByWheel(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true; // 不再往父視窗傳遞
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        CommitEdit();
        var (idx, part) = HitTest(e.Location);
        if (e.Button != MouseButtons.Left) return;

        if (part == Part.Thumb)
        {
            var (ty, th) = ThumbGeom();
            if (e.Y < ty || e.Y > ty + th) // 點在軌道上：先跳到該處
                ScrollTo((e.Y - th / 2f) / Math.Max(1, Height - th) * MaxScroll, false);
            _thumbDrag = true;
            _thumbGrabY = e.Y;
            _thumbGrabScroll = _scroll;
            Invalidate();
            return;
        }
        if (e.Clicks >= 2 && part == Part.Row && idx >= 0)
        {
            BeginEdit(idx);
            return;
        }
        _press = idx;
        _pressPart = part;
        _pressPt = e.Location;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_thumbDrag)
        {
            var (_, th) = ThumbGeom();
            float range = Math.Max(1, Height - th);
            ScrollTo(_thumbGrabScroll + (e.Y - _thumbGrabY) * MaxScroll / range, false);
            return;
        }

        if (!_dragging && _press >= 0 && _pressPart is Part.Row or Part.Check && _rows.Count > 1 &&
            Math.Abs(e.Y - _pressPt.Y) + Math.Abs(e.X - _pressPt.X) > S(4))
        {
            _dragging = true;
            _dragGrab = (int)(_pressPt.Y - (_rows[_press].Y - _scroll));
            Cursor = Cursors.SizeAll;
        }

        if (_dragging)
        {
            _dragY = e.Y;
            UpdateDropSlot();
            if ((e.Y < S(28) || e.Y > Height - S(28)) && !_anim.Enabled) _anim.Start();
            Invalidate();
            return;
        }

        var (idx, part) = HitTest(e.Location);
        if (idx != _hover || part != _hoverPart)
        {
            _hover = idx;
            _hoverPart = part;
            Cursor = part is Part.Check or Part.Delete or Part.Due ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_thumbDrag) { _thumbDrag = false; Invalidate(); return; }

        if (_dragging)
        {
            FinishDrag(true);
            return;
        }

        var (idx, part) = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && _press >= 0 && idx == _press && part == _pressPart)
        {
            var item = _rows[idx].Item;
            if (part == Part.Check) Toggle(item);
            else if (part == Part.Delete) Delete(item);
            else if (part == Part.Due) PickDue(item);
        }
        else if (e.Button == MouseButtons.Right)
        {
            if (idx >= 0 && part != Part.Thumb) ShowRowMenu(_rows[idx].Item, PointToScreen(e.Location));
            else BackgroundMenu?.Invoke(PointToScreen(e.Location));
        }
        _press = -1;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragging || _thumbDrag) return;
        _hover = -1;
        _hoverPart = Part.None;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture || !IsHandleCreated) return;
        // 滑鼠擷取意外遺失（例如切換視窗）：延後檢查，正常放開時 OnMouseUp 會先處理完畢
        BeginInvoke(() =>
        {
            if (IsDisposed || Capture) return;
            if (_dragging) FinishDrag(false);
            _thumbDrag = false;
            _press = -1;
            Invalidate();
        });
    }

    // ================= 拖曳排序 =================

    private void UpdateDropSlot()
    {
        float cy = _dragY + _scroll;
        int slot = 0;
        for (int i = 0; i < _rows.Count; i++)
            if (cy > _rows[i].Y + _rows[i].H / 2f) slot = i + 1;
        _dropSlot = slot;
    }

    private bool DropIsNoop => _dropSlot < 0 || _dropSlot == _press || _dropSlot == _press + 1;

    private void FinishDrag(bool commit)
    {
        int from = _press, slot = _dropSlot;
        bool noop = DropIsNoop;
        _dragging = false;
        _dropSlot = -1;
        _press = -1;
        Cursor = Cursors.Default;
        if (commit && !noop && from >= 0 && from < _rows.Count)
        {
            var item = _rows[from].Item;
            var anchor = slot < _rows.Count ? _rows[slot].Item : null;
            var last = _rows[^1].Item;
            _cfg.Items.Remove(item);
            int at = anchor != null ? _cfg.Items.IndexOf(anchor) : _cfg.Items.IndexOf(last) + 1;
            _cfg.Items.Insert(Math.Clamp(at, 0, _cfg.Items.Count), item);
            Reload();
            DataChanged?.Invoke();
        }
        Invalidate();
    }

    // ================= 資料操作 =================

    private void Toggle(TodoItem item)
    {
        item.Done = !item.Done;
        item.DoneAt = item.Done ? DateTime.Now : null;
        Reload();
        DataChanged?.Invoke();
    }

    private void SetDue(TodoItem item, DateTime? due)
    {
        if (!_cfg.Items.Contains(item)) return;
        item.Due = due;
        item.Notified = false;
        Reload();
        DataChanged?.Invoke();
    }

    private void PickDue(TodoItem item)
    {
        CommitEdit();
        if (DuePickerForm.Pick(FindForm(), item.Due, out var value)) SetDue(item, value);
    }

    /// <summary>子選單沿用相同外觀。</summary>
    private static void StyleSubmenu(ToolStripMenuItem mi, ContextMenuStrip owner)
    {
        var dd = mi.DropDown;
        dd.Renderer = owner.Renderer;
        dd.Font = owner.Font;
        dd.Padding = owner.Padding;
        dd.Opening += (_, _) => { dd.BackColor = Theme.Surface; dd.ForeColor = Theme.Text; };
        dd.HandleCreated += (_, _) => Theme.ApplyWindowChrome(dd.Handle);
    }

    private void Delete(TodoItem item)
    {
        _cfg.Items.Remove(item);
        Reload();
        DataChanged?.Invoke();
    }

    private void ShowRowMenu(TodoItem item, Point screen)
    {
        if (_rowMenu == null)
        {
            _rowMenu = ModernMenuRenderer.Create();
            _rowMenu.Item("編輯", () => { int i = _rows.FindIndex(r => r.Item == _menuItem); if (i >= 0) BeginEdit(i); });
            _miToggle = _rowMenu.Item("標記為已完成", () => { if (_menuItem != null && _cfg.Items.Contains(_menuItem)) Toggle(_menuItem); });
            _miDue = new ToolStripMenuItem("設定時間");
            StyleSubmenu(_miDue, _rowMenu);
            _rowMenu.Items.Add(_miDue);
            _rowMenu.Sep();
            _rowMenu.Item("刪除", () => { if (_menuItem != null && _cfg.Items.Contains(_menuItem)) Delete(_menuItem); }, danger: true);
        }
        _menuItem = item;
        _miToggle!.Text = item.Done ? "標記為未完成" : "標記為已完成";
        // 設定時間子選單：依目前時間產生快捷選項
        var dd = _miDue!.DropDownItems;
        dd.Clear();
        foreach (var (label, value) in TodoDue.Presets(DateTime.Now))
        {
            var v = value;
            _miDue.Item(label, () => SetDue(item, v));
        }
        _miDue.Item("自訂…", () => PickDue(item));
        if (item.Due != null)
        {
            dd.Add(new ToolStripSeparator());
            _miDue.Item("清除時間", () => SetDue(item, null));
        }
        _miDue.Text = item.Due is DateTime d ? $"設定時間（{TodoDue.Format(d, DateTime.Now)}）" : "設定時間";
        _rowMenu.Show(screen);
    }

    // ================= 就地編輯 =================

    private void BeginEdit(int index)
    {
        CommitEdit();
        if (index < 0 || index >= _rows.Count) return;
        var item = _rows[index].Item;
        ScrollToItem(item, false);
        _editItem = item;
        var ed = new TodoTextBox
        {
            Multiline = true,
            WordWrap = true,
            AcceptsReturn = false,
            ScrollBars = ScrollBars.None,
            MaxLength = 500,
            Font = ItemFont(false),
            BackColor = TodoTheme.SurfaceHover,
            ForeColor = TodoTheme.Text,
            Text = item.Text,
        };
        // 延後處理：避免在 TextBox 自身的按鍵處理中將其釋放
        ed.Submit += () => BeginInvoke(CommitEdit);
        ed.Cancel += () => BeginInvoke(CancelEdit);
        ed.LostFocus += OnEditorLostFocus;
        ed.TextChanged += (_, _) => UpdateEditorBounds();
        _editor = ed;
        Controls.Add(ed);
        UpdateEditorBounds();
        FindForm()?.Activate();
        ed.Focus();
        ed.SelectionStart = ed.TextLength;
        Invalidate();
    }

    private void OnEditorLostFocus(object? sender, EventArgs e) => CommitEdit();

    public void CommitEdit() => EndEdit(true);
    public void CancelEdit() => EndEdit(false);

    private void EndEdit(bool commit)
    {
        var ed = _editor;
        var item = _editItem;
        if (ed == null) return;
        _editor = null;
        _editItem = null;
        string text = TodoUi.Clean(ed.Text);
        ed.LostFocus -= OnEditorLostFocus;
        Controls.Remove(ed);
        ed.Dispose();

        bool changed = false;
        if (commit && item != null && _cfg.Items.Contains(item))
        {
            if (text.Length == 0) { _cfg.Items.Remove(item); changed = true; }
            else if (text != item.Text) { item.Text = text; changed = true; }
        }
        Reload();
        if (changed) DataChanged?.Invoke();
    }

    private void UpdateEditorBounds()
    {
        if (_editor == null || _editItem == null) return;
        var r = _rows.Find(x => x.Item == _editItem);
        if (r == null) return;
        var tr = TextRect(r, r.Y - _scroll);
        int h = Math.Max(r.TextH, MeasureText(_editor.Text, _editor.Font));
        _editor.SetBounds(tr.X, tr.Y, tr.Width, h);
    }

    // ================= 繪製 =================

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(TodoTheme.Bg);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Prepare(g);

        if (_rows.Count == 0)
        {
            PaintEmpty(g);
            return;
        }

        var clip = e.ClipRectangle;
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            float top = r.Y - _scroll;
            if (top > clip.Bottom || top + r.H < clip.Top) continue;
            if (_dragging && i == _press) { PaintDragSlot(g, r, top); continue; }
            PaintRow(g, i, top, false);
        }

        if (_dragging) PaintDragOverlay(g);

        // 上/下方仍有內容時顯示細分隔線
        if (_scroll > 0.5f || _scroll < MaxScroll - 0.5f)
        {
            using var pen = new Pen(TodoTheme.Border);
            if (_scroll > 0.5f) g.DrawLine(pen, S(RowInset), 0, Width - S(RowInset), 0);
            if (_scroll < MaxScroll - 0.5f) g.DrawLine(pen, S(RowInset), Height - 1, Width - S(RowInset), Height - 1);
        }

        if (Overflow) PaintScrollbar(g);
    }

    private void PaintRow(Graphics g, int index, float top, bool ghost)
    {
        var r = _rows[index];
        var item = r.Item;
        bool editing = item == _editItem;
        bool hot = ghost || editing || (!_dragging && index == _hover && _hoverPart != Part.Thumb);
        var rr = RowRect(r, top);
        float rad = S(8);

        if (ghost)
        {
            // 浮起的拖曳卡片：柔和陰影 + 表面色
            for (int k = 3; k >= 1; k--)
            {
                var sr = rr; sr.Offset(0, S(k)); sr.Inflate(S(k) * 0.5f, S(k) * 0.5f);
                using var sp = Theme.RoundRect(sr, rad + S(k));
                using var sb = new SolidBrush(Color.FromArgb(TodoTheme.Dark ? 28 : 14, 0, 0, 0));
                g.FillPath(sb, sp);
            }
            using var p = Theme.RoundRect(rr, rad);
            using var b = new SolidBrush(TodoTheme.Surface);
            using var pen = new Pen(TodoTheme.Border);
            g.FillPath(b, p);
            g.DrawPath(pen, p);
        }
        else if (hot)
        {
            bool pressed = index == _press && _pressPart == Part.Row;
            using var p = Theme.RoundRect(rr, rad);
            using var b = new SolidBrush(pressed ? TodoTheme.SurfacePressed : TodoTheme.SurfaceHover);
            g.FillPath(b, p);
        }

        // 圓形核取
        var cr = CheckRect(r, top);
        bool checkHot = !ghost && !_dragging && index == _hover && _hoverPart == Part.Check;
        if (item.Done)
        {
            using var b = new SolidBrush(checkHot ? TodoTheme.AccentHover : TodoTheme.Accent);
            g.FillEllipse(b, cr);
            TextRenderer.DrawText(g, Glyph.Check, TodoUi.Icon(DeviceDpi, 7.5f), Rectangle.Round(cr), TodoTheme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            var ring = RectangleF.Inflate(cr, -S(0.75f), -S(0.75f));
            using var pen = new Pen(checkHot ? TodoTheme.Accent : TodoTheme.TextFaint, Math.Max(1f, S(1.5f)));
            g.DrawEllipse(pen, ring);
            if (checkHot)
            {
                var bg = hot ? TodoTheme.SurfaceHover : TodoTheme.Bg;
                TextRenderer.DrawText(g, Glyph.Check, TodoUi.Icon(DeviceDpi, 7.5f), Rectangle.Round(cr), Theme.Blend(TodoTheme.Accent, bg, .35f),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        // 文字（編輯中由 TextBox 覆蓋）
        if (!editing)
        {
            var color = item.Done ? TodoTheme.TextFaint : TodoTheme.Text;
            TextRenderer.DrawText(g, item.Text, ItemFont(item.Done), TextRect(r, top), color, TextFlags);
        }

        // 到期時間：逾期紅色、今天到期用強調色、已完成淡化
        if (item.Due is DateTime due)
        {
            var dr = DueRect(r, top);
            var state = TodoDue.StateOf(item, DateTime.Now);
            var dc = item.Done ? TodoTheme.TextFaint
                : state == TodoDue.State.Overdue ? TodoTheme.Danger
                : state == TodoDue.State.Today ? (TodoTheme.Dark ? TodoTheme.AccentHover : TodoTheme.Accent)
                : TodoTheme.TextDim;
            bool dueHot = !ghost && !_dragging && index == _hover && _hoverPart == Part.Due;
            if (dueHot)
            {
                using var p = Theme.RoundRect(RectangleF.Inflate(dr, S(3), S(1)), S(5));
                using var b = new SolidBrush(Theme.WithAlpha(dc, TodoTheme.Dark ? 40 : 28));
                g.FillPath(b, p);
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, "\uE121", TodoUi.Icon(DeviceDpi, 8f), new Rectangle((int)dr.X, (int)dr.Y, (int)S(14), (int)dr.Height), dc, flags);
            TextRenderer.DrawText(g, DueLabel(item, due), DueFont, new Rectangle((int)(dr.X + S(16)), (int)dr.Y, (int)(dr.Width - S(16)), (int)dr.Height), dc, flags);
        }

        // 刪除鈕（停留時顯示）
        if (hot && !ghost && !editing)
        {
            var dr = DeleteRect(r, top);
            bool delHot = index == _hover && _hoverPart == Part.Delete;
            if (delHot)
            {
                using var p = Theme.RoundRect(dr, S(6));
                using var b = new SolidBrush(Theme.WithAlpha(TodoTheme.Danger, TodoTheme.Dark ? 48 : 32));
                g.FillPath(b, p);
            }
            TextRenderer.DrawText(g, Glyph.Close, TodoUi.Icon(DeviceDpi, 7.5f), Rectangle.Round(dr),
                delHot ? TodoTheme.Danger : TodoTheme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>被拖走的列原位置：虛線外框。</summary>
    private void PaintDragSlot(Graphics g, Row r, float top)
    {
        var rr = RowRect(r, top);
        rr.Inflate(-S(1), -S(1));
        using var p = Theme.RoundRect(rr, S(8));
        using var pen = new Pen(TodoTheme.Border, Math.Max(1f, S(1.2f))) { DashStyle = DashStyle.Dash };
        g.DrawPath(pen, p);
    }

    private void PaintDragOverlay(Graphics g)
    {
        // 跟隨滑鼠的浮起卡片
        if (_press >= 0 && _press < _rows.Count)
        {
            var r = _rows[_press];
            float top = Math.Clamp(_dragY - _dragGrab, -r.H / 2f, Height - r.H / 2f);
            PaintRow(g, _press, top, true);
        }

        // 插入位置指示線（畫在卡片之上，永遠可見）
        if (!DropIsNoop)
        {
            float y = _dropSlot < _rows.Count
                ? _rows[_dropSlot].Y - S(Gap) / 2f
                : _rows[^1].Y + _rows[^1].H + S(Gap) / 2f;
            y -= _scroll;
            float x0 = S(RowInset) + S(6), x1 = Width - S(RowInset) - S(6), rr = S(3.5f);
            using var pen = new Pen(TodoTheme.Accent, Math.Max(1.5f, S(2)));
            g.DrawLine(pen, x0 + rr * 2, y, x1, y);
            using var fill = new SolidBrush(TodoTheme.Bg);
            g.FillEllipse(fill, x0, y - rr, rr * 2, rr * 2);
            g.DrawEllipse(pen, x0, y - rr, rr * 2, rr * 2);
        }
    }

    private void PaintScrollbar(Graphics g)
    {
        var (ty, th) = ThumbGeom();
        bool hot = _thumbDrag || _hoverPart == Part.Thumb;
        float w = hot ? S(6) : S(4);
        var r = new RectangleF(Width - w - S(2), ty + S(2), w, th - S(4));
        using var p = Theme.RoundRect(r, w / 2f);
        using var b = new SolidBrush(Theme.WithAlpha(TodoTheme.Text, hot ? 110 : 55));
        g.FillPath(b, p);
    }

    private void PaintEmpty(Graphics g)
    {
        int hidden = _cfg.HideDone ? _cfg.Items.Count(i => i.Done) : 0;
        string line1 = hidden > 0 ? "全部完成了！" : "沒有待辦事項";
        string line2 = hidden > 0 ? $"已隱藏 {hidden} 個已完成項目" : (InputVisible ? "在下方輸入框新增第一個項目" : "按右上角 ＋ 新增第一個項目");

        var f1 = TodoUi.Font(DeviceDpi, 10.5f, FontStyle.Bold);
        var f2 = TodoUi.Font(DeviceDpi, 9f);
        var icon = TodoUi.Icon(DeviceDpi, 20f);
        int iconH = (int)S(40), h1 = TextRenderer.MeasureText(line1, f1).Height, h2 = TextRenderer.MeasureText(line2, f2).Height;
        int total = iconH + (int)S(8) + h1 + (int)S(2) + h2;
        int y = Math.Max(0, (Height - total) / 2 - (int)S(6));
        const TextFormatFlags c = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        // 圓形底 + 勾勾圖示
        var circle = new RectangleF((Width - iconH) / 2f, y, iconH, iconH);
        using (var b = new SolidBrush(Theme.WithAlpha(TodoTheme.Accent, TodoTheme.Dark ? 36 : 26))) g.FillEllipse(b, circle);
        TextRenderer.DrawText(g, Glyph.Accept, icon, Rectangle.Round(circle), TodoTheme.Accent, c | TextFormatFlags.NoPadding);
        y += iconH + (int)S(8);
        TextRenderer.DrawText(g, line1, f1, new Rectangle(0, y, Width, h1), TodoTheme.TextDim, c);
        y += h1 + (int)S(2);
        TextRenderer.DrawText(g, line2, f2, new Rectangle(0, y, Width, h2), TodoTheme.TextFaint, c);
    }
}
