namespace DeskFrames;

/// <summary>待辦事項到期時間：顯示格式、狀態與常用快捷時間。</summary>
internal static class TodoDue
{
    private static readonly string[] Week = { "日", "一", "二", "三", "四", "五", "六" };

    public enum State { None, Normal, Today, Overdue }

    public static string WeekName(DayOfWeek d) => Week[(int)d];

    /// <summary>例如「今天 18:00」「明天 09:00」「10/3（五）14:00」「2027/1/5 09:00」。</summary>
    public static string Format(DateTime due, DateTime now)
    {
        var d = due.Date; var t = now.Date;
        string time = $"{due.Hour:00}:{due.Minute:00}";
        if (d == t) return $"今天 {time}";
        if (d == t.AddDays(1)) return $"明天 {time}";
        if (d == t.AddDays(-1)) return $"昨天 {time}";
        if (d.Year == t.Year) return $"{d.Month}/{d.Day}（{Week[(int)d.DayOfWeek]}）{time}";
        return $"{d.Year}/{d.Month}/{d.Day} {time}";
    }

    public static State StateOf(TodoItem it, DateTime now)
    {
        if (it.Due is not DateTime due) return State.None;
        if (it.Done) return State.Normal;
        if (due <= now) return State.Overdue;
        return due.Date == now.Date ? State.Today : State.Normal;
    }

    /// <summary>右鍵選單的快捷時間（依目前時間動態產生）。</summary>
    public static List<(string label, DateTime value)> Presets(DateTime now)
    {
        var list = new List<(string, DateTime)>();
        var today = now.Date;
        if (now.Hour < 18) list.Add(("今天 18:00", today.AddHours(18)));
        else if (now.Hour < 21) list.Add(("今天 21:00", today.AddHours(21)));
        list.Add(("明天 09:00", today.AddDays(1).AddHours(9)));
        int toMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (toMonday == 0) toMonday = 7;
        list.Add(("下週一 09:00", today.AddDays(toMonday).AddHours(9)));
        return list;
    }

    /// <summary>解析時間：9、9:30、09:30、930、0930、21.30。</summary>
    public static bool TryParseTime(string? s, out int hour, out int minute)
    {
        hour = minute = 0;
        s = (s ?? "").Trim().Replace('：', ':').Replace('.', ':');
        if (s.Length == 0) return false;
        string h, m;
        int colon = s.IndexOf(':');
        if (colon >= 0) { h = s[..colon]; m = s[(colon + 1)..]; }
        else if (s.Length <= 2) { h = s; m = "0"; }
        else if (s.Length is 3 or 4) { h = s[..^2]; m = s[^2..]; }
        else return false;
        if (m.Length == 0) m = "0";
        return int.TryParse(h, out hour) && int.TryParse(m, out minute) && hour is >= 0 and <= 23 && minute is >= 0 and <= 59;
    }
}
