using System.Windows;

namespace PromptEditor.Views;

public partial class TableDialog : Window
{
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public string[] Headers { get; private set; } = Array.Empty<string>();

    public TableDialog()
    {
        InitializeComponent();
        RowsBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(RowsBox.Text.Trim(), out int rows) || rows < 1 || rows > 500)
        {
            MessageBox.Show(this, "行数必须是 1~500 的整数。", "创建表格", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(ColsBox.Text.Trim(), out int cols) || cols < 1 || cols > 50)
        {
            MessageBox.Show(this, "列数必须是 1~50 的整数。", "创建表格", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Rows = rows;
        Cols = cols;
        Headers = HeaderBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
