using System.Windows;

namespace PromptEditor.Views;

public partial class GotoLineWindow : Window
{
    public int LineNumber { get; private set; }

    public GotoLineWindow(int maxLine)
    {
        InitializeComponent();
        HintText.Text = $"行号 (1 - {maxLine})：";
        Loaded += (_, _) => LineBox.Focus();
    }

    private void OnGo(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(LineBox.Text.Trim(), out int n) || n < 1)
        {
            MessageBox.Show(this, "请输入有效的行号。", "跳转到行", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        LineNumber = n;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
