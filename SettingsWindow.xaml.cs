using System.IO;
using System.Text;
using Microsoft.Win32;
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
    private List<OptimizationTemplate> _templates = new();
    private string _activeTemplateId = "builtin-basic";
    private bool _loadingTemplates;
    private AppSettings? _loadedSettings;

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
        ShowContinueToggle.IsChecked = settings.ShowContinueButton;
        AutoContinueToggle.IsChecked = settings.AutoContinueEnabled;
        AutoContinueTextBox.Text = string.IsNullOrWhiteSpace(settings.AutoContinueText) ? "继续" : settings.AutoContinueText;
        AutoContinueIntervalBox.Text = Math.Clamp(settings.AutoContinueMinIntervalSeconds, 3, 600).ToString();
        _loadedSettings = settings;
        TemplateLibrary.Ensure(settings);
        _templates = settings.Templates.Select(TemplateLibrary.Clone).ToList();
        _activeTemplateId = settings.ActiveTemplateId;
        RefreshTemplateList(_templates.FirstOrDefault(t => string.Equals(t.Id, _activeTemplateId, StringComparison.Ordinal)) ?? _templates.FirstOrDefault());

        DeepOptimizeToggle.IsChecked = settings.DeepOptimizeEnabled;
        DeepOptimizeRoundsBox.Text = Math.Clamp(settings.DeepOptimizeRounds, 1, 3).ToString();
        PreviewBeforeApplyToggle.IsChecked = settings.PreviewBeforeApply;
        ContextEnabledToggle.IsChecked = settings.ContextEnabled;
        ContextThreadTailCheck.IsChecked = settings.ContextThreadTailEnabled;
        ContextGitCheck.IsChecked = settings.ContextGitEnabled;
        ContextWorkspaceMetaCheck.IsChecked = settings.ContextWorkspaceMetaEnabled;
        ContextTokenBudgetBox.Text = Math.Clamp(settings.ContextTokenBudget, 200, 4000).ToString();
        ContextMessageLimitBox.Text = Math.Clamp(settings.ContextMessageLimit, 0, 20).ToString();
        ContextStaleSecondsBox.Text = Math.Clamp(settings.ContextStaleSeconds, 30, 3600).ToString();
        UpdateContextControlsEnabled();
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
        Templates = _templates.Select(TemplateLibrary.Clone).ToList(),
        ActiveTemplateId = _activeTemplateId,
        DeepOptimizeEnabled = DeepOptimizeToggle.IsChecked == true,
        DeepOptimizeRounds = ParseRounds(DeepOptimizeRoundsBox.Text),
        PreviewBeforeApply = PreviewBeforeApplyToggle.IsChecked != false,
        ContextEnabled = ContextEnabledToggle.IsChecked == true,
        ContextThreadTailEnabled = ContextThreadTailCheck.IsChecked == true,
        ContextGitEnabled = ContextGitCheck.IsChecked == true,
        ContextWorkspaceMetaEnabled = ContextWorkspaceMetaCheck.IsChecked == true,
        ContextTokenBudget = ParseContextBudget(ContextTokenBudgetBox.Text),
        ContextMessageLimit = ParseContextMessageLimit(ContextMessageLimitBox.Text),
        ContextStaleSeconds = ParseContextStaleSeconds(ContextStaleSecondsBox.Text),
        ContextShowInPreview = _loadedSettings?.ContextShowInPreview ?? true,
        TemplateLanguage = _loadedSettings?.TemplateLanguage ?? "zh",
        SettingsStyle = _loadedSettings?.SettingsStyle ?? "A",
        HistoryEnabled = HistoryEnabledCheckBox.IsChecked != false,
        ShowContinueButton = ShowContinueToggle.IsChecked != false,
        AutoContinueEnabled = AutoContinueToggle.IsChecked == true,
        AutoContinueText = string.IsNullOrWhiteSpace(AutoContinueTextBox.Text) ? "继续" : AutoContinueTextBox.Text.Trim(),
        AutoContinueMinIntervalSeconds = ParseAutoContinueInterval(AutoContinueIntervalBox.Text),
        OptimizationPrompt = string.IsNullOrWhiteSpace(PromptBox.Text)
            ? AppSettings.DefaultOptimizationPrompt
            : PromptBox.Text.Trim()
    };

    private static int ParseAutoContinueInterval(string? text)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var seconds)) return 10;
        return Math.Clamp(seconds, 3, 600);
    }

    private static int ParseContextBudget(string? text)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var budget)) return 1200;
        return Math.Clamp(budget, 200, 4000);
    }

    private static int ParseContextMessageLimit(string? text)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var limit)) return 6;
        return Math.Clamp(limit, 0, 20);
    }

    private static int ParseContextStaleSeconds(string? text)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var seconds)) return 300;
        return Math.Clamp(seconds, 30, 3600);
    }

    private void ContextEnabledToggle_OnCheckChanged(object sender, RoutedEventArgs e) => UpdateContextControlsEnabled();

    private void UpdateContextControlsEnabled()
    {
        if (ContextEnabledToggle is null ||
            ContextThreadTailCheck is null ||
            ContextGitCheck is null ||
            ContextWorkspaceMetaCheck is null ||
            ContextTokenBudgetBox is null ||
            ContextMessageLimitBox is null ||
            ContextStaleSecondsBox is null)
        {
            return;
        }

        var enabled = ContextEnabledToggle.IsChecked == true;
        ContextThreadTailCheck.IsEnabled = enabled;
        ContextGitCheck.IsEnabled = enabled;
        ContextWorkspaceMetaCheck.IsEnabled = enabled;
        ContextTokenBudgetBox.IsEnabled = enabled;
        ContextMessageLimitBox.IsEnabled = enabled;
        ContextStaleSecondsBox.IsEnabled = enabled;
    }

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
            _ = await provider.OptimizeAsync(
                "你是连接测试助手。只输出改写后的文本，不要解释。",
                "请将“测试连接”改写得更清晰。",
                timeout.Token);
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

    private void TemplateNavButton_OnClick(object sender, RoutedEventArgs e) => ShowPanel(TemplatePanel, TemplateNavButton);

    private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTemplates) return;
        UpdateTemplateHeader();
    }

    private void RefreshTemplateList(OptimizationTemplate? select)
    {
        _loadingTemplates = true;
        TemplateList.ItemsSource = null;
        TemplateList.ItemsSource = _templates;
        TemplateList.SelectedItem = select;
        _loadingTemplates = false;
        UpdateTemplateHeader();
    }

    private void UpdateTemplateHeader()
    {
        if (TemplateCurrentText is null) return;
        var active = _templates.FirstOrDefault(t => string.Equals(t.Id, _activeTemplateId, StringComparison.Ordinal));
        TemplateCurrentText.Text = $"当前模板：{active?.Name ?? "—"}";
    }

    private void NewTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var template = new OptimizationTemplate
        {
            Id = NewTemplateId(),
            Name = "新模板",
            Description = "自定义模板",
            Category = "自定义",
            SystemPrompt = AppSettings.DefaultOptimizationPrompt,
            UserTemplate = PromptComposer.DefaultUserTemplate,
            IsBuiltin = false,
            Language = "zh"
        };
        _templates.Add(template);
        RefreshTemplateList(template);
    }

    private void DuplicateTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not OptimizationTemplate source) return;
        var copy = TemplateLibrary.Clone(source);
        copy.Id = NewTemplateId();
        copy.Name = source.Name + " 副本";
        copy.IsBuiltin = false;
        _templates.Add(copy);
        RefreshTemplateList(copy);
    }

    private void DeleteTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not OptimizationTemplate selected) return;
        if (selected.IsBuiltin)
        {
            MessageBox.Show(this, "内置模板不能删除。可以先「复制」一份，再改副本。", "Codex Input Enhancer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"确定删除模板「{selected.Name}」？", "Codex Input Enhancer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _templates.Remove(selected);
        if (string.Equals(_activeTemplateId, selected.Id, StringComparison.Ordinal))
            _activeTemplateId = _templates.FirstOrDefault()?.Id ?? "builtin-basic";

        RefreshTemplateList(_templates.FirstOrDefault(t => string.Equals(t.Id, _activeTemplateId, StringComparison.Ordinal)) ?? _templates.FirstOrDefault());
    }

    private void UseTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not OptimizationTemplate selected) return;
        _activeTemplateId = selected.Id;
        UpdateTemplateHeader();
        SaveStatusText.Text = $"已设为当前模板：{selected.Name}（点保存后生效）";
    }

    private void ImportTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入优化模板",
            Filter = "JSON 模板 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var imported = TemplateTransfer.Import(File.ReadAllText(dialog.FileName));
            var last = (OptimizationTemplate?)null;
            foreach (var template in imported)
            {
                if (_templates.Any(t => string.Equals(t.Id, template.Id, StringComparison.Ordinal)))
                    template.Id = NewTemplateId();
                _templates.Add(template);
                last = template;
            }

            RefreshTemplateList(last);
            SaveStatusText.Text = $"已导入 {imported.Count} 个模板";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "Codex Input Enhancer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportTemplateButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (TemplateList.SelectedItem is not OptimizationTemplate selected) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出优化模板",
            FileName = SanitizeFileName(selected.Name) + ".json",
            Filter = "JSON 模板 (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, TemplateTransfer.Export(selected), new UTF8Encoding(false));
            SaveStatusText.Text = $"已导出模板：{selected.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "Codex Input Enhancer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string NewTemplateId() => "custom-" + Guid.NewGuid().ToString("N")[..8];

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "template" : cleaned;
    }

    private static int ParseRounds(string? text)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out var rounds)) return 2;
        return Math.Clamp(rounds, 1, 3);
    }

    private void ShowPanel(UIElement panel, Button navButton)
    {
        foreach (var candidate in new UIElement[] { TemplatePanel, AiPanel, RecentPanel, LogPanel, BehaviorPanel, AboutPanel })
            candidate.Visibility = ReferenceEquals(candidate, panel) ? Visibility.Visible : Visibility.Collapsed;

        foreach (var button in new[] { TemplateNavButton, AiNavButton, RecentNavButton, LogNavButton, BehaviorNavButton, AboutNavButton })
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
