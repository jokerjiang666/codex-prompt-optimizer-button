namespace CodexInputEnhancer.Models;

public sealed class RecentHistoryItem
{
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }

    public string Preview => Text.Length <= 70 ? Text : Text[..70] + "…";
    public string DisplayTime => SentAt.LocalDateTime.ToString("MM-dd HH:mm");
}
