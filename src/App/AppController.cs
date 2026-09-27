namespace DeskFrames;

/// <summary>
/// 應用程式主控：系統匣、快捷鍵、框線視窗、待辦清單、Explorer 重啟復原。
/// 編輯相關邏輯見 AppController.Edit.cs。
/// </summary>
public sealed partial class AppController : ApplicationContext
{
    private const int HotkeyEdit = 1, HotkeyTodo = 2;

    private readonly AppConfig _cfg;
    private List<FrameWindow> _windows = new();
    private readonly NotifyIcon _tray;
    private readonly HostWindow _host;
    private readonly Control _invoker = new();
    private readonly RegisteredWaitHandle _showWait, _editWait;
    private TodoWidget? _todo;
    private SettingsForm? _settings;
    private System.Windows.Forms.Timer? _trimTimer;
    private bool _initialized;

    public AppConfig Config => _cfg;

    /// <summary>僅供離屏預覽繪製使用（不建立系統匣、快捷鍵、視窗）。</summary>
    internal static AppController ForPreview(AppConfig cfg) => new(cfg);

    private AppController(AppConfig cfg)
    {
        _cfg = cfg;
        _host = null!; _tray = null!; _showWait = _editWait = null!;
    }

    public AppController(bool startup, EventWaitHandle showEvent, EventWaitHandle editEvent)
    {
        _cfg = ConfigStore.Load();
        Theme.Apply(_cfg.Theme);
        _invoker.CreateControl();

        _host = new HostWindow(this);
        _tray = CreateTray();
        RegisterHotkeys();

        _showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => _invoker.BeginInvoke(ShowSettings), null, Timeout.Infinite, false);
        _editWait = ThreadPool.RegisterWaitForSingleObject(editEvent,
            (_, _) => _invoker.BeginInvoke(ToggleEdit), null, Timeout.Infinite, false);

        bool firstRun = !_cfg.FirstRunDone;
        if (firstRun)
        {
            _cfg.FirstRunDone = true;
            _cfg.Todo.Items.AddRange(new[]
            {
                new TodoItem { Text = $"按 {Hotkey.Display(_cfg.HotkeyEdit)}（或雙擊系統匣圖示）開始繪製桌面框線" },
                new TodoItem { Text = "把同類的檔案圖示拖進框線內" },
                new TodoItem { Text = "在系統匣圖示按右鍵可調整設定與樣式" },
            });
            Save();
        }
        AutoStart.Sync(_cfg.AutoStart);

        // 開機時 Explorer 可能尚未就緒：等到桌面視窗出現再建立框線
        WaitForDesktop(startup ? 90 : 5, () =>
        {
            InitWindows();
            if (firstRun)
            {
                _tray.ShowBalloonTip(6000, "DeskFrames 已啟動",
                    $"按 {Hotkey.Display(_cfg.HotkeyEdit)} 或雙擊系統匣圖示，即可在桌面上繪製分類框線。", ToolTipIcon.Info);
                EnterEdit();
            }
        });
    }

    private void WaitForDesktop(int seconds, Action then)
    {
        DesktopHost.Refresh();
        if (DesktopHost.Ready) { then(); return; }
        int tries = 0;
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) =>
        {
            DesktopHost.Refresh();
            if (DesktopHost.Ready || ++tries >= seconds)
            {
                t.Stop(); t.Dispose();
                then();
            }
        };
        t.Start();
    }

    private void InitWindows()
    {
        _initialized = true;
        ScheduleReminders();
        if (_cfg.FramesVisible)
            foreach (var f in _cfg.Frames) _windows.Add(new FrameWindow(f, this));
        ArrangeZOrder();
        if (_cfg.Todo.Visible) ShowTodo();
        ScheduleTrim();
    }

    public void Save() => ConfigStore.SaveSoon(_cfg);

    /// <summary>閒置一段時間後釋放記憶體。</summary>
    public void ScheduleTrim()
    {
        _trimTimer ??= new System.Windows.Forms.Timer { Interval = 4000 };
        _trimTimer.Tick -= TrimTick;
        _trimTimer.Tick += TrimTick;
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private void TrimTick(object? s, EventArgs e)
    {
        _trimTimer!.Stop();
        if (!_edit) Native.TrimWorkingSet();
    }

    // ================= 系統匣 =================

    private NotifyIcon CreateTray()
    {
        var menu = ModernMenuRenderer.Create();
        var edit = menu.Item("編輯框線", ToggleEdit, Hotkey.Display(_cfg.HotkeyEdit));
        edit.Font = Theme.Ui(9.5f, FontStyle.Bold);
        menu.Item("繪製新框線", () => { EnterEdit(); StartDraw(DrawMode.New); });
        menu.Sep();
        var showFrames = menu.Item("顯示框線", () => SetFramesVisible(!_cfg.FramesVisible));
        var showTodo = menu.Item("待辦清單", ToggleTodo, Hotkey.Display(_cfg.HotkeyTodo));
        menu.Sep();
        menu.Item("設定…", ShowSettings);
        var auto = menu.Item("開機時自動啟動", () => SetAutoStart(!_cfg.AutoStart));
        menu.Sep();
        menu.Item("結束 DeskFrames", ExitApp);
        menu.Opening += (_, _) =>
        {
            edit.Checked = _edit;
            showFrames.Checked = _cfg.FramesVisible;
            showTodo.Checked = _cfg.Todo.Visible;
            auto.Checked = _cfg.AutoStart;
        };

        var tray = new NotifyIcon
        {
            Text = "DeskFrames 桌面分區框線",
            Icon = LoadAppIcon(SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleEdit(); };
        tray.BalloonTipClicked += (_, _) => { if (_reminderShown) { _reminderShown = false; ShowTodo(); } };
        return tray;
    }

    public static Icon LoadAppIcon(Size size)
    {
        using var s = typeof(AppController).Assembly.GetManifestResourceStream("app.ico");
        return s != null ? new Icon(s, size) : SystemIcons.Application;
    }

    // ================= 指令 =================

    public void SetFramesVisible(bool visible)
    {
        _cfg.FramesVisible = visible;
        if (!visible && _edit) ExitEdit();
        if (visible && _windows.Count == 0 && _initialized) SyncWindows();
        foreach (var w in _windows) w.Visible = visible;
        if (visible) ArrangeZOrder();
        Save();
        _settings?.RefreshValues();
    }

    public void SetAutoStart(bool on)
    {
        _cfg.AutoStart = on;
        AutoStart.Set(on);
        Save();
        _settings?.RefreshValues();
    }

    public void SetTheme(string mode)
    {
        _cfg.Theme = mode;
        Theme.Apply(mode);
        Save();
    }

    public void ToggleTodo()
    {
        if (_cfg.Todo.Visible && _todo != null && _todo.Visible) HideTodo();
        else ShowTodo();
    }

    public void ShowTodo()
    {
        _cfg.Todo.Visible = true;
        if (_todo == null || _todo.IsDisposed)
        {
            _todo = new TodoWidget(_cfg.Todo, OnTodoChanged);
            _todo.HideRequested += HideTodo;
            _todo.StyleRequested += ShowTodoStyle;
            // 待辦清單顯示後會自行釘選到最底層：之後再排一次，讓它回到框線之上
            _todo.Shown += (_, _) => _invoker.BeginInvoke(ArrangeZOrder);
        }
        _todo.Show();
        _todo.RePin();
        ArrangeZOrder(); // 框線排到待辦清單之下
        Save();
        _settings?.RefreshValues();
    }

    public void HideTodo()
    {
        _cfg.Todo.Visible = false;
        _todo?.Hide();
        Save();
        _settings?.RefreshValues();
        ScheduleTrim();
    }

    private TodoStyleForm? _todoStyle;

    /// <summary>開啟待辦清單樣式視窗（變更即時套用並儲存）。</summary>
    public void ShowTodoStyle()
    {
        if (_todoStyle == null || _todoStyle.IsDisposed)
        {
            _todoStyle = new TodoStyleForm(_cfg.Todo, () =>
            {
                TodoTheme.Configure(_cfg.Todo);
                _todo?.ApplyStyle();
                Save();
            });
            _todoStyle.FormClosed += (_, _) => { _todoStyle = null; ScheduleTrim(); };
        }
        _todoStyle.Show();
        _todoStyle.Activate();
    }

    public void ShowSettings()
    {
        if (_settings == null || _settings.IsDisposed)
        {
            _settings = new SettingsForm(this);
            _settings.FormClosed += (_, _) => { _settings = null; ScheduleTrim(); };
        }
        _settings.Show();
        _settings.WindowState = FormWindowState.Normal;
        _settings.Activate();
    }

    public void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.Dir);
            System.Diagnostics.Process.Start("explorer.exe", ConfigStore.Dir);
        }
        catch { }
    }

    public void ExitApp()
    {
        if (_edit) ExitEdit();
        ConfigStore.Flush();
        ConfigStore.SaveNow(_cfg);
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _showWait.Unregister(null);
        _editWait.Unregister(null);
        Native.UnregisterHotKey(_host.Handle, HotkeyEdit);
        Native.UnregisterHotKey(_host.Handle, HotkeyTodo);
        foreach (var w in _windows) w.Dispose();
        _windows.Clear();
        _todo?.Dispose();
        _settings?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _host.DestroyHandle();
        base.ExitThreadCore();
    }

    // ================= 待辦提醒 =================

    private System.Windows.Forms.Timer? _reminderTimer;
    private bool _reminderShown;

    private void OnTodoChanged()
    {
        Save();
        ScheduleReminders();
    }

    /// <summary>
    /// 只設一個計時器，等到下一個到期時間（最長 30 分鐘重新校正一次，以應對睡眠或調整系統時間）。
    /// 沒有未提醒的到期事項時，完全不執行任何計時器。
    /// </summary>
    private void ScheduleReminders()
    {
        _reminderTimer?.Stop();
        var next = _cfg.Todo.Items.Where(i => !i.Done && !i.Notified && i.Due != null).Select(i => i.Due!.Value).DefaultIfEmpty().Min();
        if (next == default) return;
        double ms = Math.Clamp((next - DateTime.Now).TotalMilliseconds, 800, 30 * 60 * 1000);
        if (_reminderTimer == null)
        {
            _reminderTimer = new System.Windows.Forms.Timer();
            _reminderTimer.Tick += (_, _) => ReminderTick();
        }
        _reminderTimer.Interval = (int)ms;
        _reminderTimer.Start();
    }

    private void ReminderTick()
    {
        _reminderTimer!.Stop();
        var now = DateTime.Now;
        var due = _cfg.Todo.Items.Where(i => !i.Done && !i.Notified && i.Due is DateTime d && d <= now.AddSeconds(1)).ToList();
        if (due.Count > 0)
        {
            foreach (var i in due) i.Notified = true;
            string title, text;
            if (due.Count == 1)
            {
                title = "待辦提醒";
                text = $"{due[0].Text}\n{TodoDue.Format(due[0].Due!.Value, now)}";
            }
            else
            {
                title = $"有 {due.Count} 個待辦事項到期";
                text = string.Join("\n", due.Take(4).Select(i => "• " + i.Text)) + (due.Count > 4 ? "\n…" : "");
            }
            _reminderShown = true;
            _tray.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
            System.Media.SystemSounds.Asterisk.Play();
            Save();
        }
        _todo?.RefreshItems(); // 更新逾期顏色
        ScheduleReminders();
    }

    // ================= 快捷鍵與系統訊息 =================

    private void RegisterHotkeys()
    {
        // 快捷鍵可在 config.json 的 hotkeyEdit / hotkeyTodo 修改；註冊失敗（被其他程式占用）時提示使用者
        var failed = new List<string>();
        foreach (var (id, text) in new[] { (HotkeyEdit, _cfg.HotkeyEdit), (HotkeyTodo, _cfg.HotkeyTodo) })
        {
            if (!Hotkey.TryParse(text, out uint mods, out uint vk) ||
                !Native.RegisterHotKey(_host.Handle, id, mods | Native.MOD_NOREPEAT, vk))
                failed.Add(Hotkey.Display(text));
        }
        bool testMode = Environment.GetEnvironmentVariable("DESKFRAMES_DATA") is { Length: > 0 };
        if (failed.Count > 0 && !testMode)
            _tray.ShowBalloonTip(6000, "DeskFrames 快捷鍵無法使用",
                $"{string.Join("、", failed)} 已被其他程式占用或格式錯誤。可在設定資料夾的 config.json 修改 hotkeyEdit / hotkeyTodo。",
                ToolTipIcon.Warning);
    }

    private void OnHotkey(int id)
    {
        if (id == HotkeyEdit) ToggleEdit();
        else if (id == HotkeyTodo)
        {
            if (_cfg.Todo.Visible && _todo is { Visible: true }) HideTodo();
            else { ShowTodo(); _todo?.FocusInput(); }
        }
    }

    /// <summary>Explorer 重新啟動後（工作列重建），重新把視窗釘回桌面。</summary>
    private void OnExplorerRestarted()
    {
        var t = new System.Windows.Forms.Timer { Interval = 1500 };
        t.Tick += (_, _) =>
        {
            t.Stop(); t.Dispose();
            DesktopHost.Refresh();
            bool rebuild = _windows.Any(w => !w.Alive);
            if (rebuild)
            {
                foreach (var w in _windows) w.Dispose();
                _windows.Clear();
                if (_cfg.FramesVisible) SyncWindows();
            }
            else { foreach (var w in _windows) w.RePin(); ArrangeZOrder(); }

            if (_todo != null && !_todo.IsDisposed)
            {
                if (!_todo.IsHandleCreated || !Native.IsWindow(_todo.Handle))
                {
                    _todo.Dispose(); _todo = null;
                    if (_cfg.Todo.Visible) ShowTodo();
                }
                else _todo.RePin();
            }
        };
        t.Start();
    }

    private void OnDisplayChanged()
    {
        foreach (var w in _windows) w.Render();
    }

    private void OnSystemThemeChanged()
    {
        if (_cfg.Theme == "system") Theme.Apply("system");
    }

    /// <summary>隱藏的頂層訊息視窗：接收快捷鍵、TaskbarCreated、顯示設定變更等廣播訊息。</summary>
    private sealed class HostWindow : NativeWindow
    {
        private readonly AppController _c;
        private readonly int _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        public HostWindow(AppController c)
        {
            _c = c;
            CreateHandle(new CreateParams { Caption = "DeskFramesHost", ExStyle = Native.WS_EX_TOOLWINDOW });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY) _c.OnHotkey(m.WParam.ToInt32());
            else if (m.Msg is 0x0011 /*WM_QUERYENDSESSION*/ or 0x0016 /*WM_ENDSESSION*/) ConfigStore.Flush();
            else if (m.Msg == _taskbarCreated && _taskbarCreated != 0) _c.OnExplorerRestarted();
            else if (m.Msg == Native.WM_DISPLAYCHANGE) _c.OnDisplayChanged();
            else if (m.Msg == Native.WM_SETTINGCHANGE && m.LParam != IntPtr.Zero &&
                     System.Runtime.InteropServices.Marshal.PtrToStringUni(m.LParam) == "ImmersiveColorSet")
                _c.OnSystemThemeChanged();
            base.WndProc(ref m);
        }
    }
}
