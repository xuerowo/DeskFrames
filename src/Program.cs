namespace DeskFrames;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--render-preview")
        {
            ApplicationConfiguration.Initialize();
            PreviewRenderer.Run(args[1]);
            return;
        }

        // 單一執行個體：若已在執行，通知既有程式開啟設定視窗後結束
        // 測試模式（DESKFRAMES_DATA）使用獨立的鎖，不影響正在執行的正式版本
        string tag = Environment.GetEnvironmentVariable("DESKFRAMES_DATA") is { Length: > 0 } ? ".Test" : "";
        using var mutex = new Mutex(true, @"Local\DeskFrames.Singleton" + tag, out bool first);
        if (!first)
        {
            try
            {
                // --edit：切換編輯模式；其他：開啟設定視窗
                using var ev = EventWaitHandle.OpenExisting(args.Contains("--edit") ? @"Local\DeskFrames.Edit" + tag : @"Local\DeskFrames.Show" + tag);
                ev.Set();
            }
            catch { }
            return;
        }
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DeskFrames.Show" + tag);
        using var editEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DeskFrames.Edit" + tag);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);

        var ctx = new AppController(args.Contains("--startup"), showEvent, editEvent);
        Application.Run(ctx);
        GC.KeepAlive(mutex);
    }

    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(ConfigStore.Dir);
            File.AppendAllText(Path.Combine(ConfigStore.Dir, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { }
    }
}
