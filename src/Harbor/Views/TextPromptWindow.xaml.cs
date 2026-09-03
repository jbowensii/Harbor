using System.Windows;

namespace Harbor.Views;

/// <summary>A one-field prompt. WPF has no built-in equivalent of an input box.</summary>
public partial class TextPromptWindow : Window
{
    public string Value { get; private set; } = string.Empty;

    public TextPromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt.ToUpperInvariant();
        Input.Text = initial;

        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Value = Input.Text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
