namespace DeskFrames;

/// <summary>直接在框線標題位置出現的就地改名輸入框。Enter 確認、Esc 取消、點到別處自動確認。</summary>
public sealed class TitleEditor : Form
{
    private readonly TextBox _box;
    private readonly Action<string?> _done;
    private bool _finished;

    public TitleEditor(FrameModel f, RectangleF titleRect, float scale, Action<string?> done)
    {
        _done = done;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        var color = f.Style.ColorValue;
        BackColor = color;

        var font = FrameLayout.TitleFontFor(f.Style.TitleSize, scale);
        int padX = (int)(8 * scale);
        int h = Math.Max((int)Math.Ceiling(titleRect.Height), (int)(26 * scale));
        int w = Math.Max((int)Math.Ceiling(titleRect.Width) + (int)(24 * scale), (int)(200 * scale));
        Bounds = new Rectangle((int)titleRect.X, (int)(titleRect.Y + titleRect.Height - h), w, h);

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = font,
            BackColor = color,
            ForeColor = Theme.ContrastText(color),
            Text = f.Title,
            MaxLength = 60,
        };
        Controls.Add(_box);
        int boxH = _box.PreferredHeight;
        _box.SetBounds(padX, (h - boxH) / 2, w - padX * 2, boxH);
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Finish(true); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Finish(false); }
        };
    }

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
        int corner = 3; // DWMWCP_ROUNDSMALL
        Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_finished && IsHandleCreated) BeginInvoke(() => Finish(true));
    }

    public void Finish(bool commit)
    {
        if (_finished) return;
        _finished = true;
        var text = _box.Text.Trim();
        Close();
        _done(commit ? text : null);
    }
}
