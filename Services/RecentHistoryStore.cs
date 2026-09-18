using System.IO;
using System.Text.Json;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class RecentHistoryStore
{
    private const int MaxItems = 10;
    private readonly string _path;

    public RecentHistoryStore(SettingsStore settingsStore)
    {
        _path = Path.Combine(settingsStore.DataDirectory, "recent-history.dat");
    }

    public IReadOnlyList<RecentHistoryItem> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var encrypted = File.ReadAllBytes(_path);
            var plain = UserDataProtector.Unprotect(encrypted);
            return JsonSerializer.Deserialize<List<RecentHistoryItem>>(plain) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Add(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0) return;

        var items = Load().ToList();
        items.RemoveAll(item => string.Equals(item.Text, normalized, StringComparison.Ordinal));
        items.Insert(0, new RecentHistoryItem { Text = normalized, SentAt = DateTimeOffset.Now });
        Save(items.Take(MaxItems).ToList());
    }

    public void Delete(RecentHistoryItem item)
    {
        var items = Load().Where(existing =>
            existing.SentAt != item.SentAt || !string.Equals(existing.Text, item.Text, StringComparison.Ordinal)).ToList();
        Save(items);
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private void Save(List<RecentHistoryItem> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(items);
        File.WriteAllBytes(_path, UserDataProtector.Protect(plain));
    }
}
