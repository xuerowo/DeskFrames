namespace DeskFrames;

public enum DrawMode { None, New, Add, Cut }

/// <summary>編輯模式：選取、拖曳、吸附、繪製、復原/重做、右鍵選單。</summary>
public sealed partial class AppController : IFrameHost
{
    private bool _edit;
    private EditToolbar? _toolbar;
    private string? _selId;
    private DragOp? _drag;
    private DrawSession? _draw;
    private TitleEditor? _rename;
    private StyleForm? _styleForm;
    private readonly List<string> _undo = new(), _redo = new();

    public bool EditMode => _edit;
    public DrawMode CurrentDrawMode => _draw?.Mode ?? DrawMode.None;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public FrameModel? Selected => _cfg.Frames.FirstOrDefault(f => f.Id == _selId);
    private FrameWindow? SelWin => _windows.FirstOrDefault(w => w.Model.Id == _selId);

    private sealed class DragOp
    {
        public required FrameWindow Win;
        public PartKind Kind;
        public bool Notch;
        public int Index;
        public Point Start;
        public required Point[] Work0;
        public required string Snapshot;
        public bool Changed;
        public bool Started;
    }

    private sealed class DrawSession
    {
        public DrawMode Mode;
        public readonly List<CaptureForm> Forms = new();
        public FrameWindow? Preview;
        public FrameModel? PreviewModel;
        public Point Start;
        public bool Dragging;
    }

    // ================= 進入／離開 =================

    public void ToggleEdit() { if (_edit) ExitEdit(); else EnterEdit(); }

    public void EnterEdit()
    {
        if (_edit) return;
        if (!_initialized) return;
        _edit = true;
        if (!_cfg.FramesVisible) SetFramesVisible(true);
        _undo.Clear(); _redo.Clear();
        DesktopGrid.Refresh();
        foreach (var w in _windows) w.EditMode = true;
        _toolbar = new EditToolbar(this);
        _toolbar.Show();
        UpdateToolbar();
    }

    public void ExitEdit()
    {
        if (!_edit) return;
        CancelDraw();
        EndRename(true);
        CancelDrag();
        _styleForm?.Close();
        _edit = false;
        _selId = null;
        foreach (var w in _windows) w.EditMode = false;
        _toolbar?.CloseByController();
        _toolbar = null;
        Save();
        ScheduleTrim();
    }

    public void UpdateToolbar() => _toolbar?.UpdateState();

    public string HintText()
    {
        var sel = Selected;
        return CurrentDrawMode switch
        {
            DrawMode.New => "在桌面上按住滑鼠拖曳，繪製新的框線　·　右鍵或 Esc 取消",
            DrawMode.Add => $"拖曳出要加入「{Name(sel)}」的區塊（需與框線相接）　·　Esc 取消",
            DrawMode.Cut => $"拖曳出要從「{Name(sel)}」挖除的範圍　·　Esc 取消",
            _ when sel != null => $"已選取「{Name(sel)}」　·　Del 刪除　·　方向鍵微調　·　F2 改名　·　Ctrl+D 複製　·　右鍵更多",
            _ => "拖曳框線可移動　·　拖曳邊或角調整　·　Shift+拖曳邊線拉出凸角　·　Shift+拖曳角落切出缺角　·　按住 Alt 暫停吸附",
        };
    }

    private static string Name(FrameModel? f) => f == null ? "" : string.IsNullOrWhiteSpace(f.Title) ? "未命名" : f.Title.Trim();

    // ================= 選取 =================

    private void Select(string? id)
    {
        if (_selId == id) return;
        _selId = id;
        foreach (var w in _windows)
        {
            bool s = w.Model.Id == id;
            if (w.Selected != s) { w.Selected = s; w.Render(); }
        }
        UpdateToolbar();
        if (_styleForm != null && !_styleForm.IsDisposed)
        {
            var f = Selected;
            if (f != null) _styleForm.Bind(f); else _styleForm.Close();
        }
    }

    // ================= IFrameHost：滑鼠事件 =================

    public void FrameMouseMove(FrameWindow w, Point pt)
    {
        if (_drag != null) { UpdateDrag(pt); return; }
        var part = w.Layout.HitTest(pt);
        if (!w.Hovered || part != w.Hover)
        {
            foreach (var o in _windows)
                if (o != w && o.Hovered) { o.Hovered = false; o.Hover = HitPart.None; o.Render(); }
            w.Hovered = true;
            w.Hover = part;
            w.Render();
        }
    }

    public void FrameMouseLeave(FrameWindow w)
    {
        if (_drag != null) return;
        if (w.Hovered) { w.Hovered = false; w.Hover = HitPart.None; w.Render(); }
    }

    public void FrameMouseDown(FrameWindow w, Point pt, bool dbl, bool shift)
    {
        if (_draw != null) return;
        EndRename(true);
        var part = w.Layout.HitTest(pt);
        Select(w.Model.Id);
        _toolbar?.TakeKeyboard();
        if (dbl)
        {
            if (part.Kind is PartKind.Title or PartKind.Body) BeginRename(w);
            return;
        }
        if (part.Kind == PartKind.None) return;

        var op = new DragOp
        {
            Win = w, Kind = part.Kind, Index = part.Index, Start = pt,
            Work0 = w.Model.Points, Snapshot = SnapshotFrames(),
        };
        if (shift)
        {
            if (part.Kind == PartKind.Edge) PrepareBump(op, pt, w.Layout.Scale);
            else if (part.Kind == PartKind.Vertex) PrepareNotch(op);
        }
        _drag = op;
        Native.SetCapture(w.Handle);
    }

    public void FrameMouseUp(FrameWindow w, Point pt)
    {
        if (_drag == null) return;
        var op = _drag;
        _drag = null;
        if (Native.GetCapture() == w.Handle) Native.ReleaseCapture();
        if (op.Changed)
        {
            PushUndo(op.Snapshot);
            Save();
            ArrangeZOrder();
        }
        op.Win.Hover = op.Win.Layout.HitTest(pt);
        op.Win.Render();
        UpdateToolbar();
    }

    public void FrameRightClick(FrameWindow w, Point pt)
    {
        if (_draw != null) { CancelDraw(); return; }
        EndRename(true);
        Select(w.Model.Id);
        ShowFrameMenu(w, pt);
    }

    public int CursorFor(FrameWindow w, Point pt)
    {
        HitPart part = _drag != null ? new HitPart(_drag.Notch ? PartKind.Vertex : _drag.Kind, _drag.Index) : w.Layout.HitTest(pt);
        bool shift = Native.KeyDown(Native.VK_SHIFT) || _drag?.Notch == true;
        var pts = w.Layout.Poly;
        switch (part.Kind)
        {
            case PartKind.Title:
            case PartKind.Body:
                return Native.IDC_SIZEALL;
            case PartKind.Edge:
                if (shift && _drag == null) return Native.IDC_CROSS;
                return part.Index % 2 == 0 ? Native.IDC_SIZENS : Native.IDC_SIZEWE;
            case PartKind.Vertex:
                if (shift) return Native.IDC_CROSS;
                if (part.Index < 0 || part.Index >= pts.Length) return Native.IDC_SIZEALL;
                {
                    int n = pts.Length;
                    var v = pts[part.Index]; var prev = pts[(part.Index - 1 + n) % n]; var next = pts[(part.Index + 1) % n];
                    int hx = Math.Sign((prev.Y == v.Y ? prev.X : next.X) - v.X);
                    int vy = Math.Sign((prev.X == v.X ? prev.Y : next.Y) - v.Y);
                    return hx * vy > 0 ? Native.IDC_SIZENWSE : Native.IDC_SIZENESW;
                }
            default:
                return Native.IDC_ARROW;
        }
    }

    // ================= 拖曳 =================

    /// <summary>Shift+拖曳邊線：在按下處插入一段可拉出的凸角。</summary>
    private void PrepareBump(DragOp op, Point pt, float scale)
    {
        var pts = op.Work0;
        int n = pts.Length, k = op.Index;
        var a = pts[k]; var b = pts[(k + 1) % n];
        bool horiz = a.Y == b.Y;
        int lo = horiz ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
        int hi = horiz ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
        int len = hi - lo, grid = Grid;
        int c = horiz ? pt.X : pt.Y;
        int w, s0;
        if (IconGrid)
        {
            // 以滑鼠所在的那一格作為凸角寬度
            w = horiz ? DesktopGrid.Spacing.Width : DesktopGrid.Spacing.Height;
            if (len <= w) return; // 邊只有一格：直接拖曳整條邊
            s0 = Math.Clamp(horiz ? DesktopGrid.FloorX(c) : DesktopGrid.FloorY(c), lo, hi - w);
        }
        else
        {
            w = Math.Clamp(RoundGrid(len / 3), Math.Max(2 * grid, (int)(24 * scale)), (int)(220 * scale));
            if (len < w + (int)(12 * scale)) return; // 邊太短：直接拖曳整條邊
            s0 = Math.Clamp(RoundGrid(c - w / 2), lo, hi - w);
        }
        int s1 = s0 + w;
        bool forward = horiz ? b.X > a.X : b.Y > a.Y;
        Point P(int v) => horiz ? new Point(v, a.Y) : new Point(a.X, v);
        var p1 = P(forward ? s0 : s1); var p4 = P(forward ? s1 : s0);
        var list = pts.ToList();
        list.InsertRange(k + 1, new[] { p1, p1, p4, p4 });
        op.Work0 = list.ToArray();
        op.Index = k + 2;
    }

    /// <summary>Shift+拖曳頂點：把角落切成缺角（或往外拉成 L 形延伸）。</summary>
    private static void PrepareNotch(DragOp op)
    {
        var list = op.Work0.ToList();
        var v = list[op.Index];
        list.InsertRange(op.Index + 1, new[] { v, v });
        op.Work0 = list.ToArray();
        op.Notch = true;
    }

    private void UpdateDrag(Point pt)
    {
        var op = _drag!;
        var model = op.Win.Model;
        if (!op.Started)
        {
            var ds = SystemInformation.DragSize;
            if (Math.Abs(pt.X - op.Start.X) < ds.Width && Math.Abs(pt.Y - op.Start.Y) < ds.Height) return;
            op.Started = true;
        }
        bool snap = _cfg.SnapEnabled && !Native.KeyDown(Native.VK_MENU);
        int dx = pt.X - op.Start.X, dy = pt.Y - op.Start.Y;
        var w0 = op.Work0;
        var work = (Point[])w0.Clone();
        int n = work.Length;
        float scale = op.Win.Layout.Scale;

        if (op.Kind is PartKind.Body or PartKind.Title)
        {
            var bb = RectiPoly.Bounds(w0);
            int ox = SnapSpan(bb.Left + dx, bb.Width, true, model.Id, snap, scale) - bb.Left;
            int oy = SnapSpan(bb.Top + dy, bb.Height, false, model.Id, snap, scale) - bb.Top;
            for (int i = 0; i < n; i++) work[i].Offset(ox, oy);
        }
        else if (op.Notch)
        {
            int k = op.Index, k1 = (k + 1) % n, k2 = (k + 2) % n;
            var v = w0[k];
            var ex = new HashSet<int> { k, k1, k2, (k - 1 + n) % n, (k + 3) % n };
            int cx = SnapCoord(v.X + dx, true, model.Id, w0, ex, snap, scale);
            int cy = SnapCoord(v.Y + dy, false, model.Id, w0, ex, snap, scale);
            bool prevH = ((k - 1 + n) % n) % 2 == 0;
            if (prevH) { work[k] = new(cx, v.Y); work[k1] = new(cx, cy); work[k2] = new(v.X, cy); }
            else { work[k] = new(v.X, cy); work[k1] = new(cx, cy); work[k2] = new(cx, v.Y); }
        }
        else if (op.Kind == PartKind.Edge)
        {
            int k = op.Index, k2 = (k + 1) % n;
            var ex = new HashSet<int> { k, k2 };
            if (k % 2 == 0)
            {
                int y = SnapCoord(w0[k].Y + dy, false, model.Id, w0, ex, snap, scale);
                work[k].Y = work[k2].Y = y;
            }
            else
            {
                int x = SnapCoord(w0[k].X + dx, true, model.Id, w0, ex, snap, scale);
                work[k].X = work[k2].X = x;
            }
        }
        else if (op.Kind == PartKind.Vertex)
        {
            int k = op.Index, prev = (k - 1 + n) % n, next = (k + 1) % n;
            var ex = new HashSet<int> { k, prev, next };
            int x = SnapCoord(w0[k].X + dx, true, model.Id, w0, ex, snap, scale);
            int y = SnapCoord(w0[k].Y + dy, false, model.Id, w0, ex, snap, scale);
            if (k % 2 == 0) { work[k].Y = work[next].Y = y; work[k].X = work[prev].X = x; }
            else { work[k].X = work[next].X = x; work[k].Y = work[prev].Y = y; }
        }

        if (RectiPoly.Area2(work) <= 0) return; // 不允許翻轉
        var valid = RectiPoly.Validate(work, Math.Max(6, (int)(12 * scale)));
        if (valid == null) return;
        if (!valid.SequenceEqual(model.Points)) op.Changed = true;
        model.Points = valid;
        op.Win.Render();
    }

    private void CancelDrag()
    {
        if (_drag == null) return;
        var op = _drag;
        _drag = null;
        if (Native.GetCapture() == op.Win.Handle) Native.ReleaseCapture();
        if (op.Changed) RestoreFrames(op.Snapshot);
    }

    // ================= 吸附 =================

    private int Grid => Math.Max(1, _cfg.GridSize);

    /// <summary>目前是否以桌面圖示格線吸附。</summary>
    private bool IconGrid => _cfg.SnapEnabled && _cfg.SnapToIconGrid && DesktopGrid.Available;

    /// <summary>方向鍵微調的距離：預設移動一格（圖示格或格線），fine 為 1 像素。</summary>
    public int NudgeStep(bool horizontal, bool fine) =>
        fine ? 1 : IconGrid ? (horizontal ? DesktopGrid.Spacing.Width : DesktopGrid.Spacing.Height) : Grid;

    /// <summary>把所有框線的頂點對齊到最近的桌面圖示格線（可復原）。</summary>
    public void AlignAllToIconGrid()
    {
        if (Busy) return;
        DesktopGrid.Refresh();
        if (!DesktopGrid.Available)
        {
            _toolbar?.Flash("讀不到桌面圖示格線（請確認桌面圖示已顯示）", true);
            return;
        }
        var snap = SnapshotFrames();
        int changed = 0, failed = 0;
        foreach (var f in _cfg.Frames)
        {
            var pts = f.Points.Select(p => new Point(DesktopGrid.SnapX(p.X), DesktopGrid.SnapY(p.Y))).ToArray();
            var v = RectiPoly.Validate(pts, 1);
            if (v == null) failed++;
            else if (!v.SequenceEqual(f.Points)) { f.Points = v; changed++; }
        }
        if (changed > 0) { PushUndo(snap); SyncWindows(); Save(); }
        _toolbar?.Flash(changed == 0 && failed == 0 ? "所有框線都已對齊圖示格線"
            : $"已對齊 {changed} 個框線" + (failed > 0 ? $"，{failed} 個框線太小無法對齊（請手動調整）" : "") + "　·　Ctrl+Z 可復原", failed > 0);
    }
    private int RoundGrid(int v) => (int)Math.Round(v / (double)Grid) * Grid;

    private IEnumerable<int> Candidates(bool isX, string? selfId, Point[]? selfPts, HashSet<int>? exclude)
    {
        foreach (var f in _cfg.Frames)
        {
            if (f.Id == selfId) continue;
            var p = f.Pts;
            for (int i = isX ? 0 : 1; i < p.Length; i += 2) yield return p[i];
        }
        if (selfPts != null)
            for (int i = 0; i < selfPts.Length; i++)
                if (exclude == null || !exclude.Contains(i)) yield return isX ? selfPts[i].X : selfPts[i].Y;
        foreach (var s in Screen.AllScreens)
        {
            var wa = s.WorkingArea;
            if (isX) { yield return wa.Left; yield return wa.Right; }
            else { yield return wa.Top; yield return wa.Bottom; }
        }
    }

    /// <summary>單一座標吸附：先找附近的其他框線/自身頂點/螢幕邊緣，否則對齊格線。</summary>
    private int SnapCoord(int v, bool isX, string? selfId, Point[]? selfPts, HashSet<int>? exclude, bool snap, float scale)
    {
        if (!snap) return v;
        if (IconGrid) return isX ? DesktopGrid.SnapX(v) : DesktopGrid.SnapY(v); // 一律落在圖示之間的縫隙
        int tol = (int)(8 * scale), best = v, bestD = tol + 1;
        foreach (var c in Candidates(isX, selfId, selfPts, exclude))
        {
            int d = Math.Abs(c - v);
            if (d < bestD) { bestD = d; best = c; }
        }
        return bestD <= tol ? best : RoundGrid(v);
    }

    /// <summary>整體移動吸附：左/上緣或右/下緣任一靠近其他框線即吸附，否則左上角對齊格線。</summary>
    private int SnapSpan(int start, int size, bool isX, string selfId, bool snap, float scale)
    {
        if (!snap) return start;
        if (IconGrid) return isX ? DesktopGrid.SnapX(start) : DesktopGrid.SnapY(start);
        int tol = (int)(8 * scale), bestD = tol + 1, best = start;
        foreach (var c in Candidates(isX, selfId, null, null))
        {
            int d1 = Math.Abs(c - start), d2 = Math.Abs(c - (start + size));
            if (d1 < bestD) { bestD = d1; best = c; }
            if (d2 < bestD) { bestD = d2; best = c - size; }
        }
        return bestD <= tol ? best : RoundGrid(start);
    }

    // ================= 繪製新框線／加入／挖除 =================

    /// <summary>拖曳進行中時不執行鍵盤/工具列指令，避免狀態錯亂。</summary>
    private bool Busy => _drag != null;

    public void StartDraw(DrawMode mode)
    {
        if (Busy) return;
        if (!_edit) EnterEdit();
        if (!_edit) return;
        if (mode != DrawMode.New && Selected == null) return;
        CancelDraw();
        EndRename(true);
        var ds = new DrawSession { Mode = mode };
        foreach (var scr in Screen.AllScreens)
        {
            var f = new CaptureForm(scr.Bounds);
            f.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) DrawDown(Cursor.Position); };
            f.MouseMove += (_, _) => DrawMove(Cursor.Position);
            f.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) DrawUp(Cursor.Position);
                else if (e.Button == MouseButtons.Right) CancelDraw(); // 放開時才取消，避免右鍵選單穿透到桌面
            };
            ds.Forms.Add(f);
        }
        _draw = ds;
        foreach (var f in ds.Forms) f.Show();
        _toolbar?.RaiseTopmost();
        _toolbar?.TakeKeyboard();
        UpdateToolbar();
    }

    public void CancelDraw()
    {
        if (_draw == null) return;
        var ds = _draw;
        _draw = null;
        ds.Preview?.Dispose();
        foreach (var f in ds.Forms) f.Close();
        UpdateToolbar();
    }

    private Point SnapPoint(Point p)
    {
        bool snap = _cfg.SnapEnabled && !Native.KeyDown(Native.VK_MENU);
        float sc = Native.ScaleAt(p);
        return new Point(SnapCoord(p.X, true, null, null, null, snap, sc), SnapCoord(p.Y, false, null, null, null, snap, sc));
    }

    private void DrawDown(Point p)
    {
        if (_draw == null) return;
        _draw.Start = SnapPoint(p);
        _draw.Dragging = true;
    }

    private void DrawMove(Point p)
    {
        var ds = _draw;
        if (ds == null || !ds.Dragging) return;
        var r = RectFrom(ds.Start, SnapPoint(p));
        if (r.Width < 4 || r.Height < 4) { if (ds.Preview != null) ds.Preview.Visible = false; return; }
        if (ds.PreviewModel == null)
        {
            var style = _cfg.DefaultStyle.Clone();
            style.LineStyle = "dash"; style.Glow = false; style.FillOpacity = 14; style.LineOpacity = 100;
            if (ds.Mode == DrawMode.Add) style.Color = "#22C55E";
            else if (ds.Mode == DrawMode.Cut) style.Color = "#EF4444";
            ds.PreviewModel = new FrameModel { Title = "", Style = style };
        }
        ds.PreviewModel.Points = RectiPoly.FromRect(r);
        if (ds.Preview == null) ds.Preview = new FrameWindow(ds.PreviewModel, this, preview: true);
        else { ds.Preview.Visible = true; ds.Preview.Render(); }
    }

    private void DrawUp(Point p)
    {
        var ds = _draw;
        if (ds == null || !ds.Dragging) return;
        ds.Dragging = false;
        var r = RectFrom(ds.Start, SnapPoint(p));
        float sc = Native.ScaleAt(p);
        if (r.Width < 40 * sc || r.Height < 30 * sc)
        {
            if (ds.Preview != null) ds.Preview.Visible = false;
            _toolbar?.Flash("範圍太小，請拖曳出較大的區域", true);
            return;
        }
        var mode = ds.Mode;
        CancelDraw();
        var snapshot = SnapshotFrames();
        if (mode == DrawMode.New)
        {
            var f = new FrameModel
            {
                Title = NextTitle(),
                Style = _cfg.DefaultStyle.Clone(),
                Points = RectiPoly.FromRect(r),
            };
            _cfg.Frames.Add(f);
            PushUndo(snapshot);
            SyncWindows();
            Select(f.Id);
            Save();
            var w = SelWin;
            if (w != null) BeginRename(w);
        }
        else
        {
            var sel = Selected;
            if (sel == null) return;
            var res = RectiPoly.Boolean(sel.Points, r, mode == DrawMode.Add);
            if (res == null)
            {
                _toolbar?.Flash(mode == DrawMode.Add
                    ? "無法加入：區塊需與框線相接，且不能形成中空或只以角落相連"
                    : "無法挖除：結果不能分裂成多塊或形成中空", true);
                return;
            }
            sel.Points = res;
            PushUndo(snapshot);
            SelWin?.Render();
            Save();
        }
    }

    private static Rectangle RectFrom(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    private string NextTitle()
    {
        for (int i = 1; ; i++)
        {
            var t = i == 1 ? "新分類" : $"新分類 {i}";
            if (!_cfg.Frames.Any(f => f.Title == t)) return t;
        }
    }

    // ================= 重新命名 =================

    public void RenameSelected() { var w = SelWin; if (w != null && !Busy) BeginRename(w); }

    private void BeginRename(FrameWindow w)
    {
        EndRename(true);
        var f = w.Model;
        var L = w.Layout;
        RectangleF r;
        if (L.HasTitle) r = L.TitleRect;
        else
        {
            // 無標題時：以暫時標題計算位置
            var tmp = f.Clone(); tmp.Title = "標題";
            r = FrameLayout.Compute(tmp).TitleRect;
        }
        _rename = new TitleEditor(f, r, L.Scale, text =>
        {
            _rename = null;
            if (text == null || text == f.Title) return;
            var snap = SnapshotFrames();
            f.Title = text;
            PushUndo(snap);
            w.Render();
            Save();
            UpdateToolbar();
        });
        _rename.Show();
        _rename.Activate();
    }

    private void EndRename(bool commit)
    {
        var r = _rename;
        _rename = null;
        if (r != null && !r.IsDisposed) r.Finish(commit);
    }

    // ================= 其他指令 =================

    public void DeleteSelected()
    {
        var f = Selected;
        if (f == null || Busy) return;
        _styleForm?.CommitPending();
        var snap = SnapshotFrames();
        _cfg.Frames.Remove(f);
        _styleForm?.Close();
        _selId = null;
        PushUndo(snap);
        SyncWindows();
        Save();
        _toolbar?.Flash($"已刪除「{Name(f)}」　·　Ctrl+Z 可復原", false);
    }

    public void DuplicateSelected()
    {
        var f = Selected;
        if (f == null || Busy) return;
        var snap = SnapshotFrames();
        var c = f.Clone();
        c.Id = Guid.NewGuid().ToString("N");
        c.Title = string.IsNullOrWhiteSpace(f.Title) ? "" : f.Title + " 複本";
        int off = Math.Max(Grid * 2, 20);
        c.Points = f.Points.Select(p => new Point(p.X + off, p.Y + off)).ToArray();
        _cfg.Frames.Add(c);
        PushUndo(snap);
        SyncWindows();
        Select(c.Id);
        Save();
    }

    public void Nudge(int dx, int dy)
    {
        var f = Selected;
        if (f == null || Busy) return;
        var snap = SnapshotFrames();
        f.Points = f.Points.Select(p => new Point(p.X + dx, p.Y + dy)).ToArray();
        PushUndo(snap, coalesceKey: "nudge:" + f.Id);
        SelWin?.Render();
        ArrangeZOrder();
        Save();
    }

    public void ApplyTemplate(string kind)
    {
        var f = Selected;
        if (f == null || Busy) return;
        var snap = SnapshotFrames();
        f.Points = RectiPoly.Template(kind, RectiPoly.Bounds(f.Points), Grid);
        PushUndo(snap);
        SelWin?.Render();
        Save();
    }

    public void ShowStyleForSelected()
    {
        var f = Selected;
        if (f == null) return;
        if (_styleForm == null || _styleForm.IsDisposed)
        {
            _styleForm = new StyleForm(this);
            _styleForm.FormClosed += (_, _) => { _styleForm = null; ScheduleTrim(); };
        }
        _styleForm.Bind(f);
        _styleForm.Show();
        _styleForm.Activate();
    }

    /// <summary>樣式視窗即時預覽用：重繪指定框線。</summary>
    public void RefreshFrame(FrameModel f)
    {
        _windows.FirstOrDefault(w => w.Model == f)?.Render();
    }

    public void RefreshAllFrames()
    {
        foreach (var w in _windows) w.Render();
    }

    /// <summary>把樣式套用到所有框線（可復原）。</summary>
    public void ApplyStyleToAll(FrameStyle style)
    {
        var snap = SnapshotFrames();
        foreach (var f in _cfg.Frames) if (f.Style != style) f.Style.CopyFrom(style);
        PushUndo(snap);
        RefreshAllFrames();
        Save();
    }

    public void CommitStyleChange(string snapshot)
    {
        if (snapshot != SnapshotFrames()) PushUndo(snapshot);
        Save();
    }

    public string SnapshotFrames() => ConfigStore.SerializeFrames(_cfg.Frames);

    private void ShowFrameMenu(FrameWindow w, Point pt)
    {
        var menu = ModernMenuRenderer.Create();
        menu.Item("重新命名", () => BeginRename(w), "F2");
        menu.Item("樣式…", ShowStyleForSelected);
        var shape = new ToolStripMenuItem("套用形狀");
        shape.Item("長方形", () => ApplyTemplate("rect"));
        shape.Item("L 形", () => ApplyTemplate("L"));
        shape.Item("反 L 形", () => ApplyTemplate("J"));
        shape.Item("凸字形", () => ApplyTemplate("convex"));
        shape.Item("T 字形", () => ApplyTemplate("T"));
        shape.Item("凹字形", () => ApplyTemplate("U"));
        shape.DropDown.Renderer = menu.Renderer;
        menu.Items.Add(shape);
        menu.Sep();
        menu.Item("加入區塊（擴大範圍）", () => StartDraw(DrawMode.Add));
        menu.Item("挖除區塊（縮小範圍）", () => StartDraw(DrawMode.Cut));
        menu.Item("複製框線", DuplicateSelected, "Ctrl+D");
        menu.Sep();
        menu.Item("刪除框線", DeleteSelected, "Del", danger: true);
        menu.Closed += (_, _) => _invoker.BeginInvoke(menu.Dispose);
        _toolbar?.TakeKeyboard();
        menu.Show(pt);
    }

    // ================= 復原／重做 =================

    private string? _lastCoalesce;
    private DateTime _lastCoalesceAt;

    private void PushUndo(string snapshot, string? coalesceKey = null)
    {
        _styleForm?.CommitPending(); // 先記錄尚未提交的樣式修改，維持復原順序
        // 連續微調（方向鍵）合併為一步
        if (coalesceKey != null && coalesceKey == _lastCoalesce && (DateTime.Now - _lastCoalesceAt).TotalSeconds < 1.5 && _undo.Count > 0)
        {
            _lastCoalesceAt = DateTime.Now;
            return;
        }
        _lastCoalesce = coalesceKey;
        _lastCoalesceAt = DateTime.Now;
        _undo.Add(snapshot);
        if (_undo.Count > 80) _undo.RemoveAt(0);
        _redo.Clear();
        UpdateToolbar();
    }

    public void Undo()
    {
        if (Busy) return;
        _styleForm?.CommitPending();
        if (_undo.Count == 0) return;
        CancelDraw(); EndRename(false);
        _redo.Add(SnapshotFrames());
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _lastCoalesce = null;
        RestoreFrames(s);
    }

    public void Redo()
    {
        if (Busy) return;
        _styleForm?.CommitPending();
        if (_redo.Count == 0) return;
        CancelDraw(); EndRename(false);
        _undo.Add(SnapshotFrames());
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _lastCoalesce = null;
        RestoreFrames(s);
    }

    private void RestoreFrames(string json)
    {
        _cfg.Frames = ConfigStore.DeserializeFrames(json);
        SyncWindows();
        if (_styleForm != null && !_styleForm.IsDisposed)
        {
            var f = Selected;
            if (f != null) _styleForm.Rebind(f); else _styleForm.Close();
        }
        Save();
        UpdateToolbar();
    }

    /// <summary>讓框線視窗與設定中的框線清單一致（依 Id 對應，重用既有視窗）。</summary>
    private void SyncWindows()
    {
        var byId = new Dictionary<string, FrameWindow>();
        foreach (var w in _windows) byId.TryAdd(w.Model.Id, w);
        var list = new List<FrameWindow>();
        foreach (var f in _cfg.Frames)
        {
            if (byId.Remove(f.Id, out var w) && w.Alive) w.Model = f;
            else { w?.Dispose(); w = new FrameWindow(f, this); }
            w.Selected = f.Id == _selId;
            w.EditMode = _edit;
            w.Visible = _cfg.FramesVisible;
            w.Render();
            list.Add(w);
        }
        foreach (var w in byId.Values) w.Dispose();
        _windows = list;
        ArrangeZOrder();
        if (_selId != null && !_cfg.Frames.Any(f => f.Id == _selId)) _selId = null;
        UpdateToolbar();
    }

    /// <summary>
    /// 固定框線視窗的前後順序：越下方（上緣越低）的框線越前面。
    /// 標題坐在自己框線的上緣之上，正好落在上方框線的下緣附近；這樣排列可確保標題永遠蓋在鄰近框線的線條之上。
    /// 全部框線都插在桌面正上方，因此仍在待辦清單與所有一般視窗之下。
    /// </summary>
    private void ArrangeZOrder()
    {
        // SendToBottom 會插到桌面正上方（其他視窗之下）：依「由下往上」順序呼叫，最後呼叫的（最上方的框線）最底層
        FrameWindow? top = null;
        foreach (var w in _windows.Where(w => w.Alive && w.Visible)
                     .OrderByDescending(w => w.Layout.Bounds.Top).ThenByDescending(w => w.Layout.Bounds.Left))
        {
            DesktopHost.SendToBottom(w.Handle);
            top ??= w;
        }
        // 待辦清單放在所有框線之上（仍在一般視窗之下）
        if (top != null && _todo is { IsDisposed: false, IsHandleCreated: true, Visible: true })
            DesktopHost.PlaceAbove(_todo.Handle, top.Handle);
    }
}
