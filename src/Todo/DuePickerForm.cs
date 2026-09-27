namespace DeskFrames;

/// <summary>待辦事項的日期時間選擇視窗：月曆 + 時間輸入與快捷時段。</summary>
internal sealed class DuePickerForm : ModernForm
{
    private readonly MiniCalendar _cal = new();
    private readonly TimeRow _time = new();
    private readonly NoteLabel _summary = new() { Lines = 1 };

    /// <summary>確定後的時間；按「清除時間」時為 null。</summary>
    public DateTime? Value { get; private set; }

    public DuePickerForm(DateTime? current)
    {
        Text = "設定時間";
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterParent;
        Body.Pad96 = 18;
        Body.AutoScroll = false;

        var now = DateTime.Now;
        var init = current ?? new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
        _cal.Selected = init.Date;
        _time.SetTime(init.Hour, init.Minute);

        Body.Add(_cal);
        Body.Add(_time);
        Body.Add(_summary);
        _cal.SelectionChanged += UpdateSummary;
        _time.TimeChanged += UpdateSummary;
        UpdateSummary();

        var buttons = new List<Control>();
        if (current != null)
        {
            var clear = new ModernButton { Text = "清除時間", TabStop = false };
            clear.Click += (_, _) => { Value = null; DialogResult = DialogResult.OK; Close(); };
            buttons.Add(clear);
        }
        var cancel = new ModernButton { Text = "取消", TabStop = false };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var ok = new ModernButton { Text = "確定", Kind = ButtonKind.Primary, TabStop = false };
        ok.Click += (_, _) => Accept();
        buttons.Add(cancel); buttons.Add(ok);
        Footer.Controls.AddRange(buttons.ToArray());
        HasFooter = true;
        FitHeightToContent(340);
    }

    /// <summary>顯示視窗並回傳結果：changed=false 表示取消；value=null 表示清除時間。</summary>
    public static bool Pick(IWin32Window? owner, DateTime? current, out DateTime? value)
    {
        using var f = new DuePickerForm(current);
        bool ok = f.ShowDialog(owner) == DialogResult.OK;
        value = f.Value;
        return ok;
    }

    private DateTime Combined => _cal.Selected.Date.AddHours(_time.Hour).AddMinutes(_time.Minute);

    private void UpdateSummary()
    {
        var v = Combined;
        _summary.Text = v <= DateTime.Now
            ? $"時間：{TodoDue.Format(v, DateTime.Now)}（已過去）"
            : $"將於 {TodoDue.Format(v, DateTime.Now)} 提醒";
        _summary.Invalidate();
    }

    private void Accept()
    {
        if (!_time.Valid) { System.Media.SystemSounds.Beep.Play(); _time.FocusBox(); return; }
        Value = Combined;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _time.FocusBox();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; return; }
        if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; e.SuppressKeyPress = true; return; }
        base.OnKeyDown(e);
    }

    // ================= 月曆 =================

    private sealed class MiniCalendar : ModernControl
    {
        private DateTime _month, _selected;
        private int _hoverCell = -1, _hoverNav; // _hoverNav: -1 上月, 1 下月
        public event Action? SelectionChanged;
        public override int DipHeight => 264;

        public DateTime Selected
        {
            get => _selected;
            set { _selected = value.Date; _month = new DateTime(value.Year, value.Month, 1); Invalidate(); }
        }

        private RectangleF PrevRect => new(Width - S(70), S(2), S(32), S(30));
        private RectangleF NextRect => new(Width - S(34), S(2), S(32), S(30));
        private DateTime FirstCell => _month.AddDays(-(int)_month.DayOfWeek);
        private float CellW => Width / 7f;
        private RectangleF Cell(int i) => new(i % 7 * CellW, S(66) + i / 7 * S(33), CellW, S(33));

        private int CellAt(Point p)
        {
            for (int i = 0; i < 42; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int c = CellAt(e.Location);
            int nav = PrevRect.Contains(e.Location) ? -1 : NextRect.Contains(e.Location) ? 1 : 0;
            if (c != _hoverCell || nav != _hoverNav) { _hoverCell = c; _hoverNav = nav; Invalidate(); }
            Cursor = c >= 0 || nav != 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverCell = -1; _hoverNav = 0; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (PrevRect.Contains(e.Location)) { _month = _month.AddMonths(-1); Invalidate(); return; }
            if (NextRect.Contains(e.Location)) { _month = _month.AddMonths(1); Invalidate(); return; }
            int c = CellAt(e.Location);
            if (c < 0) return;
            Selected = FirstCell.AddDays(c);
            SelectionChanged?.Invoke();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (e is HandledMouseEventArgs h) h.Handled = true;
            _month = _month.AddMonths(e.Delta > 0 ? -1 : 1);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            DrawText(g, $"{_month.Year} 年 {_month.Month} 月", Theme.Ui(11f, FontStyle.Bold), Theme.Text,
                new Rectangle(0, (int)S(2), Width / 2, (int)S(30)), TextFormatFlags.VerticalCenter);
            foreach (var (rect, glyph, nav) in new[] { (PrevRect, "", -1), (NextRect, "", 1) })
            {
                if (_hoverNav == nav)
                {
                    using var p = Theme.RoundRect(rect, S(6));
                    using var b = new SolidBrush(Theme.SurfaceHover);
                    g.FillPath(b, p);
                }
                DrawText(g, glyph, Theme.Icon(9f), Theme.Text, Rectangle.Round(rect), center);
            }
            for (int i = 0; i < 7; i++)
                DrawText(g, TodoDue.WeekName((DayOfWeek)i), Theme.Ui(8.5f), i is 0 or 6 ? Theme.TextFaint : Theme.TextDim,
                    new Rectangle((int)(i * CellW), (int)S(40), (int)CellW, (int)S(22)), center);

            var today = DateTime.Today;
            for (int i = 0; i < 42; i++)
            {
                var day = FirstCell.AddDays(i);
                var cell = Cell(i);
                float d = Math.Min(cell.Width, cell.Height) - S(4);
                var circle = new RectangleF(cell.X + (cell.Width - d) / 2, cell.Y + (cell.Height - d) / 2, d, d);
                bool sel = day == _selected, inMonth = day.Month == _month.Month;
                Color fg = inMonth ? Theme.Text : Theme.TextFaint;
                if (sel)
                {
                    using var b = new SolidBrush(Theme.Accent);
                    g.FillEllipse(b, circle);
                    fg = Theme.OnAccent;
                }
                else if (i == _hoverCell)
                {
                    using var b = new SolidBrush(Theme.SurfaceHover);
                    g.FillEllipse(b, circle);
                }
                if (day == today && !sel)
                {
                    using var pen = new Pen(Theme.Accent, S(1.5f));
                    g.DrawEllipse(pen, RectangleF.Inflate(circle, -S(1), -S(1)));
                    fg = Theme.Accent;
                }
                DrawText(g, day.Day.ToString(), Theme.Ui(9.5f, day == today || sel ? FontStyle.Bold : FontStyle.Regular), fg,
                    Rectangle.Round(cell), center);
            }
        }
    }

    // ================= 時間 =================

    private sealed class TimeRow : ModernControl
    {
        private static readonly (int h, int m)[] Chips = { (9, 0), (12, 0), (14, 0), (18, 0), (21, 0) };
        private readonly TextBox _box = new() { BorderStyle = BorderStyle.None, MaxLength = 5, TextAlign = HorizontalAlignment.Center };
        private bool _setting;
        private int _hoverChip = -1;
        public int Hour { get; private set; }
        public int Minute { get; private set; }
        public bool Valid { get; private set; } = true;
        public event Action? TimeChanged;
        public override int DipHeight => 82;

        public TimeRow()
        {
            Cursor = Cursors.Default;
            Controls.Add(_box);
            _box.TextChanged += (_, _) =>
            {
                if (_setting) return;
                Valid = TodoDue.TryParseTime(_box.Text, out int h, out int m);
                if (Valid) { Hour = h; Minute = m; TimeChanged?.Invoke(); }
                Invalidate();
            };
            _box.Leave += (_, _) => { if (Valid) SetTime(Hour, Minute); };
            _box.GotFocus += (_, _) => { Invalidate(); BeginInvoke(_box.SelectAll); };
            _box.LostFocus += (_, _) => Invalidate();
        }

        public void FocusBox() { _box.Focus(); _box.SelectAll(); }

        public void SetTime(int h, int m)
        {
            Hour = h; Minute = m; Valid = true;
            _setting = true;
            _box.Text = $"{h:00}:{m:00}";
            _setting = false;
            Invalidate();
            TimeChanged?.Invoke();
        }

        private RectangleF Field => new(S(46), 0, S(96), S(34));
        private RectangleF Chip(int i) { float w = S(56), gap = S(6); return new(i * (w + gap), S(46), w, S(30)); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            _box.Font = Theme.Ui(10.5f);
            var f = Field; int h = _box.PreferredHeight;
            _box.SetBounds((int)(f.X + S(8)), (int)(f.Y + (f.Height - h) / 2), (int)(f.Width - S(16)), h);
        }

        private int ChipAt(Point p)
        {
            for (int i = 0; i < Chips.Length; i++) if (Chip(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int c = ChipAt(e.Location);
            if (c != _hoverChip) { _hoverChip = c; Cursor = c >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hoverChip = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int c = ChipAt(e.Location);
            if (c >= 0) SetTime(Chips[c].h, Chips[c].m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            if (_box.BackColor != Theme.Field) _box.BackColor = Theme.Field;
            if (_box.ForeColor != Theme.Text) _box.ForeColor = Theme.Text;
            var f = Field;
            DrawText(g, "時間", Theme.Ui(9.75f), Theme.Text, new Rectangle(0, 0, (int)S(44), (int)f.Height), TextFormatFlags.VerticalCenter);
            using (var path = Theme.RoundRect(f, S(6)))
            using (var bg = new SolidBrush(Theme.Field))
            using (var pen = new Pen(!Valid ? Theme.Danger : _box.Focused ? Theme.Accent : Theme.Border, S(!Valid || _box.Focused ? 1.5f : 1f)))
            {
                g.FillPath(bg, path);
                g.DrawPath(pen, path);
            }
            DrawText(g, Valid ? "24 小時制，例如 9:30、1830" : "格式不正確，例如 09:30", Theme.Ui(8.5f), Valid ? Theme.TextFaint : Theme.Danger,
                new Rectangle((int)(f.Right + S(10)), 0, Width - (int)(f.Right + S(10)), (int)f.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            for (int i = 0; i < Chips.Length; i++)
            {
                var r = Chip(i);
                if (r.Right > Width) break;
                bool sel = Valid && Chips[i].h == Hour && Chips[i].m == Minute;
                using (var p = Theme.RoundRect(r, r.Height / 2))
                using (var b = new SolidBrush(sel ? Theme.Accent : i == _hoverChip ? Theme.SurfaceHover : Theme.Surface))
                using (var pen = new Pen(sel ? Theme.Accent : Theme.Border))
                {
                    g.FillPath(b, p);
                    g.DrawPath(pen, p);
                }
                DrawText(g, $"{Chips[i].h:00}:{Chips[i].m:00}", Theme.Ui(9f), sel ? Theme.OnAccent : Theme.Text, Rectangle.Round(r),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
