using System.Windows;
using System.Windows.Input;
using CodexInputEnhancer.Services;

namespace CodexInputEnhancer;

public enum PreviewChoice
{
    Keep,
    Apply,
    Rewrite
}

public partial class PreviewWindow : Window
{
    private readonly string _updated;

    public PreviewChoice Choice { get; private set; } = PreviewChoice.Keep;

    public PreviewWindow(string original, string updated, string meta)
    {
        InitializeComponent();

        _updated = updated;
        MetaText.Text = meta;

        var diff = TextDiff.Compute(original, updated);
        OriginalList.ItemsSource = diff.Original;
        UpdatedList.ItemsSource = diff.Updated;
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = PreviewChoice.Apply;
        DialogResult = true;
    }

    private void KeepButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = PreviewChoice.Keep;
        DialogResult = false;
    }

    private void RewriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        Choice = PreviewChoice.Rewrite;
        DialogResult = true;
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_updated);
            CopyStatus.Text = "已复制到剪贴板";
        }
        catch
        {
            CopyStatus.Text = "复制失败";
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Choice = PreviewChoice.Keep;
        DialogResult = false;
    }
}