using System.Drawing.Drawing2D;

namespace DeskFrames;

/// <summary>
/// 直角多邊形（所有邊皆水平或垂直）運算。
/// 標準形式：無重複點、無共線點、順時針（螢幕座標 y 向下時面積為正）、第 0 點為最上最左頂點（因此第 0 條邊為水平）。
/// 在標準形式下，第 i 條邊為水平 ⇔ i 為偶數；插入凸角/缺角時維持此奇偶性。
/// </summary>
public static class RectiPoly
{
    public static Point[] FromRect(Rectangle r) =>
        new[] { new Point(r.Left, r.Top), new Point(r.Right, r.Top), new Point(r.Right, r.Bottom), new Point(r.Left, r.Bottom) };

    public static long Area2(IReadOnlyList<Point> p)
    {
        long a = 0;
        for (int i = 0, n = p.Count; i < n; i++)
        {
            var u = p[i]; var v = p[(i + 1) % n];
            a += (long)u.X * v.Y - (long)v.X * u.Y;
        }
        return a;
    }

    public static Rectangle Bounds(IReadOnlyList<Point> p)
    {
        if (p.Count == 0) return Rectangle.Empty;
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var q in p) { l = Math.Min(l, q.X); t = Math.Min(t, q.Y); r = Math.Max(r, q.X); b = Math.Max(b, q.Y); }
        return Rectangle.FromLTRB(l, t, r, b);
    }

    /// <summary>轉為標準形式；退化（面積 0、斜邊、點數不足）時回傳 null。</summary>
    public static Point[]? Normalize(IReadOnlyList<Point> src)
    {
        var list = new List<Point>(src);
        bool changed = true;
        while (changed && list.Count >= 3)
        {
            changed = false;
            for (int i = 0; i < list.Count && list.Count >= 3;)
            {
                int j = (i + 1) % list.Count;
                if (list[i] == list[j]) { list.RemoveAt(j); changed = true; }
                else i++;
            }
            for (int i = 0; i < list.Count && list.Count >= 3;)
            {
                int n = list.Count;
                var a = list[(i - 1 + n) % n]; var b = list[i]; var c = list[(i + 1) % n];
                if ((a.X == b.X && b.X == c.X) || (a.Y == b.Y && b.Y == c.Y)) { list.RemoveAt(i); changed = true; }
                else i++;
            }
        }
        if (list.Count < 4 || list.Count % 2 != 0) return null;
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i]; var b = list[(i + 1) % list.Count];
            if ((a.X == b.X) == (a.Y == b.Y)) return null; // 斜邊或零長
        }
        long area = Area2(list);
        if (area == 0) return null;
        if (area < 0) list.Reverse();
        int start = 0;
        for (int i = 1; i < list.Count; i++)
            if (list[i].Y < list[start].Y || (list[i].Y == list[start].Y && list[i].X < list[start].X)) start = i;
        var res = new Point[list.Count];
        for (int i = 0; i < res.Length; i++) res[i] = list[(start + i) % list.Count];
        return res;
    }

    /// <summary>簡單多邊形檢查：不相鄰的邊不得相交或接觸。</summary>
    public static bool IsSimple(Point[] p)
    {
        int n = p.Length;
        for (int i = 0; i < n; i++)
        {
            var a1 = p[i]; var a2 = p[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                var b1 = p[j]; var b2 = p[(j + 1) % n];
                if (Math.Max(Math.Min(a1.X, a2.X), Math.Min(b1.X, b2.X)) <= Math.Min(Math.Max(a1.X, a2.X), Math.Max(b1.X, b2.X)) &&
                    Math.Max(Math.Min(a1.Y, a2.Y), Math.Min(b1.Y, b2.Y)) <= Math.Min(Math.Max(a1.Y, a2.Y), Math.Max(b1.Y, b2.Y)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>標準化並驗證（簡單多邊形、每邊至少 minEdge）。不合法回傳 null。</summary>
    public static Point[]? Validate(IReadOnlyList<Point> pts, int minEdge)
    {
        var n = Normalize(pts);
        if (n == null || !IsSimple(n)) return null;
        for (int i = 0; i < n.Length; i++)
        {
            var a = n[i]; var b = n[(i + 1) % n.Length];
            if (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < minEdge) return null;
        }
        return n;
    }

    public static bool Contains(IReadOnlyList<Point> p, PointF q)
    {
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) &&
                q.X < (float)(p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                inside = !inside;
        }
        return inside;
    }

    public static float DistToSegment(PointF q, Point a, Point b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len2 = dx * dx + dy * dy;
        float t = len2 == 0 ? 0 : Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / len2, 0, 1);
        float px = a.X + t * dx - q.X, py = a.Y + t * dy - q.Y;
        return MathF.Sqrt(px * px + py * py);
    }

    /// <summary>
    /// 多邊形與矩形的布林運算（add=聯集，否則=差集）。
    /// 以座標壓縮網格重建外框；結果必須是單一、無破洞、無對角接觸的區域，否則回傳 null。
    /// </summary>
    public static Point[]? Boolean(Point[] poly, Rectangle r, bool add)
    {
        var xs = poly.Select(p => p.X).Append(r.Left).Append(r.Right).Distinct().OrderBy(v => v).ToArray();
        var ys = poly.Select(p => p.Y).Append(r.Top).Append(r.Bottom).Distinct().OrderBy(v => v).ToArray();
        int nx = xs.Length - 1, ny = ys.Length - 1;
        if (nx < 1 || ny < 1) return null;
        var cov = new bool[nx, ny];
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                var c = new PointF((xs[i] + xs[i + 1]) / 2f, (ys[j] + ys[j + 1]) / 2f);
                bool inP = Contains(poly, c);
                bool inR = c.X > r.Left && c.X < r.Right && c.Y > r.Top && c.Y < r.Bottom;
                cov[i, j] = add ? inP || inR : inP && !inR;
            }

        var next = new Dictionary<Point, Point>();
        bool AddEdge(Point a, Point b) => next.TryAdd(a, b);
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < ny; j++)
            {
                if (!cov[i, j]) continue;
                if ((j == 0 || !cov[i, j - 1]) && !AddEdge(new(xs[i], ys[j]), new(xs[i + 1], ys[j]))) return null;
                if ((i == nx - 1 || !cov[i + 1, j]) && !AddEdge(new(xs[i + 1], ys[j]), new(xs[i + 1], ys[j + 1]))) return null;
                if ((j == ny - 1 || !cov[i, j + 1]) && !AddEdge(new(xs[i + 1], ys[j + 1]), new(xs[i], ys[j + 1]))) return null;
                if ((i == 0 || !cov[i - 1, j]) && !AddEdge(new(xs[i], ys[j + 1]), new(xs[i], ys[j]))) return null;
            }
        if (next.Count == 0) return null;

        var start = next.Keys.First();
        var loop = new List<Point> { start };
        var cur = next[start];
        while (cur != start)
        {
            loop.Add(cur);
            if (loop.Count > next.Count || !next.TryGetValue(cur, out cur)) return null;
        }
        if (loop.Count != next.Count) return null; // 多個區塊或有破洞
        return Normalize(loop);
    }

    /// <summary>預設形狀範本（填滿矩形 r，數值對齊 grid）。</summary>
    public static Point[] Template(string kind, Rectangle r, int grid)
    {
        int G(double v) => grid > 1 ? (int)Math.Round(v / grid) * grid : (int)Math.Round(v);
        int L = r.Left, T = r.Top, R = r.Right, B = r.Bottom;
        int w = r.Width, h = r.Height;
        int x2 = L + G(w / 2.0), y2 = T + G(h / 2.0);
        int x3a = L + G(w / 3.0), x3b = L + G(w * 2 / 3.0), y3 = T + G(h / 3.0);
        Point[] pts = kind switch
        {
            "L" => new Point[] { new(L, T), new(x2, T), new(x2, y2), new(R, y2), new(R, B), new(L, B) },
            "J" => new Point[] { new(x2, T), new(R, T), new(R, B), new(L, B), new(L, y2), new(x2, y2) },
            "convex" => new Point[] { new(x3a, T), new(x3b, T), new(x3b, y3), new(R, y3), new(R, B), new(L, B), new(L, y3), new(x3a, y3) },
            "T" => new Point[] { new(L, T), new(R, T), new(R, y3), new(x3b, y3), new(x3b, B), new(x3a, B), new(x3a, y3), new(L, y3) },
            "U" => new Point[] { new(L, T), new(x3a, T), new(x3a, y2), new(x3b, y2), new(x3b, T), new(R, T), new(R, B), new(L, B) },
            _ => FromRect(r),
        };
        return Normalize(pts) ?? FromRect(r);
    }

    /// <summary>
    /// 把（標準形式的）直角多邊形往內縮 d 像素，用於繪製：相鄰框線共用同一條格線時，兩條線之間會留下縫隙而不重疊。
    /// 若內縮後會翻轉（邊太短），逐步減少內縮量；仍不行就不內縮。
    /// </summary>
    public static PointF[] Inset(Point[] p, float d)
    {
        int n = p.Length;
        for (int attempt = 0; attempt < 4 && d > 0.25f; attempt++, d /= 2)
        {
            var r = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                var prev = p[(i - 1 + n) % n]; var v = p[i]; var next = p[(i + 1) % n];
                // 每條邊往內（行進方向右側）平移 d：水平邊 y += d*sign(dx)，垂直邊 x -= d*sign(dy)
                float x = v.X, y = v.Y;
                if (prev.Y == v.Y) y = v.Y + d * Math.Sign(v.X - prev.X); else x = v.X - d * Math.Sign(v.Y - prev.Y);
                if (next.Y == v.Y) y = v.Y + d * Math.Sign(next.X - v.X); else x = v.X - d * Math.Sign(next.Y - v.Y);
                r[i] = new PointF(x, y);
            }
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                var a0 = p[i]; var b0 = p[(i + 1) % n]; var a1 = r[i]; var b1 = r[(i + 1) % n];
                ok = Math.Sign(b0.X - a0.X) == Math.Sign(b1.X - a1.X) && Math.Sign(b0.Y - a0.Y) == Math.Sign(b1.Y - a1.Y)
                     && Math.Abs(b1.X - a1.X) + Math.Abs(b1.Y - a1.Y) >= 1f;
            }
            if (ok) return r;
        }
        return p.Select(q => (PointF)q).ToArray();
    }

    public static GraphicsPath RoundedPath(Point[] p, float radius) => RoundedPath(p.Select(q => (PointF)q).ToArray(), radius);

    /// <summary>建立圓角外框路徑（凸角、凹角皆以貝茲曲線圓滑處理）。</summary>
    public static GraphicsPath RoundedPath(PointF[] p, float radius)
    {
        const float k = 0.5523f;
        int n = p.Length;
        if (n < 3) return new GraphicsPath();
        var pts = new List<PointF>(n * 4);
        var types = new List<byte>(n * 4);
        for (int i = 0; i < n; i++)
        {
            PointF v = p[i], a = p[(i - 1 + n) % n], b = p[(i + 1) % n];
            float la = Dist(v, a), lb = Dist(v, b);
            float r = Math.Min(radius, Math.Min(la, lb) / 2f);
            var pin = new PointF(v.X + (a.X - v.X) / la * r, v.Y + (a.Y - v.Y) / la * r);
            var pout = new PointF(v.X + (b.X - v.X) / lb * r, v.Y + (b.Y - v.Y) / lb * r);
            pts.Add(pin); types.Add((byte)(i == 0 ? PathPointType.Start : PathPointType.Line));
            if (r > 0.3f)
            {
                pts.Add(new PointF(pin.X + (v.X - pin.X) * k, pin.Y + (v.Y - pin.Y) * k)); types.Add((byte)PathPointType.Bezier);
                pts.Add(new PointF(pout.X + (v.X - pout.X) * k, pout.Y + (v.Y - pout.Y) * k)); types.Add((byte)PathPointType.Bezier);
                pts.Add(pout); types.Add((byte)PathPointType.Bezier);
            }
        }
        types[^1] |= (byte)PathPointType.CloseSubpath;
        return new GraphicsPath(pts.ToArray(), types.ToArray(), FillMode.Alternate);
    }

    private static float Dist(PointF a, PointF b) => MathF.Max(1e-3f, MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)));
}
