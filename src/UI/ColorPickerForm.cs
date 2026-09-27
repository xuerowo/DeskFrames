using System.Drawing.Drawing2D;

namespace DeskFrames;

/// <summary>
/// 自製調色盤（取代 Windows 內建色彩對話框）：飽和度／亮度方塊 + 色相條、色號與 RGB 輸入、常用色票、新舊色對照。
/// 調整時透過 <see cref="Preview"/> 即時預覽；按「取消」由呼叫端恢復原色。
/// </summary>
public sealed class ColorPickerForm : ModernForm
{
    private readonly HsvPicker _picker = new();
    private readonly ColorFields _fields;
    private readonly Color _original;
    private bool _syncing;

    /// <summary>顏色改變時（拖曳中也會觸發）。</summary>
    public event Action<Color>? Preview;
    public Color Color { get; private set; }

    public ColorPickerForm(Color initial)
    {
        _original = initial;
        Color = initial;
        Text = "選擇顏色";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Body.Pad96 = 20;
        Body.AutoScroll = false;

        _fields = new ColorFields(initial);
        Body.Add(_picker);
        Body.Add(_fields);

        _picker.Color = initial;
        _picker.ColorChanged += c => SetColor(c, fromPicker: true);
        _fields.ColorChanged += c => SetColor(c, fromPicker: false);

        var cancel = new ModernButton { Text = "取消", TabStop = false };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        var ok = new ModernButton { Text = "確定", Kind = ButtonKind.Primary, TabStop = false };
        ok.Click += (_, _) => Accept();
        Footer.Controls.AddRange(new Control[] { cancel, ok });
        HasFooter = true;
        FitHeightToContent(380);
    }

    private void Accept()
    {
        _fields.CommitPending();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SetColor(Color c, bool fromPicker)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            Color = c;
            if (!fromPicker) _picker.Color = c;
            _fields.SetColor(c, _original);
            Preview?.Invoke(c);
        }
        finally { _syncing = false; }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _fields.SetColor(Color, _original);
        _fields.FocusHex();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter：若在輸入框中先套用該欄位（由欄位自行處理），否則等同「確定」
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; return; }
        if (e.KeyCode == Keys.Enter && ActiveControl is not TextBoxBase)
        {
            Accept(); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }

    // ================= 飽和度／亮度方塊 + 色相條 =================

    private sealed class HsvPicker : ModernControl
    {
        private double _h, _s, _v;
        private int _drag; // 0=無 1=方塊 2=色相
        public event Action<Color>? ColorChanged;
        public override int DipHeight => 214;

        public HsvPicker() { Cursor = Cursors.Cross; }

        public Color Color
        {
            get => FromHsv(_h, _s, _v);
            set
            {
                ToHsv(value, out double h, out _s, out _v);
                if (_s > 0.001 && _v > 0.001) _h = h; // 灰階時保留原本色相
                Invalidate();
            }
        }

        private RectangleF Square => new(S(8), S(8), Width - S(16), S(164)); // 四周留空，游標在邊緣時不會被切掉
        private RectangleF HueBar => new(0, S(188), Width, S(18));

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = HueBar.Contains(e.Location) || e.Y > Square.Bottom ? 2 : 1;
            Capture = true;
            Update(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != 0) Update(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = 0; Capture = false; }

        private void Update(Point p)
        {
            if (_drag == 1)
            {
                var r = Square;
                _s = Math.Clamp((p.X - r.X) / r.Width, 0, 1);
                _v = 1 - Math.Clamp((p.Y - r.Y) / r.Height, 0, 1);
            }
            else
            {
                var r = HueBar;
                _h = Math.Clamp((p.X - r.X) / r.Width, 0, 1) * 360;
            }
            Invalidate();
            ColorChanged?.Invoke(Color);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var sq = Square;
            using (var clip = Theme.RoundRect(sq, S(8)))
            {
                g.SetClip(clip);
                using (var hue = new SolidBrush(FromHsv(_h, 1, 1))) g.FillRectangle(hue, sq);
                using (var white = new LinearGradientBrush(sq, Color.White, Color.FromArgb(0, Color.White), 0f)) g.FillRectangle(white, sq);
                using (var black = new LinearGradientBrush(sq, Color.FromArgb(0, Color.Black), Color.Black, 90f)) g.FillRectangle(black, sq);
                g.ResetClip();
            }
            // 方塊游標
            float cx = sq.X + (float)_s * sq.Width, cy = sq.Y + (float)(1 - _v) * sq.Height, rad = S(7);
            using (var p1 = new Pen(Color.FromArgb(120, 0, 0, 0), S(3))) g.DrawEllipse(p1, cx - rad, cy - rad, rad * 2, rad * 2);
            using (var p2 = new Pen(Color.White, S(2))) g.DrawEllipse(p2, cx - rad, cy - rad, rad * 2, rad * 2);

            // 色相條
            var hb = HueBar;
            using (var path = Theme.RoundRect(hb, hb.Height / 2))
            using (var lg = new LinearGradientBrush(hb, Color.Red, Color.Red, 0f))
            {
                lg.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                    Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f },
                };
                g.FillPath(lg, path);
            }
            float hx = hb.X + (float)(_h / 360) * hb.Width;
            hx = Math.Clamp(hx, hb.X + hb.Height / 2, hb.Right - hb.Height / 2);
            var knob = new RectangleF(hx - hb.Height / 2 - S(1), hb.Y - S(1), hb.Height + S(2), hb.Height + S(2));
            using (var b = new SolidBrush(FromHsv(_h, 1, 1))) g.FillEllipse(b, knob);
            using (var p3 = new Pen(Color.White, S(2.5f))) g.DrawEllipse(p3, knob);
            using (var p4 = new Pen(Color.FromArgb(90, 0, 0, 0), S(1))) g.DrawEllipse(p4, RectangleF.Inflate(knob, S(1.5f), S(1.5f)));
        }
    }

    // ================= 新舊色、色號、RGB、常用色 =================

    private sealed class ColorFields : ModernControl
    {
        private readonly TextBox _hex = MakeBox(10);
        private readonly TextBox _r = MakeBox(3), _g = MakeBox(3), _b = MakeBox(3);
        private Color _color, _original;
        private bool _setting;
        public event Action<Color>? ColorChanged;
        public override int DipHeight => 132;

        public ColorFields(Color c)
        {
            _color = _original = c;
            Cursor = Cursors.Default;
            foreach (var t in new[] { _hex, _r, _g, _b })
            {
                Controls.Add(t);
                t.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit((TextBox)t); ((TextBox)t).SelectAll(); }
                };
                t.Leave += (_, _) => Commit(t);
                t.GotFocus += (_, _) => BeginInvoke(t.SelectAll);
                t.TextChanged += (_, _) => { if (!_setting) { if (t == _hex) LiveHex(); else LiveRgb(); } };
            }
            SetColor(c, c);
        }

        private static TextBox MakeBox(int max) => new() { BorderStyle = BorderStyle.None, MaxLength = max, TextAlign = HorizontalAlignment.Center };

        public void FocusHex() { _hex.Focus(); _hex.SelectAll(); }

        public void SetColor(Color c, Color original)
        {
            _color = c; _original = original;
            _setting = true;
            if (!_hex.Focused) _hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            if (!_r.Focused) _r.Text = c.R.ToString();
            if (!_g.Focused) _g.Text = c.G.ToString();
            if (!_b.Focused) _b.Text = c.B.ToString();
            _setting = false;
            Invalidate();
        }

        /// <summary>色號輸入滿 6 碼即套用（不必按 Enter）。</summary>
        private void LiveHex()
        {
            var s = _hex.Text.Trim().TrimStart('#');
            if (s.Length == 6 && ColorRow.TryParseColor(s, out var c)) ColorChanged?.Invoke(c);
        }

        /// <summary>按「確定」前：把輸入框中尚未套用的內容（例如 #58F 或 91, 140, 255）一併套用。</summary>
        public void CommitPending()
        {
            if (_hex.Focused && ColorRow.TryParseColor(_hex.Text, out var c)) ColorChanged?.Invoke(c);
            else LiveRgb();
        }

        /// <summary>RGB 欄位輸入中即時套用（三個都合法時）。</summary>
        private void LiveRgb()
        {
            if (byte.TryParse(_r.Text, out var r) && byte.TryParse(_g.Text, out var g) && byte.TryParse(_b.Text, out var b))
                ColorChanged?.Invoke(Color.FromArgb(r, g, b));
        }

        private void Commit(TextBox t)
        {
            if (t == _hex)
            {
                if (ColorRow.TryParseColor(_hex.Text, out var c)) ColorChanged?.Invoke(c);
                else System.Media.SystemSounds.Beep.Play();
            }
            // 格式錯誤時恢復；正確時統一格式（包含目前正在輸入的欄位）
            _setting = true;
            _hex.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
            _r.Text = _color.R.ToString(); _g.Text = _color.G.ToString(); _b.Text = _color.B.ToString();
            _setting = false;
            Invalidate();
        }

        // 版面：第一列 新/原 色塊 + 色號；第二列 R G B；第三列 常用色
        private RectangleF NewSwatch => new(0, 0, S(56), S(34));
        private RectangleF OldSwatch => new(S(56), 0, S(56), S(34));
        private RectangleF HexField => new(S(170), 0, Width - S(170), S(34));
        private RectangleF RgbField(int i) { float w = (Width - S(24)) / 3f; return new(i * (w + S(12)), S(46), w, S(34)); }
        private RectangleF PaletteSwatch(int i)
        {
            int n = Theme.Palette.Length; float gap = S(8);
            float d = Math.Min(S(26), (Width - gap * (n - 1)) / n);
            return new(i * (d + gap), S(96), d, d);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var font = Theme.Ui(10f);
            Place(_hex, HexField, S(34), font);
            Place(_r, RgbField(0), S(28), font);
            Place(_g, RgbField(1), S(28), font);
            Place(_b, RgbField(2), S(28), font);
        }

        private void Place(TextBox t, RectangleF f, float leftPad, Font font)
        {
            t.Font = font;
            int h = t.PreferredHeight;
            t.SetBounds((int)(f.X + leftPad), (int)(f.Y + (f.Height - h) / 2), (int)(f.Width - leftPad - S(10)), h);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < Theme.Palette.Length; i++)
                if (RectangleF.Inflate(PaletteSwatch(i), S(3), S(3)).Contains(e.Location)) { ColorChanged?.Invoke(Theme.Palette[i]); return; }
            if (OldSwatch.Contains(e.Location)) ColorChanged?.Invoke(_original); // 點原色可恢復
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hand = OldSwatch.Contains(e.Location) ||
                        Enumerable.Range(0, Theme.Palette.Length).Any(i => RectangleF.Inflate(PaletteSwatch(i), S(3), S(3)).Contains(e.Location));
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            foreach (var t in new[] { _hex, _r, _g, _b })
            {
                if (t.BackColor != Theme.Field) t.BackColor = Theme.Field;
                if (t.ForeColor != Theme.Text) t.ForeColor = Theme.Text;
            }

            // 新／原 色塊（左圓角、右圓角）
            var both = RectangleF.Union(NewSwatch, OldSwatch);
            using (var path = Theme.RoundRect(both, S(8)))
            {
                g.SetClip(path);
                using (var nb = new SolidBrush(_color)) g.FillRectangle(nb, NewSwatch);
                using (var ob = new SolidBrush(_original)) g.FillRectangle(ob, OldSwatch);
                g.ResetClip();
                using var pen = new Pen(Theme.Border); g.DrawPath(pen, path);
            }
            DrawText(g, "新", Theme.Ui(8f), Theme.ContrastText(_color), Rectangle.Round(NewSwatch), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            DrawText(g, "原", Theme.Ui(8f), Theme.ContrastText(_original), Rectangle.Round(OldSwatch), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            DrawField(g, HexField, "色號", _hex.Focused);
            string[] names = { "R", "G", "B" };
            var boxes = new[] { _r, _g, _b };
            for (int i = 0; i < 3; i++) DrawField(g, RgbField(i), names[i], boxes[i].Focused);

            for (int i = 0; i < Theme.Palette.Length; i++)
            {
                var r = PaletteSwatch(i);
                using var b = new SolidBrush(Theme.Palette[i]);
                g.FillEllipse(b, r);
                if (Theme.Palette[i].ToArgb() == _color.ToArgb())
                {
                    using var ring = new Pen(Theme.Text, S(2));
                    g.DrawEllipse(ring, RectangleF.Inflate(r, S(3), S(3)));
                }
                else if (Theme.Palette[i].GetBrightness() > 0.9f) { using var pen = new Pen(Theme.Border); g.DrawEllipse(pen, r); }
            }
        }

        private void DrawField(Graphics g, RectangleF f, string label, bool focused)
        {
            using (var path = Theme.RoundRect(f, S(6)))
            using (var bg = new SolidBrush(Theme.Field))
            using (var pen = new Pen(focused ? Theme.Accent : Theme.Border, S(focused ? 1.5f : 1f)))
            {
                g.FillPath(bg, path);
                g.DrawPath(pen, path);
            }
            DrawText(g, label, Theme.Ui(8.5f), Theme.TextDim,
                new Rectangle((int)(f.X + S(10)), (int)f.Y, (int)S(40), (int)f.Height), TextFormatFlags.VerticalCenter);
        }
    }

    // ================= HSV 轉換 =================

    private static Color FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = (h / 60) switch
        {
            < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x),
            < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    private static void ToHsv(Color col, out double h, out double s, out double v)
    {
        double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d == 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }
}
