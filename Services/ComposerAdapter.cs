using System.Runtime.InteropServices;
using System.Windows.Automation;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class ComposerAdapter
{
    public string? ReadText(ComposerTarget target)
    {
        try
        {
            if (target.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)
                && valueObject is ValuePattern value)
            {
                return NormalizePlaceholder(target.Element, value.Current.Value);
            }

            if (target.Element.TryGetCurrentPattern(TextPattern.Pattern, out var textObject)
                && textObject is TextPattern text)
            {
                return NormalizePlaceholder(
                    target.Element,
                    text.DocumentRange.GetText(-1).TrimEnd('\r', '\n'));
            }
        }
        catch (ElementNotAvailableException) { }
        catch (COMException) { }

        return null;
    }

    private static string NormalizePlaceholder(AutomationElement element, string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var className = element.Current.ClassName ?? string.Empty;
        if (!className.StartsWith("ProseMirror", StringComparison.OrdinalIgnoreCase))
            return text;

        var placeholder = element.Current.Name?.Trim();
        if (string.IsNullOrWhiteSpace(placeholder))
            return text;

        // Codex 的 ProseMirror 空输入框会把占位符当作文本返回。
        return string.Equals(text.Trim(), placeholder, StringComparison.Ordinal)
            ? string.Empty
            : text;
    }

    public bool CanWrite(ComposerTarget target)
    {
        try
        {
            return target.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)
                   && valueObject is ValuePattern value
                   && !value.Current.IsReadOnly;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public bool WriteText(ComposerTarget target, string text)
    {
        try
        {
            if (target.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueObject)
                && valueObject is ValuePattern value
                && !value.Current.IsReadOnly)
            {
                value.SetValue(text);
                return true;
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
        catch (COMException) { }

        return false;
    }
}
