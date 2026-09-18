using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexInputEnhancer.Models;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settingsStore;
    private readonly SecretStore _secretStore;
    private readonly RecentHistoryStore _recentHistoryStore;

    public event Action<AppSettings>? SettingsSaved;
    public event Action<string>? RestoreRecentRequested;
    public event EventHandler? RecentHistoryChanged;

    public SettingsWindow(
        SettingsStore settingsStore,
        SecretStore secretStore,
        RecentHistoryStore recentHistoryStore)
    {
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _recentHistoryStore = recentHistoryStore;
        InitializeComponent();
        LoadForm();
        RefreshRecentList();
        RefreshOptimizationLog();
    }

    public void ShowRecentTab()
    {
        ShowPanel(RecentPanel, RecentNavButton);
        RefreshRecentList();
    }

    public void RefreshRecent() => RefreshRecentList();

    private void LoadForm()
    {
        var settings = _settingsStore.Load();
        var isApi = string.Equals(settings.Provider, "openai-compatible", StringComparison.OrdinalIgnoreCase);
        CodexProviderRadio.IsChecked = !isApi;
        ApiProviderRadio.IsChecked = isApi;
        ApiBaseUrlBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Password = _secretStore.Read();
        ModelBox.Text = settings.Model;
        PromptBox.Text = settings.OptimizationPrompt;
        HistoryEnabledCheckBox.IsChecked = settings.HistoryEnabled;
        SelectReasoning(settings.ReasoningEffort);
        UpdateApiFields();
    }

    private AppSettings ReadForm() => new()
    {
        Provider = ApiProviderRadio.IsChecked == true ? "openai-compatible" : "codex-cli",
        ApiBaseUrl = ApiBaseUrlBox.Text.Trim(),
        Model = ModelBox.Text.Trim(),
        ReasoningEffort = SelectedReasoning(),
        TimeoutSeconds = 60,
        HistoryEnabled = HistoryEnabledCheckBox.IsChecked != false,
        OptimizationPrompt = string.IsNullOrWhiteSpace(PromptBox.Text)
            ? AppSettings.DefaultOptimizationPrompt
            : PromptBox.Text.Trim()
    };

    private void SelectReasoning(string value)
    {
        foreach (var item in ReasoningBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                ReasoningBox.SelectedItem = item;
                return;
            }
        }
        ReasoningBox.SelectedIndex = 0;
    }

    private string SelectedReasoning() =>
        (ReasoningBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "low";

    private void ProviderRadio_OnChecked(object sender, RoutedEventArgs e) => UpdateApiFields();

    private void UpdateApiFields()
    {
        if (ApiBaseUrlBox is null || ApiKeyBox is null) return;
        var enabled = ApiProviderRadio.IsChecked == true;
        ApiBaseUrlBox.IsEnabled = enabled;
        ApiKeyBox.IsEnabled = enabled;
        if (GetModelsButton is not null) GetModelsButton.IsEnabled = enabled;
    }

    private async void GetModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var currentModel = ModelBox.Text;
        GetModelsButton.IsEnabled = false;
        GetModelsButton.Content = "获取中…";
        ModelFetchStatusText.Text = string.Empty;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var models = await OpenAiCompatibleProvider.GetModelsAsync(
                ApiBaseUrlBox.Text.Trim(),
                ApiKeyBox.Password,
                timeout.Token);

            ModelBox.Items.Clear();
            foreach (var model in models)
                ModelBox.Items.Add(model);

            ModelBox.Text = currentModel;
            ModelFetchStatusText.Text = $"已获取 {models.Count} 个模型";
        }
        catch (OperationCanceledException)
        {
            ModelFetchStatusText.Text = "获取模型超时";
        }
        catch (Exception ex)
        {
            ModelFetchStatusText.Text = $"获取失败：{ex.Message}";
        }
        finally
        {
            GetModelsButton.Content = "获取模型";
            GetModelsButton.IsEnabled = ApiProviderRadio.IsChecked == true;
        }
    }

    private void RestorePromptButton_OnClick(object sender, RoutedEventArgs e)
    {
        PromptBox.Text = AppSettings.DefaultOptimizationPrompt;
        PromptBox.Focus();
        PromptBox.CaretIndex = PromptBox.Text.Length;
    }

    private async void TestConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        TestStatusText.Text = "测试中…";
        try
        {
            var settings = ReadForm();
            var provider = OptimizerProviderFactory.Create(settings, ApiKeyBox.Password);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 5, 300)));
            _ = await provider.OptimizeAsync("请将“测试连接”改写得更清晰。", timeout.Token);
            TestStatusText.Text = "连接成功";
        }
        catch (OperationCanceledException)
        {
            TestStatusText.Text = "连接超时";
        }
        catch (Exception ex)
        {
            TestStatusText.Text = $"连接失败：{ex.Message}";
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SaveButton.Content = "保存中…";
        SaveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(110, 115, 111));
        SaveStatusText.Text = "正在保存设置…";

        // Yield once so the saving state is painted before the local IO starts.
        await Dispatcher.Yield(DispatcherPriority.Background);

        try
        {
            var settings = ReadForm();
            _settingsStore.Save(settings);
            _secretStore.Write(ApiKeyBox.Password);
            SettingsSaved?.Invoke(settings);
            SaveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(47, 122, 75));
            SaveStatusText.Text = "保存成功";
            TestStatusText.Text = "已保存";
        }
        catch (Exception ex)
        {
            SaveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(176, 58, 58));
            SaveStatusText.Text = $"保存失败：{ex.Message}";
        }
        finally
        {
            SaveButton.Content = "保存";
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void AiNavButton_OnClick(object sender, RoutedEventArgs e) => ShowPanel(AiPanel, AiNavButton);
    private void RecentNavButton_OnClick(object sender, RoutedEventArgs e) => ShowRecentTab();
    private void LogNavButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshOptimizationLog();
        ShowPanel(LogPanel, LogNavButton);
    }
    private void BehaviorNavButton_OnClick(object sender, RoutedEventArgs e) => ShowPanel(BehaviorPanel, BehaviorNavButton);
    private void AboutNavButton_OnClick(object sender, RoutedEventArgs e) => ShowPanel(AboutPanel, AboutNavButton);

    private void ShowPanel(UIElement panel, Button navButton)
    {
        foreach (var candidate in new UIElement[] { AiPanel, RecentPanel, LogPanel, BehaviorPanel, AboutPanel })
            candidate.Visibility = ReferenceEquals(candidate, panel) ? Visibility.Visible : Visibility.Collapsed;

        foreach (var button in new[] { AiNavButton, RecentNavButton, LogNavButton, BehaviorNavButton, AboutNavButton })
        {
            button.Background = ReferenceEquals(button, navButton)
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 235, 231))
                : System.Windows.Media.Brushes.Transparent;
            button.FontWeight = ReferenceEquals(button, navButton) ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void RefreshRecentList()
    {
        var items = _recentHistoryStore.Load();
        RecentList.ItemsSource = items;
        RecentNavButton.Content = $"最近发送  {items.Count}";
    }

    private void RefreshLogButton_OnClick(object sender, RoutedEventArgs e) => RefreshOptimizationLog();

    private void RefreshOptimizationLog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "runtime-status.log");
            if (!File.Exists(path))
            {
                OptimizationLogList.ItemsSource = new[]
                {
                    new OptimizationLogRow("—", "暂无日志", "完成一次提示词优化后，这里会显示请求状态。")
                };
                return;
            }

            var rows = File.ReadLines(path)
                .Where(line => line.Contains(" optimize=", StringComparison.Ordinal))
                .Reverse()
                .Take(80)
                .Select(ParseOptimizationLog)
                .ToList();

            OptimizationLogList.ItemsSource = rows.Count > 0
                ? rows
                : new[] { new OptimizationLogRow("—", "暂无日志", "完成一次提示词优化后，这里会显示请求状态。") };
        }
        catch (Exception ex)
        {
            OptimizationLogList.ItemsSource = new[]
            {
                new OptimizationLogRow("—", "读取失败", ex.Message)
            };
        }
    }

    private static OptimizationLogRow ParseOptimizationLog(string line)
    {
        var firstSpace = line.IndexOf(' ');
        var time = "—";
        var state = line;
        if (firstSpace > 0)
        {
            if (DateTimeOffset.TryParse(line[..firstSpace], out var parsed))
                time = parsed.LocalDateTime.ToString("MM-dd HH:mm:ss");
            state = line[(firstSpace + 1)..];
        }

        var status = state switch
        {
            var s when s.StartsWith("optimize=started", StringComparison.Ordinal) => "开始优化",
            var s when s.StartsWith("optimize=success", StringComparison.Ordinal) => "优化成功",
            var s when s.StartsWith("optimize=cancelled", StringComparison.Ordinal) => "已取消",
            var s when s.StartsWith("optimize=nochange", StringComparison.Ordinal) => "无需修改",
            var s when s.StartsWith("optimize=ignored", StringComparison.Ordinal) => "已忽略",
            var s when s.StartsWith("optimize=failed", StringComparison.Ordinal) => "优化失败",
            _ => "优化状态"
        };

        var detailsIndex = state.IndexOf(' ');
        var details = detailsIndex >= 0 ? state[(detailsIndex + 1)..] : string.Empty;
        return new OptimizationLogRow(time, status, details);
    }

    private sealed record OptimizationLogRow(string Time, string Status, string Details);

    private void RestoreRecentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentHistoryItem item) return;
        RestoreRecentRequested?.Invoke(item.Text);
    }

    private void CopyRecentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentHistoryItem item) return;
        Clipboard.SetText(item.Text);
    }

    private void DeleteRecentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (RecentList.SelectedItem is not RecentHistoryItem item) return;
        _recentHistoryStore.Delete(item);
        RefreshRecentList();
        RecentHistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRecentButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_recentHistoryStore.Load().Count == 0) return;
        if (MessageBox.Show(this, "确定清空最近发送记录？", "Codex Input Enhancer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _recentHistoryStore.Clear();
        RefreshRecentList();
        RecentHistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}
