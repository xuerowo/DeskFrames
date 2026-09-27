using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace DeskFrames;

public enum PartKind { None, Body, Title, Edge, Vertex }

public readonly record struct HitPart(PartKind Kind, int Index = -1)
{
    public static readonly HitPart None = new(PartKind.None);
}

public struct RenderState
{
    public bool Edit, Selected, ShowHandles;
    public HitPart Hover;
}

/// <summary>框線的幾何配置：多邊形、標題標籤位置、視窗範圍（皆為螢幕實體像素）。</summary>
public sealed class FrameLayout
{
    public Point[] Poly = Array.Empty<Point>();
    /// <summary>實際繪製用的外框：往內縮一點，讓共用同一條格線的相鄰框線之間留有縫隙。</summary>
    public PointF[] DrawPoly = Array.Empty<PointF>();
    public Rectangle Bounds;
    public float Scale = 1f;
    public RectangleF TitleRect;
    public bool HasTitle;
    /// <summary>框線貼齊螢幕上緣時，標題改畫在框線內側（避免跑出螢幕）。</summary>
    public bool TitleFlipped;
    public Font TitleFont = null!;
    public Rectangle Window;

    private static readonly Graphics Measurer = CreateMeasurer();
    private static readonly Dictionary<int, Font> FontCache = new();

    private static Graphics CreateMeasurer()
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    public static Font TitleFontFor(float pt, float scale)
    {
        int px10 = (int)Math.Round(pt * scale * 96f / 72f * 10);
        if (!FontCache.TryGetValue(px10, out var f))
        {
            try { f = new Font(Theme.UiFontName, px10 / 10f, FontStyle.Bold, GraphicsUnit.Pixel); }
            catch { f = new Font(FontFamily.GenericSansSerif, px10 / 10f, FontStyle.Bold, GraphicsUnit.Pixel); }
            FontCache[px10] = f;
        }
        return f;
    }

    public static FrameLayout Compute(FrameModel f)
    {
        var L = new FrameLayout { Poly = f.Points };
        L.Bounds = RectiPoly.Bounds(L.Poly);
        var center = new Point(L.Bounds.X + L.Bounds.Width / 2, L.Bounds.Y + L.Bounds.Height / 2);
        float s = L.Scale = Native.ScaleAt(center);
        var st = f.Style;
        float lw = Math.Max(1f, st.LineWidth * s);
        L.TitleFont = TitleFontFor(st.TitleSize, s);
        if (L.Poly.Length >= 4) L.DrawPoly = RectiPoly.Inset(L.Poly, lw / 2f + 1.5f * s);

        // 標題錨點：第 0 條邊（標準形式下為最上最左的上緣）
        if (L.Poly.Length >= 4)
        {
            var a = L.DrawPoly[0]; var b = L.DrawPoly[1];
            // 精簡標籤：高度剛好放進上下兩排圖示之間的縫隙
            float padX = 8 * s, padY = 2.5f * s;
            var text = string.IsNullOrWhiteSpace(f.Title) ? "" : f.Title.Trim();
            L.HasTitle = text.Length > 0;
            var sz = Measurer.MeasureString(text.Length > 0 ? text : "標題", L.TitleFont, PointF.Empty, StringFormat.GenericTypographic);
            float th = L.TitleFont.Size * 1.22f;
            float tabH = MathF.Ceiling(th + padY * 2);
            float edgeLen = b.X - a.X;
            float inset = Math.Min(st.CornerRadius * s, edgeLen / 2f) + 2 * s;
            float avail = Math.Max(edgeLen - inset * 2, 48 * s);
            float tabW = MathF.Ceiling(Math.Min(sz.Width + padX * 2 + 1, avail));
            if (st.TitleStyle == "text") tabW = MathF.Ceiling(Math.Min(sz.Width + 4 * s, avail));
            float x = st.TitleAlign switch
            {
                "center" => a.X + (edgeLen - tabW) / 2f,
                "right" => b.X - inset - tabW,
                _ => a.X + inset,
            };
            float y = st.TitleStyle switch
            {
                "pill" => a.Y - tabH + lw / 2f + 1 * s, // 坐在線上：放進兩排圖示之間的縫隙，不壓到下方圖示
                "text" => a.Y - tabH - lw / 2f + padY,
                _ => a.Y - tabH + lw / 2f,
            };
            var wa = Screen.FromPoint(new Point((int)a.X + 1, (int)a.Y + 1)).WorkingArea;
            if (y < wa.Top)
            {
                L.TitleFlipped = true;
                y = st.TitleStyle == "text" ? a.Y + lw / 2f + 2 * s : a.Y - lw / 2f;
                if (st.TitleStyle == "pill") y = a.Y + lw / 2f + 3 * s;
            }
            L.TitleRect = new RectangleF(MathF.Round(x), MathF.Round(y), tabW, tabH);
        }

        var box = L.Bounds;
        if (L.HasTitle) box = Rectangle.Union(box, Rectangle.Ceiling(L.TitleRect));
        int margin = (int)Math.Ceiling(lw / 2 + (st.Glow ? 8 : 4) * s + 10 * s);
        box.Inflate(margin, margin);
        L.Window = box;
        return L;
    }

    /// <summary>編輯模式的命中測試（螢幕座標）。</summary>
    public HitPart HitTest(Point pt)
    {
        float tolV = 9 * Scale, tolE = 7 * Scale;
        if (HasTitle && TitleRect.Contains(pt)) return new(PartKind.Title);
        int n = Poly.Length;
        for (int i = 0; i < n; i++)
        {
            float dx = pt.X - Poly[i].X, dy = pt.Y - Poly[i].Y;
            if (dx * dx + dy * dy <= tolV * tolV) return new(PartKind.Vertex, i);
        }
        int best = -1; float bestD = tolE;
        for (int i = 0; i < n; i++)
        {
            float d = RectiPoly.DistToSegment(pt, Poly[i], Poly[(i + 1) % n]);
            if (d <= bestD) { bestD = d; best = i; }
        }
        if (best >= 0) return new(PartKind.Edge, best);
        if (RectiPoly.Contains(Poly, pt)) return new(PartKind.Body);
        return HitPart.None;
    }
}

/// <summary>框線繪製：底色、陰影、柔光、主線、標題標籤、編輯控制點。</summary>
public static class FrameRenderer
{
    /// <summary>可被滑鼠命中、但肉眼看不見的最低不透明度。</summary>
    private const int HitAlpha = 3;

    public static void Draw(Graphics g, FrameModel f, FrameLayout L, RenderState rs)
    {
        if (L.Poly.Length < 4) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.CompositingQuality = CompositingQuality.HighQuality;

        var st = f.Style;
        float s = L.Scale;
        var color = st.ColorValue;
        int lineA = Math.Clamp(st.LineOpacity, 10, 100) * 255 / 100;
        float lw = Math.Max(1f, st.LineWidth * s);
        using var path = RectiPoly.RoundedPath(L.DrawPoly, st.CornerRadius * s);

        // 底色：完全依使用者設定。編輯模式下保留極低的不透明度（肉眼不可見），
        // 讓框內空白處仍可點選拖曳（alpha 為 0 的像素會點穿到桌面）。
        // 注意：GDI+ 高品質合成會把 alpha 1 捨入成 0，實測 alpha 3 → 像素 alpha 2 才可靠。
        int fillA = st.FillOpacity * 255 / 100;
        if (rs.Edit) fillA = Math.Max(fillA, HitAlpha);
        if (fillA > 0)
        {
            using var b = new SolidBrush(Color.FromArgb(Math.Min(255, fillA), color));
            g.FillPath(b, path);
        }

        // 編輯模式：幾乎透明的寬邊，讓框線外側也能被滑鼠抓到
        if (rs.Edit)
        {
            using var hit = new Pen(Color.FromArgb(HitAlpha, 0, 0, 0), 16 * s) { LineJoin = LineJoin.Round };
            g.DrawPath(hit, path);
        }

        // 深色描邊：確保在淺色桌布上也清楚
        using (var sh = new Pen(Color.FromArgb(lineA * 38 / 255, 0, 0, 0), lw + 2f * s) { LineJoin = LineJoin.Round })
            g.DrawPath(sh, path);

        if (st.Glow)
        {
            (float w, int a)[] layers = { (lw + 11 * s, 14), (lw + 7 * s, 24), (lw + 3.5f * s, 44) };
            foreach (var (w, a) in layers)
            {
                using var gp = new Pen(Color.FromArgb(a * lineA / 255, color), w) { LineJoin = LineJoin.Round };
                g.DrawPath(gp, path);
            }
        }

        if (rs.Selected)
        {
            using var sel = new Pen(Color.FromArgb(110, color), lw + 6 * s) { LineJoin = LineJoin.Round };
            g.DrawPath(sel, path);
        }

        using (var pen = new Pen(Color.FromArgb(lineA, color), lw) { LineJoin = LineJoin.Round, DashCap = DashCap.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            if (st.LineStyle == "dash") pen.DashPattern = new[] { 3.2f, 2.4f };
            else if (st.LineStyle == "dot") pen.DashPattern = new[] { 0.01f, 2.2f };
            g.DrawPath(pen, path);
        }

        // 滑鼠懸停的邊：高亮
        if (rs.Edit && rs.Hover.Kind == PartKind.Edge && rs.Hover.Index < L.Poly.Length)
        {
            var a = L.DrawPoly[rs.Hover.Index]; var b = L.DrawPoly[(rs.Hover.Index + 1) % L.DrawPoly.Length];
            using var hp = new Pen(Color.FromArgb(235, Color.White), lw + 2 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var hp2 = new Pen(Color.FromArgb(255, color), lw + 5 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(hp2, a, b);
            g.DrawLine(hp, a, b);
        }

        DrawTitle(g, f, L, rs, color, lineA, lw);

        if (rs.Edit && rs.ShowHandles) DrawHandles(g, L, rs, color);
    }

    private static void DrawTitle(Graphics g, FrameModel f, FrameLayout L, RenderState rs, Color color, int lineA, float lw)
    {
        if (!L.HasTitle) return;
        var st = f.Style;
        float s = L.Scale;
        var r = L.TitleRect;
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = st.TitleStyle == "text" ? StringAlignment.Near : StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        var text = f.Title.Trim();
        bool hot = rs.Edit && rs.Hover.Kind == PartKind.Title;

        if (st.TitleStyle == "text")
        {
            var tr = new RectangleF(r.X, r.Y, r.Width + 2 * s, r.Height);
            using (var shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            {
                var o = Math.Max(1f, s);
                g.DrawString(text, L.TitleFont, shadow, new RectangleF(tr.X + o, tr.Y + o, tr.Width, tr.Height), sf);
            }
            using var tb = new SolidBrush(Color.FromArgb(255, hot ? Theme.Blend(color, Color.White, .3f) : color));
            g.DrawString(text, L.TitleFont, tb, tr, sf);
            return;
        }

        using var path = st.TitleStyle == "pill" ? Theme.RoundRect(r, r.Height / 2f) : Theme.TabPath(r, 7 * s);
        if (L.TitleFlipped && st.TitleStyle == "tab")
            using (var flip = new Matrix(1, 0, 0, -1, 0, r.Top + r.Bottom)) path.Transform(flip); // 上下翻轉成向下的標籤
        using (var shadow = new Pen(Color.FromArgb(60, 0, 0, 0), 2.5f * s)) g.DrawPath(shadow, path);
        var bg = hot ? Theme.Blend(color, Color.White, .18f) : color;
        using (var b = new SolidBrush(Color.FromArgb(Math.Max(lineA, 215), bg))) g.FillPath(b, path);
        var textRect = RectangleF.Inflate(r, -8 * s, 0);
        if (st.TitleStyle == "tab") textRect.Offset(0, L.TitleFlipped ? lw / 4f : -lw / 4f);
        using var fg = new SolidBrush(Theme.ContrastText(bg));
        g.DrawString(text, L.TitleFont, fg, textRect, sf);
    }

    private static void DrawHandles(Graphics g, FrameLayout L, RenderState rs, Color color)
    {
        float s = L.Scale;
        var stroke = color.GetBrightness() > 0.85f ? Color.FromArgb(90, 90, 100) : color;
        using var fill = new SolidBrush(Color.White);
        using var pen = new Pen(stroke, 1.6f * s);
        using var hotFill = new SolidBrush(stroke);
        int n = L.DrawPoly.Length;
        // 邊的中點控制條
        for (int i = 0; i < n; i++)
        {
            var a = L.DrawPoly[i]; var b = L.DrawPoly[(i + 1) % n];
            float len = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
            if (len < 44 * s) continue;
            float cx = (a.X + b.X) / 2f, cy = (a.Y + b.Y) / 2f;
            bool horiz = Math.Abs(a.Y - b.Y) < 0.01f;
            float hw = (horiz ? 16 : 5) * s, hh = (horiz ? 5 : 16) * s;
            var rr = new RectangleF(cx - hw / 2, cy - hh / 2, hw, hh);
            using var p = Theme.RoundRect(rr, 2.5f * s);
            bool hot = rs.Hover.Kind == PartKind.Edge && rs.Hover.Index == i;
            g.FillPath(hot ? hotFill : fill, p);
            g.DrawPath(pen, p);
        }
        // 頂點圓形控制點
        for (int i = 0; i < n; i++)
        {
            float rad = 4.8f * s;
            var v = L.DrawPoly[i];
            bool hot = rs.Hover.Kind == PartKind.Vertex && rs.Hover.Index == i;
            if (hot) rad += 1.5f * s;
            var rr = new RectangleF(v.X - rad, v.Y - rad, rad * 2, rad * 2);
            g.FillEllipse(hot ? hotFill : fill, rr);
            g.DrawEllipse(pen, rr);
        }
    }
}
