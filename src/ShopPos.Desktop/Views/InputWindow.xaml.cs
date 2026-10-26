using System.Windows;

namespace ShopPos.Desktop.Views;

/// <summary>A tiny "type one value" dialog.</summary>
public partial class InputWindow : Window
{
    public InputWindow(string title, string prompt, string initialText = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    /// <summary>What was typed (valid after the window returned true).</summary>
    public string Result { get; private set; } = string.Empty;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text.Trim();
        DialogResult = true;
    }
}
