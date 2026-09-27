using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DeskFrames;

/// <summary>
/// 開發用：DeskFrames.exe --render-preview &lt;資料夾&gt;
/// 將框線（一般/編輯狀態）與各視窗離屏繪製成 PNG，用於檢查外觀，不需截取螢幕。
/// </summary>
internal static class PreviewRenderer
{
    public static void Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var cfg = ConfigStore.Load();
        Theme.Apply(cfg.Theme);
        DesktopHost.Refresh();
        DesktopGrid.Refresh();
        if (Environment.GetEnvironmentVariable("DESKFRAMES_PREVIEW_ALIGN") == "1" && DesktopGrid.Available)
            foreach (var fr in cfg.Frames)
            {
                var v = RectiPoly.Validate(fr.Points.Select(p => new Point(DesktopGrid.SnapX(p.X), DesktopGrid.SnapY(p.Y))).ToArray(), 1);
                if (v != null) fr.Points = v;
            }
        RenderFrames(cfg, Path.Combine(outDir, "frames-normal.png"), edit: false);
        RenderFrames(cfg, Path.Combine(outDir, "frames-edit.png"), edit: true);

        TodoTheme.Configure(cfg.Todo);
        Snap(new TodoWidget(cfg.Todo, () => { }), Path.Combine(outDir, "todo.png"));
        Snap(new TodoStyleForm(cfg.Todo, () => { }), Path.Combine(outDir, "todostyle.png"));
        var ctl = new PreviewHost(cfg);
        Snap(new SettingsForm(ctl.Controller), Path.Combine(outDir, "settings.png"));
        var full = new SettingsForm(ctl.Controller);
        full.Shown += (_, _) => full.ScrollBodyTo(1300);
        Snap(full, Path.Combine(outDir, "settings-full.png"));
        if (cfg.Frames.Count > 0)
        {
            var sf = new StyleForm(ctl.Controller);
            sf.Bind(cfg.Frames[0]);
            Snap(sf, Path.Combine(outDir, "style.png"));
            var sf2 = new StyleForm(ctl.Controller);
            sf2.Bind(cfg.Frames[0]);
            sf2.Shown += (_, _) => sf2.ScrollBodyTo(2000);
            Snap(sf2, Path.Combine(outDir, "style-bottom.png"));
        }
        Snap(new EditToolbar(ctl.Controller), Path.Combine(outDir, "toolbar.png"));
        Snap(new ColorPickerForm(Color.FromArgb(91, 140, 255)), Path.Combine(outDir, "picker.png"));
        Snap(new DuePickerForm(DateTime.Today.AddDays(1).AddHours(9)), Path.Combine(outDir, "due.png"));
    }

    private static void RenderFrames(AppConfig cfg, string path, bool edit)
    {
        if (cfg.Frames.Count == 0) return;
        var box = cfg.Frames.Select(f => FrameLayout.Compute(f).Window).Aggregate(Rectangle.Union);
        box.Inflate(40, 40);
        using var bmp = new Bitmap(box.Width, box.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        // 模擬桌布：斜向漸層 + 光斑
        using (var lg = new LinearGradientBrush(new Rectangle(0, 0, box.Width, box.Height),
                   Color.FromArgb(30, 44, 84), Color.FromArgb(88, 56, 110), 35f))
            g.FillRectangle(lg, 0, 0, box.Width, box.Height);
        using (var spot = new SolidBrush(Color.FromArgb(40, 255, 200, 140)))
            g.FillEllipse(spot, box.Width * 0.55f, box.Height * 0.35f, box.Width * 0.5f, box.Height * 0.8f);
        if (DesktopGrid.Available) DrawGridIcons(g, box);
        else
        // 模擬桌面圖示
        using (var icon = new SolidBrush(Color.FromArgb(200, 235, 238, 245)))
        using (var label = new SolidBrush(Color.White))
        using (var font = new Font(Theme.UiFontName, 11f, GraphicsUnit.Pixel))
        {
            var rnd = new Random(3);
            foreach (var f in cfg.Frames)
            {
                var b = RectiPoly.Bounds(f.Points);
                for (int y = b.Top + 40; y < b.Bottom - 80; y += 150)
                    for (int x = b.Left + 30; x < b.Right - 100; x += 130)
                    {
                        if (!RectiPoly.Contains(f.Points, new PointF(x + 50, y + 50))) continue;
                        g.FillRectangle(icon, x - box.X + 30, y - box.Y + 10, 60, 64);
                        g.DrawString("檔案" + rnd.Next(10, 99), font, label, x - box.X + 36, y - box.Y + 82);
                    }
            }
        }
        g.TranslateTransform(-box.X, -box.Y);
        bool first = true;
        foreach (var f in cfg.Frames.OrderBy(fr => RectiPoly.Bounds(fr.Points).Top).ThenBy(fr => RectiPoly.Bounds(fr.Points).Left))
        {
            var L = FrameLayout.Compute(f);
            var rs = new RenderState { Edit = edit };
            if (edit && first)
            {
                rs.Selected = true; rs.ShowHandles = true;
                rs.Hover = new HitPart(PartKind.Edge, 1);
                first = false;
            }
            FrameRenderer.Draw(g, f, L, rs);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    /// <summary>依真實桌面圖示格線，在每一格畫出模擬圖示與兩行文字。</summary>
    private static void DrawGridIcons(Graphics g, Rectangle box)
    {
        var sp = DesktopGrid.Spacing; var o = DesktopGrid.Origin;
        using var icon = new SolidBrush(Color.FromArgb(215, 235, 238, 245));
        using var label = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        int iw = (int)(sp.Width * 0.63);
        for (int y = DesktopGrid.FloorY(box.Top); y < box.Bottom; y += sp.Height)
            for (int x = DesktopGrid.FloorX(box.Left); x < box.Right; x += sp.Width)
            {
                int cx = x - box.X, cy = y - box.Y;
                g.FillRectangle(icon, cx + (sp.Width - iw) / 2, cy + 2, iw, iw);
                g.FillRectangle(label, cx + 6, cy + iw + 8, sp.Width - 12, 17);
                g.FillRectangle(label, cx + 16, cy + iw + 29, sp.Width - 32, 17);
            }
        _ = o;
    }

    private static void Snap(Form f, string path)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(40, 40);
        f.Opacity = 0.01;
        f.Show();
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            PrintWindow(f.Handle, hdc, 2 /*PW_RENDERFULLCONTENT*/);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(path, ImageFormat.Png);
        f.Close();
    }

    /// <summary>設定視窗需要一個控制器實例；此處建立不顯示任何框線的最小版本。</summary>
    private sealed class PreviewHost
    {
        public AppController Controller { get; }
        public PreviewHost(AppConfig cfg) => Controller = AppController.ForPreview(cfg);
    }
}
