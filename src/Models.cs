using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskFrames;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool FirstRunDone { get; set; }
    public bool AutoStart { get; set; } = true;
    public bool FramesVisible { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    /// <summary>吸附到桌面圖示格線（圖示之間的縫隙）；讀不到格線時改用 GridSize。</summary>
    public bool SnapToIconGrid { get; set; } = true;
    public int GridSize { get; set; } = 10;
    /// <summary>system / dark / light</summary>
    public string Theme { get; set; } = "system";
    public FrameStyle DefaultStyle { get; set; } = new();
    public List<FrameModel> Frames { get; set; } = new();
    public TodoConfig Todo { get; set; } = new();
    public int[]? ToolbarPos { get; set; }
    /// <summary>全域快捷鍵：進入／離開編輯模式。</summary>
    public string HotkeyEdit { get; set; } = "ctrl+alt+f";
    /// <summary>全域快捷鍵：顯示／隱藏待辦清單（避開 OverlayTranslator 的 Ctrl+Alt+T）。</summary>
    public string HotkeyTodo { get; set; } = "ctrl+alt+d";
}

public sealed class FrameStyle
{
    public string Color { get; set; } = "#5B8CFF";
    /// <summary>框線粗細（DIP）。</summary>
    public float LineWidth { get; set; } = 2f;
    /// <summary>圓角半徑（DIP）。</summary>
    public float CornerRadius { get; set; } = 10f;
    /// <summary>solid / dash / dot</summary>
    public string LineStyle { get; set; } = "solid";
    /// <summary>框線不透明度 %。</summary>
    public int LineOpacity { get; set; } = 100;
    /// <summary>底色不透明度 %。</summary>
    public int FillOpacity { get; set; } = 6;
    public bool Glow { get; set; }
    /// <summary>tab / pill / text</summary>
    public string TitleStyle { get; set; } = "tab";
    /// <summary>left / center / right</summary>
    public string TitleAlign { get; set; } = "left";
    /// <summary>標題字級（pt）。</summary>
    public float TitleSize { get; set; } = 9.5f;

    public FrameStyle Clone() => (FrameStyle)MemberwiseClone();

    public void CopyFrom(FrameStyle o)
    {
        Color = o.Color; LineWidth = o.LineWidth; CornerRadius = o.CornerRadius; LineStyle = o.LineStyle;
        LineOpacity = o.LineOpacity; FillOpacity = o.FillOpacity; Glow = o.Glow;
        TitleStyle = o.TitleStyle; TitleAlign = o.TitleAlign; TitleSize = o.TitleSize;
    }

    [JsonIgnore]
    public Color ColorValue
    {
        get
        {
            try { return ColorTranslator.FromHtml(Color); }
            catch { return System.Drawing.Color.FromArgb(91, 140, 255); }
        }
        set => Color = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }
}

public sealed class FrameModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "新分類";
    /// <summary>直角多邊形頂點（實體螢幕像素，展平為 x0,y0,x1,y1...）。第 0 條邊固定為水平邊，順時針。</summary>
    public int[] Pts { get; set; } = Array.Empty<int>();
    public FrameStyle Style { get; set; } = new();

    [JsonIgnore]
    public Point[] Points
    {
        get
        {
            var r = new Point[Pts.Length / 2];
            for (int i = 0; i < r.Length; i++) r[i] = new Point(Pts[i * 2], Pts[i * 2 + 1]);
            return r;
        }
        set
        {
            var a = new int[value.Length * 2];
            for (int i = 0; i < value.Length; i++) { a[i * 2] = value[i].X; a[i * 2 + 1] = value[i].Y; }
            Pts = a;
        }
    }

    public FrameModel Clone() => new() { Id = Id, Title = Title, Pts = (int[])Pts.Clone(), Style = Style.Clone() };
}

public sealed class TodoConfig
{
    public bool Visible { get; set; } = true;
    /// <summary>位置與大小（實體像素）。X = int.MinValue 表示尚未放置。</summary>
    public int X { get; set; } = int.MinValue;
    public int Y { get; set; } = int.MinValue;
    public int W { get; set; }
    public int H { get; set; }
    public int Opacity { get; set; } = 100;
    public bool HideDone { get; set; }
    public bool Locked { get; set; }
    /// <summary>常駐顯示「新增」輸入框；false 時平常隱藏，按 ＋ 才出現。</summary>
    public bool ShowInput { get; set; }
    public string Title { get; set; } = "待辦事項";
    // ---- 清單樣式 ----
    /// <summary>app（跟隨介面）/ dark / light</summary>
    public string StyleTheme { get; set; } = "app";
    /// <summary>自訂背景色（null = 使用主題預設）。</summary>
    public string? Background { get; set; }
    /// <summary>自訂強調色（null = 使用主題預設）。</summary>
    public string? Accent { get; set; }
    /// <summary>事項文字大小（pt）。</summary>
    public float FontSize { get; set; } = 10f;
    /// <summary>compact / normal / loose</summary>
    public string Density { get; set; } = "normal";
    /// <summary>round / small / none</summary>
    public string Corner { get; set; } = "round";
    public List<TodoItem> Items { get; set; } = new();
}

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime? DoneAt { get; set; }
    /// <summary>到期時間（null = 未設定）。</summary>
    public DateTime? Due { get; set; }
    /// <summary>此到期時間是否已提醒過（修改時間時重設）。</summary>
    public bool Notified { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(List<FrameModel>))]
internal partial class JsonCtx : JsonSerializerContext { }

/// <summary>設定檔讀寫：%APPDATA%\DeskFrames\config.json，延遲寫入 + 原子取代。</summary>
public static class ConfigStore
{
    /// <summary>資料夾；可用環境變數 DESKFRAMES_DATA 指定（測試用）。</summary>
    public static string Dir { get; } = Environment.GetEnvironmentVariable("DESKFRAMES_DATA") is { Length: > 0 } d
        ? d : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFrames");
    public static string FilePath => Path.Combine(Dir, "config.json");

    private static System.Windows.Forms.Timer? _timer;
    private static AppConfig? _pending;

    /// <summary>讀取失敗且無法保留備份時為 true：此時停止寫入，避免覆蓋使用者原本的設定。</summary>
    public static bool SaveBlocked { get; private set; }

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath)) return new AppConfig();

        // 開機時檔案可能暫時被防毒或同步軟體鎖住：重試幾次
        string? json = null;
        for (int i = 0; i < 6 && json == null; i++)
        {
            try { json = File.ReadAllText(FilePath); }
            catch (IOException) { Thread.Sleep(500); }
            catch { break; }
        }
        if (json == null)
        {
            SaveBlocked = true;
            return new AppConfig { FirstRunDone = true, AutoStart = AutoStart.IsEnabled() };
        }

        try
        {
            var cfg = JsonSerializer.Deserialize(json, JsonCtx.Default.AppConfig);
            if (cfg != null) return Sanitize(cfg);
        }
        catch { }

        // 設定檔損毀：先保留備份；備份失敗就不再寫入
        try { File.Copy(FilePath, FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); }
        catch { SaveBlocked = true; }
        return new AppConfig { FirstRunDone = true, AutoStart = AutoStart.IsEnabled() };
    }

    /// <summary>修正手動編輯或舊版造成的不合法值，避免啟動時崩潰。</summary>
    private static AppConfig Sanitize(AppConfig c)
    {
        c.DefaultStyle ??= new FrameStyle();
        ClampStyle(c.DefaultStyle);
        c.Theme ??= "system";
        if (string.IsNullOrWhiteSpace(c.HotkeyEdit)) c.HotkeyEdit = "ctrl+alt+f";
        if (string.IsNullOrWhiteSpace(c.HotkeyTodo)) c.HotkeyTodo = "ctrl+alt+d";
        c.Frames ??= new();
        c.Frames.RemoveAll(f => f == null || f.Pts == null || f.Pts.Length < 8 || f.Pts.Length % 2 != 0);
        var ids = new HashSet<string>();
        foreach (var f in c.Frames.ToList())
        {
            if (string.IsNullOrEmpty(f.Id) || !ids.Add(f.Id)) { f.Id = Guid.NewGuid().ToString("N"); ids.Add(f.Id); }
            f.Title ??= "";
            f.Style ??= c.DefaultStyle.Clone();
            ClampStyle(f.Style);
            var pts = f.Points;
            var valid = RectiPoly.Validate(pts, 1);
            if (valid == null)
            {
                var b = RectiPoly.Bounds(pts);
                if (b.Width >= 8 && b.Height >= 8) valid = RectiPoly.FromRect(b);
            }
            if (valid == null) c.Frames.Remove(f); else f.Points = valid;
        }
        c.Todo ??= new TodoConfig();
        c.Todo.Title ??= "待辦事項";
        c.Todo.Items ??= new();
        c.Todo.Items.RemoveAll(i => i == null);
        foreach (var i in c.Todo.Items) { i.Text ??= ""; if (string.IsNullOrEmpty(i.Id)) i.Id = Guid.NewGuid().ToString("N"); }
        c.Todo.Opacity = Math.Clamp(c.Todo.Opacity, 30, 100);
        c.Todo.StyleTheme ??= "app";
        c.Todo.Density ??= "normal";
        c.Todo.Corner ??= "round";
        c.Todo.FontSize = Clamp(c.Todo.FontSize, 8, 16, 10);
        c.GridSize = Math.Clamp(c.GridSize, 2, 64);
        return c;
    }

    private static float Clamp(float v, float lo, float hi, float def) => float.IsFinite(v) ? Math.Clamp(v, lo, hi) : def;

    private static void ClampStyle(FrameStyle s)
    {
        s.Color ??= "#5B8CFF";
        s.LineStyle ??= "solid";
        s.TitleStyle ??= "tab";
        s.TitleAlign ??= "left";
        s.LineWidth = Clamp(s.LineWidth, 0.5f, 20, 2);
        s.CornerRadius = Clamp(s.CornerRadius, 0, 100, 10);
        s.TitleSize = Clamp(s.TitleSize, 6, 48, 10.5f);
        s.LineOpacity = Math.Clamp(s.LineOpacity, 10, 100);
        s.FillOpacity = Math.Clamp(s.FillOpacity, 0, 100);
    }

    /// <summary>延遲 600ms 寫入（合併連續修改）。</summary>
    public static void SaveSoon(AppConfig cfg)
    {
        _pending = cfg;
        if (_timer == null)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 600 };
            _timer.Tick += (_, _) => { _timer!.Stop(); Flush(); };
        }
        _timer.Stop();
        _timer.Start();
    }

    public static void Flush()
    {
        if (_pending != null) { SaveNow(_pending); _pending = null; }
    }

    public static void SaveNow(AppConfig cfg)
    {
        if (SaveBlocked) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonCtx.Default.AppConfig));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
            else File.Move(tmp, FilePath);
        }
        catch { /* 寫入失敗時保留舊檔 */ }
    }

    public static string SerializeFrames(List<FrameModel> frames) =>
        JsonSerializer.Serialize(frames, JsonCtx.Default.ListFrameModel);

    public static List<FrameModel> DeserializeFrames(string json) =>
        JsonSerializer.Deserialize(json, JsonCtx.Default.ListFrameModel) ?? new();
}
