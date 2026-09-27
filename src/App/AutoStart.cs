using Microsoft.Win32;

namespace DeskFrames;

/// <summary>開機自動啟動（HKCU\...\Run，不需系統管理員權限）。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DeskFrames";

    private static string Command => $"\"{Environment.ProcessPath}\" --startup";

    /// <summary>測試時（設定 DESKFRAMES_DATA）不寫入登錄檔。</summary>
    private static bool Disabled => Environment.GetEnvironmentVariable("DESKFRAMES_DATA") is { Length: > 0 };

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) is string;
        }
        catch { return false; }
    }

    public static void Set(bool enabled)
    {
        if (Disabled) return;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled) k.SetValue(Name, Command);
            else k.DeleteValue(Name, false);
        }
        catch { }
    }

    /// <summary>程式搬移位置後，自動更新登錄檔中的路徑。</summary>
    public static void Sync(bool enabled)
    {
        if (Disabled) return;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            var cur = k?.GetValue(Name) as string;
            if (enabled && cur != Command) Set(true);
            else if (!enabled && cur != null) Set(false);
        }
        catch { }
    }
}
