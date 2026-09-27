using System.Windows;
using System.Windows.Input;
using PromptEditorLib.Markdown;
using PromptEditor.ViewModels;

namespace PromptEditor.Views;

public partial class QuickJumpWindow : Window
{
    private readonly MainViewModel _vm;
    private List<OutlineItem> _allItems = new();

    private sealed record Row(int TargetLine, string Display, string LineDisplay);

    public QuickJumpWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // 当前激活标签的大纲（标题 + 顶层标签）
        var doc = vm.ActiveDocument;
        if (doc is not null)
        {
            _allItems = OutlineBuilder.Build(doc.Document.Text, vm.TagLibrary.Tags);
        }
        ResultList.ItemsSource = new List<Row>();
        Loaded += (_, _) => FilterBox.Focus();
    }

    private void OnFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => Refresh();

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => Refresh();

    private void Refresh()
    {
        string q = FilterBox.Text.Trim();

        // :数字 → 直接跳行
        if (q.StartsWith(':'))
        {
            ResultList.ItemsSource = int.TryParse(q[1..], out int n)
                ? new List<Row> { new(n, $"跳转到第 {n} 行", $"行 {n}") }
                : new List<Row>();
            return;
        }

        var source = _allItems
            .Select(i => new Row(i.Line + 1, i.Display, $"行 {i.Line + 1}"));

        if (q.Length > 0)
        {
            source = source.Where(r =>
                r.Display.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        ResultList.ItemsSource = source.Take(200).ToList();
        if (ResultList.Items.Count > 0) ResultList.SelectedIndex = 0;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Jump();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ReferenceEquals(sender, FilterBox))
        {
            if (ResultList.Items.Count > 0)
            {
                int next = Math.Min(ResultList.SelectedIndex + 1, ResultList.Items.Count - 1);
                ResultList.SelectedIndex = next;
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up && ReferenceEquals(sender, FilterBox))
        {
            int prev = Math.Max(ResultList.SelectedIndex - 1, 0);
            ResultList.SelectedIndex = prev;
            e.Handled = true;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => Jump();

    private void Jump()
    {
        if (ResultList.SelectedItem is not Row row) return;
        var doc = _vm.ActiveDocument;
        if (doc is null) return;

        doc.JumpToLine(row.TargetLine);
        DialogResult = true;
    }
}
