using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using PromptEditorLib.Tags;

namespace PromptEditor.Views;

/// <summary>标签库管理窗口：增删改、导入导出、恢复默认（删除需二次确认）。</summary>
public partial class TagLibraryWindow : Window
{
    /// <summary>确定后为 true，调用方读取 ResultLibrary。</summary>
    public bool Saved { get; private set; }

    public TagLibrary? ResultLibrary { get; private set; }

    public ObservableCollection<TagRow> Rows { get; } = new();

    /// <summary>
    /// 数据行。由 DataGrid 双向绑定直接改写属性，代码侧只在增删时操作
    /// 集合本身，不就地改已有行，因此无需 INotifyPropertyChanged。
    /// </summary>
    public sealed class TagRow
    {
        public string Name { get; set; } = "";
        public string StartTag { get; set; } = "";
        public string EndTag { get; set; } = "";
        public string WrapModeText { get; set; } = "Block";
        public bool IsPinned { get; set; }

        public static TagRow From(TagDefinition t) => new()
        {
            Name = t.Name,
            StartTag = t.StartTag,
            EndTag = t.EndTag,
            WrapModeText = t.WrapMode == TagWrapMode.Inline ? "Inline" : "Block",
            IsPinned = t.IsPinned,
        };

        public TagDefinition To() => new(
            Name.Trim(), StartTag, EndTag,
            WrapModeText.Equals("Inline", StringComparison.OrdinalIgnoreCase)
                ? TagWrapMode.Inline : TagWrapMode.Block,
            IsPinned);
    }

    public TagLibraryWindow(TagLibrary library)
    {
        InitializeComponent();
        foreach (var t in library.Tags)
            Rows.Add(TagRow.From(t));
        Grid.ItemsSource = Rows;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var row = new TagRow { Name = "new_tag", StartTag = "[NEW START]", EndTag = "[NEW END]" };
        Rows.Add(row);
        Grid.SelectedItem = row;
        Grid.ScrollIntoView(row);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not TagRow row)
        {
            MessageBox.Show(this, "请先选择要删除的标签。", "标签库管理",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(this,
            $"确定删除标签「{row.Name}」吗？", "删除确认",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm == MessageBoxResult.Yes)
            Rows.Remove(row);
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "恢复默认预设将丢弃当前全部标签定义，确定吗？", "恢复默认",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        Rows.Clear();
        foreach (var t in TagLibrary.CreateDefault().Tags)
            Rows.Add(TagRow.From(t));
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入标签库",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var lib = TagLibrary.Load(dlg.FileName);
            Rows.Clear();
            foreach (var t in lib.Tags)
                Rows.Add(TagRow.From(t));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "导入标签库",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出标签库",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = "tags.json",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            BuildLibrary().Save(dlg.FileName);
            MessageBox.Show(this, "导出成功。", "导出标签库",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：{ex.Message}", "导出标签库",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private TagLibrary BuildLibrary() =>
        new() { Tags = Rows.Select(r => r.To()).ToList() };

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var lib = BuildLibrary();
        var validation = lib.Validate();
        if (validation.HasErrors)
        {
            MessageBox.Show(this,
                "存在无法保存的问题：\n" + string.Join("\n", validation.Errors),
                "标签库管理", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (validation.Warnings.Count > 0)
        {
            var confirm = MessageBox.Show(this,
                string.Join("\n", validation.Warnings) + "\n\n仍要保存吗？",
                "标签库警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        ResultLibrary = lib;
        Saved = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
