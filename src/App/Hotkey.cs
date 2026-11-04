namespace DeskFrames;

/// <summary>全域快捷鍵字串解析，例如 "ctrl+alt+q"、"win+shift+f2"。</summary>
public static class Hotkey
{
    public static bool TryParse(string? text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= 0x8; break;
                default:
                    if (vk != 0) return false;
                    if (raw.Length == 1 && char.IsLetterOrDigit(raw[0])) vk = char.ToUpperInvariant(raw[0]);
                    else if (Enum.TryParse<Keys>(raw, true, out var k) && (int)k is > 0 and < 256) vk = (uint)k;
                    else return false;
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    /// <summary>顯示用格式，例如 "Ctrl+Alt+Q"。</summary>
    public static string Display(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return string.Join("+", text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
