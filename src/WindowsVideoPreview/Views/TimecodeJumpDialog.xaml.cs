using System.Windows;

namespace WindowsVideoPreview.Views;

public partial class TimecodeJumpDialog : Window
{
    public string ResultText { get; private set; } = string.Empty;

    public TimecodeJumpDialog(string currentTimecode)
    {
        InitializeComponent();
        InputTextBox.Text = currentTimecode;
        InputTextBox.SelectAll();
        Loaded += (_, _) => InputTextBox.Focus();
    }

    private void OnJumpClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputTextBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
