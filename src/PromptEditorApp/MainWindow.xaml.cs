using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PromptEditor.Controls;
using PromptEditor.Services;
using PromptEditor.ViewModels;

namespace PromptEditor;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(string[]? openArgs = null)
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ActiveEditorChanged += OnActiveEditorChanged;
        _vm.MatchesChanged += OnMatchesChanged;
        _vm.Tabs.CollectionChanged += OnTabsChanged;
        _vm.ShowFindBar += () => { FindBar.Visibility = Visibility.Visible; FindTextBox.Focus(); };
        _vm.HideFindBar += () => FindBar.Visibility = Visibility.Collapsed;
        InitQuickTagCombo();

        // 恢复窗口状态
        var s = App.Settings;
        if (!double.IsNaN(s.WindowX)) Left = s.WindowX;
        if (!double.IsNaN(s.WindowY)) Top = s.WindowY;
        Width = s.WindowWidth;
        Height = s.WindowHeight;
        WindowState = s.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
        if (!_vm.IsOutlineOpen) OutlinePanel.Visibility = Visibility.Collapsed;

        // 全局键盘处理（Ctrl+1~6、Tab/Shift+Tab、Ctrl+滚轮等）
        PreviewKeyDown += OnGlobalPreviewKeyDown;
        PreviewMouseWheel += OnGlobalPreviewMouseWheel;
        Drop += OnWindowDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Closing += OnWindowClosing;
        LocationChanged += OnWindowLocationChanged;
        StateChanged += OnWindowStateChanged;

        Loaded += (_, _) =>
        {
            _vm.EnsureAtLeastOneTab();
            // 启动时把编辑器挂载进窗口（构造阶段 ActiveEditorChanged 事件尚未订阅）
            if (_vm.ActiveDocument is null)
                _vm.ActivateTab(_vm.Tabs[0]);
            else
                OnActiveEditorChanged(_vm.ActiveDocument);
            if (openArgs is { Length: > 0 })
                _vm.OpenPaths(openArgs);
        };
    }

    // ---------- 编辑器实例切换 ----------

    private void OnActiveEditorChanged(DocumentViewModel? vm)
    {
        EditorHost.Content = vm?.Editor;
        if (vm is not null)
        {
            // 挂接右键菜单（每个编辑器实例挂一次）
            vm.Editor.ContextMenu ??= BuildEditorContextMenu();
            // 激活后把焦点交给编辑器
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => vm.Editor.Focus());
        }
    }

    // ---------- 编辑器右键菜单 ----------

    /// <summary>构建编辑器右键菜单：撤销/剪贴板 + 标题/样式 + 列表 + 外围标签。</summary>
    private ContextMenu BuildEditorContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItemOf("撤销", _vm.UndoCommand));
        menu.Items.Add(MenuItemOf("重做", _vm.RedoCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Command = ApplicationCommands.Cut });
        menu.Items.Add(new MenuItem { Command = ApplicationCommands.Copy });
        menu.Items.Add(new MenuItem { Command = ApplicationCommands.Paste });
        menu.Items.Add(new Separator());

        var headings = new MenuItem { Header = "标题" };
        RelayCommand[] headingCommands =
        {
            _vm.Heading1Command, _vm.Heading2Command, _vm.Heading3Command,
            _vm.Heading4Command, _vm.Heading5Command, _vm.Heading6Command,
        };
        for (int i = 0; i < headingCommands.Length; i++)
            headings.Items.Add(MenuItemOf($"H{i + 1}  {new string('#', i + 1)}", headingCommands[i]));
        menu.Items.Add(headings);
        menu.Items.Add(MenuItemOf("加粗  **文字**", _vm.BoldCommand));
        menu.Items.Add(MenuItemOf("斜体  *文字*", _vm.ItalicCommand));
        menu.Items.Add(MenuItemOf("行内代码  `代码`", _vm.InlineCodeCommand));
        menu.Items.Add(MenuItemOf("删除线  ~~文字~~", _vm.StrikeCommand));
        menu.Items.Add(MenuItemOf("代码块  ```", _vm.CodeBlockCommand));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuItemOf("无序列表  - ", _vm.UnorderedListCommand));
        menu.Items.Add(MenuItemOf("有序列表  1. ", _vm.OrderedListCommand));
        menu.Items.Add(MenuItemOf("引用  > ", _vm.QuoteCommand));
        menu.Items.Add(new Separator());

        // ===== 外围标签子菜单：顶部过滤输入框 + 过滤后的标签短名列表（超长可滚动） =====
        var tags = new MenuItem { Header = "外围标签" };
        var tagFilterBox = new TextBox { Width = 150, Margin = new Thickness(2, 3, 6, 3) };
        var tagFilterItem = new MenuItem
        {
            // 内嵌输入框：点击输入框时子菜单保持打开
            Header = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "标签：", VerticalAlignment = VerticalAlignment.Center },
                    tagFilterBox,
                },
            },
            Focusable = false,
            StaysOpenOnClick = true,
        };
        tags.Items.Add(tagFilterItem);
        tags.Items.Add(new Separator());

        void RebuildTagMenuItems()
        {
            // 保留前两项（过滤框 + 分隔线），其余按过滤条件重建
            for (int i = tags.Items.Count - 1; i >= 2; i--)
                tags.Items.RemoveAt(i);

            var text = (tagFilterBox.Text ?? "").Trim();
            var filtered = string.IsNullOrEmpty(text)
                ? _vm.QuickTags
                : _vm.QuickTags.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

            int count = 0;
            foreach (var tag in filtered)
            {
                var t = tag;
                tags.Items.Add(new MenuItem
                {
                    Header = t.Name,
                    Command = new RelayCommand(() =>
                    {
                        if (_vm.ActiveDocument is not null) EditorOps.WrapWithTag(_vm.ActiveDocument, t);
                    })
                });
                count++;
            }
            if (count == 0)
                tags.Items.Add(new MenuItem { Header = "(无匹配标签)", IsEnabled = false });
        }

        RebuildTagMenuItems();
        tagFilterBox.TextChanged += (_, _) => RebuildTagMenuItems();
        // 输入框内按 ↓ 跳到第一个标签项
        tagFilterBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Down) return;
            var first = tags.Items.OfType<MenuItem>().Skip(2).FirstOrDefault(i => i.IsEnabled);
            first?.Focus();
            e.Handled = true;
        };
        // 子菜单每次展开都重建（标签库可能已修改）并聚焦过滤框
        tags.SubmenuOpened += (_, _) =>
        {
            tagFilterBox.Text = "";
            RebuildTagMenuItems();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => tagFilterBox.Focus());
        };
        menu.Items.Add(tags);
        menu.Items.Add(MenuItemOf("移除当前外围标签", _vm.RemoveTagCommand));

        return menu;
    }

    private static MenuItem MenuItemOf(string header, ICommand? command) => new()
    {
        Header = header,
        Command = command
    };

    /// <summary>点击带 ▾ 的工具栏按钮时在其下方弹出菜单。</summary>
    private void ShowToolbarDropdown(Button button, params object[] items)
    {
        var menu = new ContextMenu();
        foreach (var it in items) menu.Items.Add(it);
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnHeadingMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        ShowToolbarDropdown(b,
            MenuItemOf("标题 1（#）", _vm.Heading1Command),
            MenuItemOf("标题 2（##）", _vm.Heading2Command),
            MenuItemOf("标题 3（###）", _vm.Heading3Command),
            MenuItemOf("标题 4（####）", _vm.Heading4Command),
            MenuItemOf("标题 5（#####）", _vm.Heading5Command),
            MenuItemOf("标题 6（######）", _vm.Heading6Command));
    }

    private void OnListMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        ShowToolbarDropdown(b,
            MenuItemOf("无序列表（- ）", _vm.UnorderedListCommand),
            MenuItemOf("有序列表（1. ）", _vm.OrderedListCommand),
            MenuItemOf("引用（> ）", _vm.QuoteCommand));
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) { }

    // ---------- 查找高亮 ----------

    private void OnMatchesChanged(IReadOnlyList<(int Start, int Length)> matches, int current)
    {
        if (_vm.ActiveDocument is not DocumentViewModel vm) return;
        var renderer = vm.MatchRenderer;
        renderer.SetMatches(matches, current);
        vm.Editor.TextArea.TextView.Redraw();
    }

    // ---------- 快捷键 ----------

    private void OnGlobalPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+数字：Markdown H1~H6 切换
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D6)
        {
            int level = e.Key - Key.D1 + 1;
            if (_vm.ActiveDocument is not null)
            {
                EditorOps.ApplyHeadingLevel(_vm.ActiveDocument, level);
                e.Handled = true;
            }
            return;
        }

        // Tab / Shift+Tab：列表缩进或默认插入
        if (e.Key == Key.Tab && _vm.ActiveDocument is DocumentViewModel docVm &&
            e.OriginalSource is DependencyObject d && IsInsideEditor(d))
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                EditorOps.IndentSelection(_vm.ActiveDocument, indent: false);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None &&
                     EditorOps.IndentSelection(_vm.ActiveDocument, indent: true))
            {
                e.Handled = true; // 是列表行 → 已缩进
            }
            return;
        }

        // Ctrl+G / Ctrl+P 已由 InputBinding 处理；Ctrl+W 关闭标签
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            if (_vm.CloseTabCanExecute())
            {
                _vm.CloseTabCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private static bool IsInsideEditor(DependencyObject d)
    {
        for (var cur = d; cur != null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
            if (cur is ICSharpCode.AvalonEdit.Editing.TextArea)
                return true;
        return false;
    }

    private void OnGlobalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+滚轮缩放字号，Ctrl+0 复位
        if (Keyboard.Modifiers == ModifierKeys.Control && _vm.ActiveDocument is DocumentViewModel vm)
        {
            double delta = e.Delta > 0 ? 2 : -2;
            double newSize = Math.Clamp(vm.Editor.FontSize + delta, 8, 72);
            vm.Editor.FontSize = newSize;
            e.Handled = true;
        }
    }

    // ---------- 标签栏交互 ----------

    private Point _tabDragStart;
    private object? _tabDragItem;

    private void OnTabPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            if (e.OriginalSource is FrameworkElement { DataContext: DocumentViewModel vm })
                _vm.CloseTab(vm);
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left)
        {
            _tabDragStart = e.GetPosition(TabList);
            _tabDragItem = null;
        }
    }

    private void OnTabMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(TabList);
        if (_tabDragItem is null)
        {
            if (Math.Abs(pos.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            if (e.OriginalSource is FrameworkElement { DataContext: DocumentViewModel vm })
            {
                _tabDragItem = vm;
                DragDrop.DoDragDrop(TabList, new DataObject("tab", vm), DragDropEffects.Move);
            }
        }
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("tab") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("tab") is DocumentViewModel vm)
        {
            int newIndex = GetCurrentTabIndex(e.GetPosition(TabList));
            _vm.MoveTab(vm, newIndex);
        }
        _tabDragItem = null;
    }

    private int GetCurrentTabIndex(Point pos)
    {
        int index = 0;
        foreach (var item in TabList.Items)
        {
            if (TabList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem lbi &&
                lbi.TranslatePoint(new Point(lbi.ActualWidth / 2, 0), TabList).X > pos.X)
                return index;
            index++;
        }
        return _vm.Tabs.Count - 1;
    }

    private void OnTabCloseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocumentViewModel vm })
            _vm.CloseTab(vm);
    }

    private void OnTabOverflowClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var t in _vm.Tabs)
        {
            var mi = new MenuItem { Header = t.DisplayTitleWithSuffix, Tag = t };
            mi.Click += (_, _) => _vm.ActiveDocument = t;
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = (Button)sender;
        menu.IsOpen = true;
    }

    // ---------- 工具栏 ----------

    // ---------- 工具栏：标签过滤选择 ----------

    /// <summary>初始化工具栏标签下拉：可输入过滤（包含匹配，忽略大小写）。</summary>
    private void InitQuickTagCombo()
    {
        QuickTagCombo.ItemsSource = _vm.QuickTags.ToList();
        // 可编辑 ComboBox 的文本输入事件挂在内部 TextBox 上
        QuickTagCombo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(OnQuickTagFilterChanged));
        QuickTagCombo.DropDownOpened += (_, _) =>
        {
            // 无过滤词时恢复完整列表（标签库可能已修改）
            if (string.IsNullOrWhiteSpace(QuickTagCombo.Text))
                QuickTagCombo.ItemsSource = _vm.QuickTags.ToList();
        };
    }

    private void OnQuickTagFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (QuickTagCombo.ItemsSource is null) return;
        var text = (QuickTagCombo.Text ?? "").Trim();
        QuickTagCombo.ItemsSource = string.IsNullOrEmpty(text)
            ? _vm.QuickTags.ToList()
            : _vm.QuickTags.Where(t => t.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        // 输入时保持下拉展开，展示过滤结果
        if (!QuickTagCombo.IsDropDownOpen)
            QuickTagCombo.IsDropDownOpen = true;
    }

    private void OnQuickTagSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (sender is ComboBox { SelectedItem: PromptEditorLib.Tags.TagDefinition tag })
        {
            // 触发包裹后清空过滤、还原完整列表，便于连续使用
            Dispatcher.BeginInvoke(() =>
            {
                EditorOps.WrapWithTag(vm.ActiveDocument!, tag);
                QuickTagCombo.SelectedItem = null;
                QuickTagCombo.Text = "";
                QuickTagCombo.ItemsSource = vm.QuickTags.ToList();
            });
        }
    }

    private void OnTableButton(object sender, RoutedEventArgs e)
    {
        var dlg = new Views.TableDialog { Owner = this };
        if (dlg.ShowDialog() == true && _vm.ActiveDocument is not null)
            EditorOps.InsertTableTemplate(_vm.ActiveDocument, dlg.Rows, dlg.Cols, dlg.Headers);
    }

    private void OnEolMarkersToggle(object sender, RoutedEventArgs e)
    {
        _vm.ToggleEolMarkers();
    }

    private void OnFindBarClose(object sender, RoutedEventArgs e) => FindBar.Visibility = Visibility.Collapsed;

    private void OnFindTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.FindNextCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            FindBar.Visibility = Visibility.Collapsed;
            _vm.ActiveDocument?.Editor.Focus();
            e.Handled = true;
        }
    }

    // ---------- 大纲 ----------

    private void OnOutlineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (OutlineList.SelectedItem is OutlineRow row)
            _vm.JumpToOutline(row);
    }

    // ---------- 文件拖放 ----------

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _vm.OpenPaths(files);
    }

    // ---------- 关闭保护 ----------

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 保存窗口状态
        var s = App.Settings;
        if (WindowState == WindowState.Normal)
        {
            s.WindowX = Left;
            s.WindowY = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.WindowMaximized = WindowState == WindowState.Maximized;

        var unsaved = _vm.Tabs.Where(t => t.IsModified).ToList();
        if (unsaved.Count == 0) return;

        if (unsaved.Count == 1)
        {
            var doc = unsaved[0];
            var result = MessageBox.Show(
                $"“{doc.DisplayTitle}” 有未保存的修改，是否保存？",
                "关闭确认", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            switch (result)
            {
                case MessageBoxResult.Yes:
                    if (!_vm.SaveDocument(doc)) { e.Cancel = true; return; }
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    return;
            }
        }
        else
        {
            var dlg = new Views.UnsavedFilesWindow(unsaved) { Owner = this };
            dlg.ShowDialog();
            switch (dlg.Decision)
            {
                case MultiCloseDecision.SaveAll:
                    foreach (var doc in unsaved)
                        if (!_vm.SaveDocument(doc)) { e.Cancel = true; return; }
                    break;
                case MultiCloseDecision.Cancel:
                    e.Cancel = true;
                    return;
            }
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            App.Settings.WindowX = Left;
            App.Settings.WindowY = Top;
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        App.Settings.WindowMaximized = WindowState == WindowState.Maximized;
    }

    private void OnWindowClosingImpl(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 退出前的多文档未保存保护
        var unsaved = _vm.Tabs.Where(t => t.IsModified).ToList();
        if (unsaved.Count > 1)
        {
            var dlg = new Views.UnsavedFilesWindow(unsaved) { Owner = this };
            dlg.ShowDialog();
            switch (dlg.Decision)
            {
                case MultiCloseDecision.SaveAll:
                    foreach (var doc in unsaved)
                        if (!_vm.SaveDocument(doc)) { e.Cancel = true; return; }
                    break;
                case MultiCloseDecision.Cancel:
                    e.Cancel = true;
                    return;
            }
        }
    }
}
