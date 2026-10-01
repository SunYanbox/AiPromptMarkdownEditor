using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PromptEditorLib.Tags;

namespace PromptEditor.Views;

/// <summary>
/// 标签选择器：可输入过滤（匹配短名 + 实际开始/结束标签文本，包含匹配），
/// 回车或点击应用包裹；提供标签库管理入口。
/// </summary>
public partial class TagPickerWindow : Window
{
    private TagLibrary _library;
    private List<TagDefinition> _all = new();

    public TagDefinition? SelectedTag { get; private set; }

    /// <summary>
    /// 通过本窗口的「标签库管理…」修改并保存过标签库时为 true，
    /// 调用方须在关闭后读取 UpdatedLibrary 并持久化（ShowDialog 返回值与 SelectedTag 无关）。
    /// </summary>
    public bool LibraryUpdated { get; private set; }

    public TagLibrary? UpdatedLibrary { get; private set; }

    private sealed record Row(string Display);

    public TagPickerWindow(TagLibrary library)
    {
        InitializeComponent();
        _library = library;
        _all = library.OrderedForMenu().ToList();
        Refresh();
        Loaded += (_, _) => FilterBox.Focus();
    }

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        string q = FilterBox.Text.Trim();
        IEnumerable<TagDefinition> source = _all;

        if (q.Length > 0)
        {
            source = source.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.StartTag.Contains(q, StringComparison.Ordinal) ||
                t.EndTag.Contains(q, StringComparison.Ordinal));
        }

        List.ItemsSource = source
            .Select(t => new Row($"{t.Name}  →  {t.StartTag} … {t.EndTag}"))
            .ToList();
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Apply();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && ReferenceEquals(sender, FilterBox) && List.Items.Count > 0)
        {
            List.SelectedIndex = Math.Min(List.SelectedIndex + 1, List.Items.Count - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ReferenceEquals(sender, FilterBox))
        {
            List.SelectedIndex = Math.Max(List.SelectedIndex - 1, 0);
            e.Handled = true;
        }
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e) => Apply();

    private void Apply()
    {
        if (List.SelectedItem is not Row row) return;
        // Row.Display 以 "短名  →  " 开头，还原短名
        int idx = row.Display.IndexOf("  →  ", StringComparison.Ordinal);
        string name = idx > 0 ? row.Display[..idx] : row.Display;
        SelectedTag = _all.FirstOrDefault(t => t.Name == name);
        if (SelectedTag is not null) DialogResult = true;
    }

    private void OnOpenTagLibrary(object sender, RoutedEventArgs e)
    {
        var dlg = new TagLibraryWindow(_library) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Saved && dlg.ResultLibrary is not null)
        {
            // 记录更新结果，由调用方（MainViewModel）持久化并写回全局标签库
            UpdatedLibrary = dlg.ResultLibrary;
            LibraryUpdated = true;

            // 本窗口即时生效
            _library = dlg.ResultLibrary;
            _all = dlg.ResultLibrary.OrderedForMenu().ToList();
            Refresh();
        }
    }
}
