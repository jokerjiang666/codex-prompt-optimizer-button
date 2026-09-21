using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CodexInputEnhancer;

public partial class VariablePromptWindow : Window
{
    private readonly List<(string Name, TextBox Box)> _fields = new();

    public IReadOnlyDictionary<string, string> Values { get; private set; } = new Dictionary<string, string>();

    public VariablePromptWindow(IReadOnlyList<string> names)
    {
        InitializeComponent();

        foreach (var name in names)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var box = new TextBox { MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
            panel.Children.Add(box);

            FieldPanel.Children.Add(panel);
            _fields.Add((name, box));
        }

        if (_fields.Count > 0)
        {
            Loaded += (_, _) => _fields[0].Box.Focus();
        }
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        Values = _fields.ToDictionary(f => f.Name, f => f.Box.Text.Trim(), StringComparer.Ordinal);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}