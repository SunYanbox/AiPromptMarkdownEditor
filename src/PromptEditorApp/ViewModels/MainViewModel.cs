using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PromptEditorLib.Markdown;
using PromptEditorLib.Tags;
using PromptEditorLib.Text;
using PromptEditorLib.Tabs;
using PromptEditor.Services;

namespace PromptEditor.ViewModels;

/// <summary>大纲面板行（标题按级别缩进，标签平级显示）。</summary>
public sealed record OutlineRow
{
    public int Line { get; init; }
    public string Display { get; init; } = "";
    public System.Windows.Thickness IndentMargin { get; init; }

    public OutlineRow(OutlineItem item)
    {
        Line = item.Line;
        Display = item.Display;
        IndentMargin = new System.Windows.Thickness(item.IsTag ? 24 : (item.Level - 1) * 16, 0, 0, 0);
    }
}

/// <summary>关闭有未保存修改标签时的用户选择。</summary>
public enum CloseDecision { Save, Discard, Cancel }

/// <summary>多文档未保存对话框的结果。</summary>
public enum MultiCloseDecision { SaveAll, DiscardAll, Cancel }

/// <summary>
/// 主视图模型：多标签页、文件、查找替换、状态栏、设置等。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly string _untitledBase = "未命名.md";

    public ObservableCollection<DocumentViewModel> Tabs { get; } = new();

    private DocumentViewModel? _activeDocument;
    /// <summary>激活标签（public setter 供 TabList SelectedItem 双向绑定使用）。</summary>
    public DocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (SetProperty(ref _activeDocument, value))
            {
                OnActiveDocumentChanged();
            }
        }
    }

    /// <summary>激活标签已切换（MainWindow 把对应编辑器实例放入内容宿主）。</summary>
    public event Action<DocumentViewModel?>? ActiveEditorChanged;

    /// <summary>大纲/结构刷新请求。</summary>
    public event Action<DocumentViewModel?>? StructureRefreshRequested;

    public ObservableCollection<OutlineRow> OutlineItems { get; } = new();

    // ---------- 标签库 ----------

    private TagLibrary _tagLibrary;
    public TagLibrary TagLibrary
    {
        get => _tagLibrary;
        private set
        {
            _tagLibrary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QuickTags));
        }
    }

    public IEnumerable<TagDefinition> QuickTags => TagLibrary.OrderedForMenu();

    // ---------- 状态栏 ----------

    private int _caretLine = 1, _caretColumn = 1;
    public int CaretLine { get => _caretLine; set => SetProperty(ref _caretLine, value); }
    public int CaretColumn { get => _caretColumn; set => SetProperty(ref _caretColumn, value); }

    private int _selectedChars, _selectedLines;
    public int SelectedChars { get => _selectedChars; set => SetProperty(ref _selectedChars, value); }
    public int SelectedLines { get => _selectedLines; set => SetProperty(ref _selectedLines, value); }

    private int _totalChars, _totalLines;
    public int TotalChars { get => _totalChars; set => SetProperty(ref _totalChars, value); }
    public int TotalLines { get => _totalLines; set => SetProperty(ref _totalLines, value); }

    private string _eolDisplay = "LF";
    public string EolDisplay { get => _eolDisplay; set => SetProperty(ref _eolDisplay, value); }

    private string _encodingDisplay = "UTF-8";
    public string EncodingDisplay { get => _encodingDisplay; set => SetProperty(ref _encodingDisplay, value); }

    private string _tokensDisplay = "0 token（估算值）";
    public string TokensDisplay { get => _tokensDisplay; set => SetProperty(ref _tokensDisplay, value); }

    private string _tagErrorDisplay = "";
    public string TagErrorDisplay { get => _tagErrorDisplay; set => SetProperty(ref _tagErrorDisplay, value); }

    // 状态栏分段显示（供 XAML 绑定）
    public string StatusCaret => $"行 {CaretLine}，列 {CaretColumn}";
    public string StatusSelection => SelectedChars > 0
        ? $"已选 {SelectedChars} 字符（{SelectedLines} 行）"
        : "未选择";
    public string StatusCounts => $"共 {TotalChars} 字符，{TotalLines} 行";
    public string StatusEncoding => EncodingDisplay;
    public string StatusTokens => TokensDisplay;
    public string StatusTagError => TagErrorDisplay;

    private string _statusMessage = "";
    /// <summary>状态栏消息（保存成功/失败等），数秒后自动清除。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(IsStatusMessageVisible));
                OnPropertyChanged(nameof(IsStatusMessageError));
            }
        }
    }
    public bool IsStatusMessageVisible => StatusMessage.Length > 0;
    public bool IsStatusMessageError => StatusMessage.StartsWith("保存失败", StringComparison.Ordinal);

    /// <summary>在状态栏显示一条消息，5 秒后自动消失。以「保存失败」开头的消息按错误色显示。</summary>
    public void ShowStatusMessage(string message)
    {
        StatusMessage = message;
        _statusMessageTimer ??= CreateStatusMessageTimer();
        _statusMessageTimer.Stop();
        _statusMessageTimer.Start();
    }

    private DispatcherTimer? _statusMessageTimer;

    private DispatcherTimer CreateStatusMessageTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        t.Tick += (_, _) => { t.Stop(); StatusMessage = ""; };
        return t;
    }

    private void NotifyStatusDisplay()
    {
        OnPropertyChanged(nameof(StatusCaret));
        OnPropertyChanged(nameof(StatusSelection));
        OnPropertyChanged(nameof(StatusCounts));
        OnPropertyChanged(nameof(StatusEncoding));
        OnPropertyChanged(nameof(StatusTokens));
        OnPropertyChanged(nameof(StatusTagError));
    }

    private string _titleSuffix = "";
    public string TitleSuffix { get => _titleSuffix; set { SetProperty(ref _titleSuffix, value); OnPropertyChanged(nameof(MainTitle)); } }

    public string MainTitle =>
        (ActiveDocument is null ? "AI 提示词 Markdown 编辑器" : ActiveDocument.ToString()) + _titleSuffix;

    // ---------- 查找 / 替换 ----------

    private string _findText = "";
    public string FindText { get => _findText; set { SetProperty(ref _findText, value); ScheduleMatchRefresh(); } }

    private string _replaceText = "";
    public string ReplaceText { get => _replaceText; set => SetProperty(ref _replaceText, value); }

    private bool _matchCase;
    public bool MatchCase { get => _matchCase; set { SetProperty(ref _matchCase, value); ScheduleMatchRefresh(); } }

    private bool _wholeWord;
    public bool WholeWord { get => _wholeWord; set { SetProperty(ref _wholeWord, value); ScheduleMatchRefresh(); } }

    private bool _useRegex;
    public bool UseRegex { get => _useRegex; set { SetProperty(ref _useRegex, value); ScheduleMatchRefresh(); } }

    private bool _isFindBarOpen;
    public bool IsFindBarOpen
    {
        get => _isFindBarOpen;
        set
        {
            if (SetProperty(ref _isFindBarOpen, value))
            {
                if (value) ShowFindBar?.Invoke();
                else HideFindBar?.Invoke();
            }
        }
    }

    /// <summary>查找栏显隐与窗口关闭（MainWindow 订阅）。</summary>
    public event Action? ShowFindBar;
    public event Action? HideFindBar;
    public event Action? CloseWindow;

    private readonly List<(int Start, int Length)> _currentMatches = new();
    private int _currentMatchIndex = -1;
    private DispatcherTimer? _matchRefreshTimer;

    /// <summary>查找匹配变化（MainWindow 据此更新高亮渲染器）。</summary>
    public event Action<IReadOnlyList<(int Start, int Length)>, int>? MatchesChanged;

    // ---------- 重新打开已关闭标签 ----------

    private readonly Stack<string> _closedFileStack = new();

    public MainViewModel()
    {
        _tagLibrary = LoadTagLibrary();

        NewTabCommand = new RelayCommand(() => CreateNewTab());
        OpenCommand = new RelayCommand(() => OpenFiles());
        SaveCommand = new RelayCommand(() => SaveActive(SaveMode.Save), () => ActiveDocument != null);
        SaveAsCommand = new RelayCommand(() => SaveActive(SaveMode.SaveAs), () => ActiveDocument != null);
        SaveAllCommand = new RelayCommand(() => SaveAll(), () => Tabs.Any(t => t.IsModified));
        CloseTabCommand = new RelayCommand(() => CloseTab(ActiveDocument), () => ActiveDocument != null);
        ReopenClosedTabCommand = new RelayCommand(ReopenClosedTab, () => _closedFileStack.Count > 0);
        NextTabCommand = new RelayCommand(() => SwitchTab(1));
        PrevTabCommand = new RelayCommand(() => SwitchTab(-1));
        UndoCommand = new RelayCommand(() => ActiveDocument?.Document.UndoStack.Undo(), () => ActiveDocument?.Document.UndoStack.CanUndo == true);
        RedoCommand = new RelayCommand(() => ActiveDocument?.Document.UndoStack.Redo(), () => ActiveDocument?.Document.UndoStack.CanRedo == true);
        FindCommand = new RelayCommand(ShowFind);
        ReplaceCommand = new RelayCommand(ShowReplace);
        FindNextCommand = new RelayCommand(() => FindNext(backward: false));
        FindPrevCommand = new RelayCommand(() => FindNext(backward: true));
        ReplaceNextCommand = new RelayCommand(ReplaceNext);
        ReplaceAllCommand = new RelayCommand(ReplaceAll);
        CloseFindBarCommand = new RelayCommand(() => { IsFindBarOpen = false; _currentMatches.Clear(); MatchesChanged?.Invoke(_currentMatches, -1); });
        GotoLineCommand = new RelayCommand(GotoLineDialog);
        QuickJumpCommand = new RelayCommand(QuickJumpDialog);
        FoldToggleCommand = new RelayCommand(ToggleFoldAtCaret, () => ActiveDocument != null);
        FoldAllCommand = new RelayCommand(() => SetAllFolds(true));
        UnfoldAllCommand = new RelayCommand(() => SetAllFolds(false));
        UnifyLfCommand = new RelayCommand(() => UnifyLineEndings(LineEnding.Lf));
        UnifyCrlfCommand = new RelayCommand(() => UnifyLineEndings(LineEnding.Crlf));
        ToggleEolMarkersCommand = new RelayCommand(ToggleEolMarkers);
        ToggleOutlineCommand = new RelayCommand(() => IsOutlineOpen = !IsOutlineOpen);
        SettingsCommand = new RelayCommand(OpenSettings);
        TagLibraryCommand = new RelayCommand(OpenTagLibrary);
        CopyPathCommand = new RelayCommand(CopyActivePath, () => ActiveDocument?.FilePath != null);
        RevealInExplorerCommand = new RelayCommand(RevealActiveInExplorer, () => ActiveDocument?.FilePath != null);
        CloseOthersCommand = new RelayCommand(() => CloseOthers(ActiveDocument), () => ActiveDocument != null);
        CloseRightCommand = new RelayCommand(() => CloseRight(ActiveDocument), () => ActiveDocument != null);
        RemoveTagCommand = new RelayCommand(RemoveTagAtCaret);
        OutlineJumpCommand = new RelayCommand(p => { if (p is OutlineRow row) JumpToOutline(row); });
        Heading1Command = new RelayCommand(() => ApplyHeading(1));
        Heading2Command = new RelayCommand(() => ApplyHeading(2));
        Heading3Command = new RelayCommand(() => ApplyHeading(3));
        Heading4Command = new RelayCommand(() => ApplyHeading(4));
        Heading5Command = new RelayCommand(() => ApplyHeading(5));
        Heading6Command = new RelayCommand(() => ApplyHeading(6));
        BoldCommand = new RelayCommand(ApplyBold);
        ItalicCommand = new RelayCommand(() => ApplyInlineMarker("*"));
        InlineCodeCommand = new RelayCommand(() => ApplyInlineMarker("`"));
        StrikeCommand = new RelayCommand(() => ApplyInlineMarker("~~"));
        CodeBlockCommand = new RelayCommand(ApplyCodeBlock);
        QuoteCommand = new RelayCommand(ApplyQuote);
        UnorderedListCommand = new RelayCommand(ApplyUnorderedList);
        OrderedListCommand = new RelayCommand(ApplyOrderedList);
        ResetZoomCommand = new RelayCommand(() => ActiveDocument?.ResetZoom());
        TagPickerCommand = new RelayCommand(ShowTagPicker);

        RebuildRecentFileCommands();
        if (Tabs.Count == 0) CreateNewTab(activate: false);
    }

    public RelayCommand Heading1Command { get; }
    public RelayCommand Heading2Command { get; }
    public RelayCommand Heading3Command { get; }
    public RelayCommand Heading4Command { get; }
    public RelayCommand Heading5Command { get; }
    public RelayCommand Heading6Command { get; }
    public RelayCommand BoldCommand { get; }
    public RelayCommand ItalicCommand { get; }
    public RelayCommand InlineCodeCommand { get; }
    public RelayCommand StrikeCommand { get; }
    public RelayCommand CodeBlockCommand { get; }
    public RelayCommand QuoteCommand { get; }
    public RelayCommand UnorderedListCommand { get; }
    public RelayCommand OrderedListCommand { get; }
    public RelayCommand ResetZoomCommand { get; }
    public RelayCommand ToggleFoldCommand => FoldToggleCommand;
    public RelayCommand TagPickerCommand { get; }

    private void ApplyHeading(int level) => EditorOps.ApplyHeadingLevel(ActiveDocument, level);
    private void ApplyBold() => EditorOps.ToggleInlineMarker(ActiveDocument, "**");
    private void ApplyInlineMarker(string marker) => EditorOps.ToggleInlineMarker(ActiveDocument, marker);
    private void ApplyCodeBlock() => EditorOps.ToggleCodeBlock(ActiveDocument);
    private void ApplyQuote() => EditorOps.ToggleQuote(ActiveDocument);
    private void ApplyUnorderedList() => EditorOps.ToggleUnorderedList(ActiveDocument);
    private void ApplyOrderedList() => EditorOps.ToggleOrderedList(ActiveDocument);

    private void ShowTagPicker()
    {
        if (ActiveDocument is null) return;
        var dlg = new Views.TagPickerWindow(TagLibrary) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true && dlg.SelectedTag is not null)
        {
            EditorOps.WrapWithTag(ActiveDocument, dlg.SelectedTag);
        }
    }

    /// <summary>确保至少存在一个标签（关闭最后一个时自动新建未命名.md）。</summary>
    public void EnsureAtLeastOneTab()
    {
        if (Tabs.Count == 0) CreateNewTab();
    }

    public bool CloseTabCanExecute() => ActiveDocument != null;

    // ---------- 命令 ----------

    public RelayCommand NewTabCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand CloseTabCommand { get; }
    public RelayCommand ReopenClosedTabCommand { get; }
    public RelayCommand NextTabCommand { get; }
    public RelayCommand PrevTabCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand FindCommand { get; }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand FindNextCommand { get; }
    public RelayCommand FindPrevCommand { get; }
    public RelayCommand ReplaceNextCommand { get; }
    public RelayCommand ReplaceAllCommand { get; }
    public RelayCommand CloseFindBarCommand { get; }
    public RelayCommand GotoLineCommand { get; }
    public RelayCommand QuickJumpCommand { get; }
    public RelayCommand FoldToggleCommand { get; }
    public RelayCommand FoldAllCommand { get; }
    public RelayCommand UnfoldAllCommand { get; }
    public RelayCommand UnifyLfCommand { get; }
    public RelayCommand UnifyCrlfCommand { get; }
    public RelayCommand ToggleEolMarkersCommand { get; }
    public RelayCommand ToggleOutlineCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand TagLibraryCommand { get; }
    public RelayCommand CopyPathCommand { get; }
    public RelayCommand RevealInExplorerCommand { get; }
    public RelayCommand CloseOthersCommand { get; }
    public RelayCommand CloseRightCommand { get; }
    public RelayCommand RemoveTagCommand { get; }
    public RelayCommand OutlineJumpCommand { get; }

    private bool _isOutlineOpen = true;
    public bool IsOutlineOpen { get => _isOutlineOpen; set => SetProperty(ref _isOutlineOpen, value); }

    // ---------- 标签页管理 ----------

    /// <summary>新建空标签（至少保留一个标签）。</summary>
    public DocumentViewModel CreateNewTab(bool activate = true)
    {
        var vm = new DocumentViewModel(App.Settings);
        vm.IsModified = false;
        vm.UntitledName = NextUntitledName();
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(DocumentViewModel.DisplayTitle) or nameof(DocumentViewModel.DisplaySuffix))
                OnPropertyChanged(nameof(MainTitle));
        };
        vm.StatusChanged += OnDocStatusChanged;
        vm.StructureChanged += OnDocStructureChanged;
        Tabs.Add(vm);
        RecomputeUntitledNames();
        RecomputeDisplayTitles();
        if (activate) ActiveDocument = vm;
        return vm;
    }

    /// <summary>打开文件（可多选），已有同路径标签则激活（路径不区分大小写）。</summary>
    public void OpenFiles(string? initialDir = null)
    {
        var dlg = new OpenFileDialog
        {
            Title = "打开文件",
            Filter = "Markdown / 文本文件|*.md;*.markdown;*.txt;*.prompt|所有文件|*.*",
            Multiselect = true,
            InitialDirectory = initialDir ?? (Path.GetDirectoryName(ActiveDocument?.FilePath) is string d && Directory.Exists(d) ? d : null),
        };
        if (dlg.ShowDialog() != true) return;
        OpenPaths(dlg.FileNames);
    }

    public void OpenPaths(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var normalized = Path.GetFullPath(path);
            var existing = Tabs.FirstOrDefault(t =>
                t.FilePath is not null &&
                string.Equals(t.FilePath, normalized, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveDocument = existing;
                continue;
            }

            DocumentViewModel vm;
            try
            {
                var result = FileService.OpenText(normalized);
                vm = new DocumentViewModel(App.Settings, result.Text);
                vm.FilePath = normalized;
                vm.Encoding = result.Encoding;
                vm.IsModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开文件：\n{normalized}\n\n{ex.Message}", "打开失败",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                continue;
            }

            AttachDoc(vm);
            Tabs.Add(vm);
            AddRecent(normalized);
            ActiveDocument = vm;
        }
        RecomputeDisplayTitles();
    }

    private void AttachDoc(DocumentViewModel vm)
    {
        vm.StatusChanged += OnDocStatusChanged;
        vm.StructureChanged += OnDocStructureChanged;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(DocumentViewModel.DisplayTitle) or nameof(DocumentViewModel.DisplaySuffix))
                OnPropertyChanged(nameof(MainTitle));
        };
    }

    /// <summary>关闭标签（含未保存保护）。forceClose 时用于窗口退出场景。</summary>
    public bool CloseTab(DocumentViewModel? vm)
    {
        if (vm is null) return true;
        if (vm.IsModified)
        {
            var decision = ConfirmClose(vm);
            if (decision == CloseDecision.Cancel) return false;
            if (decision == CloseDecision.Save && !SaveDocument(vm)) return false;
        }

        int index = Tabs.IndexOf(vm);
        Tabs.Remove(vm);
        if (vm.FilePath is not null)
            _closedFileStack.Push(vm.FilePath);

        if (Tabs.Count == 0)
        {
            CreateNewTab(); // 始终保留至少一个标签
        }
        else if (ActiveDocument == vm)
        {
            ActiveDocument = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
        RecomputeUntitledNames();
        RecomputeDisplayTitles();
        return true;
    }

    public void CloseOthers(DocumentViewModel? keep)
    {
        if (keep is null) return;
        foreach (var t in Tabs.Where(t => t != keep).ToList())
        {
            if (!CloseTab(t)) break;
        }
    }

    public void CloseRight(DocumentViewModel? anchor)
    {
        if (anchor is null) return;
        foreach (var t in Tabs.Skip(Tabs.IndexOf(anchor) + 1).ToList())
        {
            if (!CloseTab(t)) break;
        }
    }

    /// <summary>关闭窗口：列出全部未保存文档（全部保存 / 全部放弃 / 取消）。</summary>
    public bool ConfirmCloseWindow()
    {
        var unsaved = Tabs.Where(t => t.IsModified).ToList();
        if (unsaved.Count == 0) return true;

        if (unsaved.Count == 1)
        {
            var decision = ConfirmClose(unsaved[0]);
            if (decision == CloseDecision.Save) return SaveDocument(unsaved[0]);
            return decision == CloseDecision.Discard;
        }

        var multi = new Views.UnsavedFilesWindow(unsaved) { Owner = Application.Current.MainWindow };
        var result = multi.ShowDialog();
        if (result != true) return false;
        switch (multi.Decision)
        {
            case MultiCloseDecision.SaveAll:
                foreach (var t in unsaved)
                    if (!SaveDocument(t)) return false;
                return true;
            case MultiCloseDecision.DiscardAll:
                return true;
            default:
                return false;
        }
    }

    private CloseDecision ConfirmClose(DocumentViewModel vm)
    {
        var result = MessageBox.Show(
            $"「{vm.DisplayTitle}」有未保存的修改。\n\n是否保存？",
            "关闭标签", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => CloseDecision.Save,
            MessageBoxResult.No => CloseDecision.Discard,
            _ => CloseDecision.Cancel,
        };
    }

    public void SwitchTab(int delta)
    {
        if (Tabs.Count == 0 || ActiveDocument is null) return;
        int idx = (Tabs.IndexOf(ActiveDocument) + delta % Tabs.Count + Tabs.Count) % Tabs.Count;
        ActiveDocument = Tabs[idx];
    }

    public void ActivateTab(DocumentViewModel vm) => ActiveDocument = vm;

    public void ReopenClosedTab()
    {
        if (_closedFileStack.Count == 0) return;
        string path = _closedFileStack.Pop();
        if (File.Exists(path)) OpenPaths([path]);
    }

    public void MoveTab(DocumentViewModel vm, int newIndex)
    {
        int oldIndex = Tabs.IndexOf(vm);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Tabs.Count || oldIndex == newIndex) return;
        Tabs.Move(oldIndex, newIndex);
    }

    // ---------- 命名 ----------

    /// <summary>未命名标签编号：未命名.md、未命名2.md…取当前未被占用的最小编号。</summary>
    private string NextUntitledName()
    {
        var used = new HashSet<string>(Tabs.Select(t => t.BaseDisplayName), StringComparer.Ordinal);
        if (!used.Contains(_untitledBase)) return _untitledBase;
        for (int i = 2; ; i++)
        {
            var name = $"未命名{i}.md";
            if (!used.Contains(name)) return name;
        }
    }

    /// <summary>新建/关闭后重算未命名标签名（编号取最小编号）。</summary>
    private void RecomputeUntitledNames()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Tabs.Where(t => t.FilePath is null))
        {
            // 按现有顺序重排未命名编号
        }
        // 简化实现：只在有未命名标签时基于当前占用情况调整（避免打扰用户命名习惯，只在冲突时重编号）
        int candidate = 2;
        foreach (var t in Tabs.Where(t => t.FilePath is null).ToList())
        {
            if (t.UntitledName == _untitledBase) { used.Add(_untitledBase); continue; }
            if (used.Contains(t.UntitledName))
            {
                while (used.Contains($"未命名{candidate}.md")) candidate++;
                t.UntitledName = $"未命名{candidate}.md";
                used.Add(t.UntitledName);
            }
            else used.Add(t.UntitledName);
        }
        RecomputeDisplayTitles();
    }

    /// <summary>同名消歧重算：打开/关闭/另存为后调用，冲突消除后自动回缩为最短显示。</summary>
    public void RecomputeDisplayTitles()
    {
        var sources = Tabs.Select(t =>
            t.FilePath is not null
                ? TabNameSource.FromPath(t.Id, t.FilePath)
                : TabNameSource.Untitled(t.Id, t.UntitledName)).ToList();
        var names = DisplayNameDisambiguator.Compute(sources);
        foreach (var t in Tabs)
        {
            t.SetDisambiguatedName(names[t.Id]);
        }
        OnPropertyChanged(nameof(MainTitle));
    }

    // ---------- 保存 ----------

    private enum SaveMode { Save, SaveAs }

    private void SaveActive(SaveMode mode)
    {
        if (ActiveDocument is null) return;
        SaveDocument(ActiveDocument, mode == SaveMode.SaveAs);
    }

    public bool SaveDocument(DocumentViewModel vm, bool saveAs = false)
    {
        string? path = vm.FilePath;
        if (path is null || saveAs)
        {
            var dlg = new SaveFileDialog
            {
                Title = "另存为",
                Filter = "Markdown|*.md|文本文件|*.txt|所有文件|*.*",
                FileName = vm.BaseDisplayName,
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }

        try
        {
            FileService.SaveText(path, vm.Document.Text, vm.Encoding);
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"保存失败：{ex.Message}");
            MessageBox.Show($"保存失败：\n{path}\n\n{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        bool pathChanged = !string.Equals(vm.FilePath, path, StringComparison.OrdinalIgnoreCase);
        vm.FilePath = Path.GetFullPath(path);
        vm.IsModified = false;
        if (saveAs || pathChanged)
        {
            RecomputeDisplayTitles();
        }
        AddRecent(vm.FilePath);
        UpdateStatus(vm);
        ShowStatusMessage($"保存成功：{vm.FilePath}");
        return true;
    }

    public void SaveAll()
    {
        foreach (var t in Tabs.Where(t => t.IsModified).ToList())
            SaveDocument(t);
    }

    /// <summary>以指定编码另存（编码转换）。</summary>
    public bool SaveDocumentWithEncoding(DocumentViewModel vm, Encoding encoding)
    {
        var dlg = new SaveFileDialog
        {
            Title = "编码转换另存",
            Filter = "Markdown|*.md|文本文件|*.txt|所有文件|*.*",
            FileName = vm.BaseDisplayName,
        };
        if (dlg.ShowDialog() != true) return false;
        try
        {
            FileService.SaveText(dlg.FileName, vm.Document.Text, encoding);
        }
        catch (Exception ex)
        {
            ShowStatusMessage($"保存失败：{ex.Message}");
            MessageBox.Show($"保存失败：{ex.Message}", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        vm.Encoding = encoding;
        vm.FilePath = Path.GetFullPath(dlg.FileName);
        vm.IsModified = false;
        RecomputeDisplayTitles();
        UpdateStatus(vm);
        ShowStatusMessage($"保存成功：{vm.FilePath}");
        return true;
    }

    // ---------- 最近文件 ----------

    /// <summary>「文件 → 最近文件」菜单的条目集合（显示绝对路径，点击即打开）。</summary>
    public ObservableCollection<RecentFileCommand> RecentFileCommands { get; } = new();

    private void RebuildRecentFileCommands()
    {
        RecentFileCommands.Clear();
        if (App.Settings.RecentFiles.Count == 0)
        {
            // 空列表占位项（CanExecute=false 自动禁用）
            RecentFileCommands.Add(new RecentFileCommand("(暂无最近文件)", new RelayCommand(() => { }, () => false)));
            return;
        }
        foreach (var path in App.Settings.RecentFiles)
        {
            RecentFileCommands.Add(new RecentFileCommand(path, new RelayCommand(
                () =>
                {
                    if (!File.Exists(path))
                    {
                        ShowStatusMessage($"文件不存在：{path}");
                        MessageBox.Show($"文件不存在或已被移动：\n{path}", "打开失败",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    OpenPaths(new[] { path });
                })));
        }
    }

    private void AddRecent(string path)
    {
        var list = App.Settings.RecentFiles;
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > 15) list.RemoveRange(15, list.Count - 15);
        OnPropertyChanged(nameof(RecentFiles));
        RebuildRecentFileCommands();
    }

    public IReadOnlyList<string> RecentFiles => App.Settings.RecentFiles;

    // ---------- 撤销 / 重做 ----------

    public void Undo() => ActiveDocument?.Document.UndoStack.Undo();
    public void Redo() => ActiveDocument?.Document.UndoStack.Redo();

    // ---------- 标签库 ----------

    private static TagLibrary LoadTagLibrary()
    {
        try
        {
            if (File.Exists(SettingsStore.TagsPath))
                return TagLibrary.Load(SettingsStore.TagsPath);
        }
        catch
        {
            // 损坏则回退默认
        }
        return TagLibrary.CreateDefault();
    }

    public void ReloadTagLibrary()
    {
        TagLibrary = LoadTagLibrary();
    }

    public void SaveTagLibrary(TagLibrary library)
    {
        TagLibrary = library;
        try
        {
            library.Save(SettingsStore.TagsPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"标签库保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------- 状态 ----------

    private void OnActiveDocumentChanged()
    {
        ActiveEditorChanged?.Invoke(ActiveDocument);
        UpdateStatus(ActiveDocument);
        ScheduleStructureRefresh(ActiveDocument);
        OnPropertyChanged(nameof(MainTitle));
        RelayCommand.Refresh();
    }

    private void OnDocStatusChanged(DocumentViewModel vm)
    {
        if (vm != ActiveDocument) return;
        UpdateStatus(vm);
    }

    private void OnDocStructureChanged(DocumentViewModel vm)
    {
        if (vm != ActiveDocument) return;
        ScheduleMatchRefresh();
        ScheduleStructureRefresh(vm);
    }

    /// <summary>结构刷新（折叠区域 + 标签配对错误 + 大纲），后台解析不占 UI 线程。</summary>
    private DispatcherTimer? _structureTimer;

    private void ScheduleStructureRefresh(DocumentViewModel? vm)
    {
        if (vm is null) return;
        _structurePendingVm = vm;
        _structureTimer ??= CreateDebounceTimer(350, () =>
        {
            if (_structurePendingVm != null) RefreshStructure(_structurePendingVm);
        });
        _structureTimer.Stop();
        _structureTimer.Start();
    }

    private DocumentViewModel? _structurePendingVm;

    private async void RefreshStructure(DocumentViewModel vm)
    {
        string text = vm.Document.Text;
        var tags = TagLibrary.Tags;
        var owner = vm;

        var (folds, items, tagError) = await System.Threading.Tasks.Task.Run(() =>
        {
            var foldRegions = FoldingCalculator.Calculate(text, tags);
            var outline = OutlineBuilder.Build(text, tags);
            string error = TagPairScanner.FindUnpairedDescription(text, tags);
            return (foldRegions, outline, error);
        });

        // 期间可能已切换标签或文档继续变化：折叠仍可应用（按偏移），大纲仅限激活标签
        vm.UpdateFolding(folds);
        vm.TagErrorText = tagError;
        if (owner == ActiveDocument)
        {
            UpdateStatus(owner);
            OutlineItems.Clear();
            foreach (var item in items)
                OutlineItems.Add(new OutlineRow(item));
        }
    }

    public void UpdateStatus(DocumentViewModel? vm)
    {
        if (vm is null)
        {
            TotalChars = TotalLines = 0;
            EolDisplay = "-";
            EncodingDisplay = "-";
            TokensDisplay = "0 token（估算值）";
            TagErrorDisplay = "";
            NotifyStatusDisplay();
            return;
        }

        TotalChars = vm.Document.TextLength;
        TotalLines = vm.Document.LineCount;
        SelectedChars = vm.Editor.SelectionLength;
        SelectedLines = vm.Editor.SelectedText.Count(c => c == '\n') + (vm.Editor.SelectionLength > 0 ? 1 : 0);
        CaretLine = vm.Editor.TextArea.Caret.Line;
        CaretColumn = vm.Editor.TextArea.Caret.Column;

        var info = vm.LineEndingInfo;
        EolDisplay = info.Mixed ? "混合" : LineEndingInfo.NameOf(info.Dominant);
        EncodingDisplay = FileService.DisplayName(vm.Encoding);
        TokensDisplay = $"≈{TokenEstimator.Estimate(vm.Document.Text)} token（估算值）";
        TagErrorDisplay = vm.TagErrorText;
        NotifyStatusDisplay();
        RelayCommand.Refresh();
    }

    // ---------- 行尾 ----------

    private void UnifyLineEndings(LineEnding target)
    {
        if (ActiveDocument is null) return;
        EditorOps.UnifyLineEndings(ActiveDocument, target);
        UpdateStatus(ActiveDocument);
    }

    public void JumpToFirstMixedEol()
    {
        var vm = ActiveDocument;
        if (vm is null || !vm.LineEndingInfo.Mixed) return;
        vm.JumpToLine(vm.LineEndingInfo.FirstMixedLine);
    }

    public void ToggleEolMarkers()
    {
        App.Settings.ShowLineEndMarkers = !App.Settings.ShowLineEndMarkers;
        foreach (var t in Tabs)
            t.SetLineEndMarkersEnabled(App.Settings.ShowLineEndMarkers);
        OnPropertyChanged(nameof(EolMarkersEnabled));
    }

    public bool EolMarkersEnabled => App.Settings.ShowLineEndMarkers;

    // ---------- 折叠 ----------

    private void ToggleFoldAtCaret()
    {
        var vm = ActiveDocument;
        if (vm is null) return;
        vm.ToggleFoldAtOffset(vm.Editor.TextArea.Caret.Offset);
    }

    private void SetAllFolds(bool folded)
    {
        ActiveDocument?.SetAllFolds(folded);
    }

    // ---------- 大纲 ----------

    private static DispatcherTimer CreateDebounceTimer(int ms, Action tick)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); tick(); };
        return t;
    }

    public void JumpToOutline(OutlineRow row)
    {
        ActiveDocument?.JumpToLine(row.Line + 1);
    }

    // ---------- 跳转 ----------

    private void GotoLineDialog()
    {
        if (ActiveDocument is null) return;
        var dlg = new Views.GotoLineWindow(ActiveDocument.Document.LineCount) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
            ActiveDocument.JumpToLine(dlg.LineNumber);
    }

    private void QuickJumpDialog()
    {
        if (ActiveDocument is null) return;
        var dlg = new Views.QuickJumpWindow(this) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    // ---------- 查找 / 替换 ----------

    private void ShowFind()
    {
        IsFindBarOpen = true;
        // 以选区作为初始查找词
        var sel = ActiveDocument?.Editor.SelectedText;
        if (!string.IsNullOrEmpty(sel) && sel.Length < 200 && !sel.Contains('\n'))
            FindText = sel;
        ScheduleMatchRefresh();
    }

    private void ShowReplace()
    {
        ShowFind();
    }

    private System.Text.RegularExpressions.Regex? BuildFindRegex()
    {
        if (string.IsNullOrEmpty(FindText)) return null;
        string pattern = UseRegex ? FindText : System.Text.RegularExpressions.Regex.Escape(FindText);
        if (WholeWord)
            pattern = @"(?<![\w])(?:" + pattern + @")(?![\w])";
        try
        {
            return new System.Text.RegularExpressions.Regex(
                pattern,
                MatchCase ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch (ArgumentException)
        {
            return null; // 正则无效
        }
    }

    private void ScheduleMatchRefresh()
    {
        _matchRefreshTimer ??= CreateDebounceTimer(250, RefreshMatches);
        _matchRefreshTimer.Stop();
        _matchRefreshTimer.Start();
    }

    private void RefreshMatches()
    {
        _currentMatches.Clear();
        _currentMatchIndex = -1;
        var regex = BuildFindRegex();
        var doc = ActiveDocument?.Document;
        if (regex is not null && doc is not null)
        {
            try
            {
                foreach (System.Text.RegularExpressions.Match m in regex.Matches(doc.Text))
                {
                    if (m.Length == 0) continue;
                    _currentMatches.Add((m.Index, m.Length));
                    if (_currentMatches.Count >= 10000) break; // 防止极端情况下卡死
                }
            }
            catch (System.Text.RegularExpressions.RegexParseException)
            {
            }
        }
        MatchesChanged?.Invoke(_currentMatches, _currentMatchIndex);
    }

    private void FindNext(bool backward)
    {
        var vm = ActiveDocument;
        if (vm is null || _currentMatches.Count == 0) return;

        int caret = vm.Editor.TextArea.Caret.Offset;
        int idx;
        if (backward)
        {
            idx = _currentMatches.FindLastIndex(m => m.Start < caret);
            if (idx < 0) idx = _currentMatches.Count - 1; // 循环查找
        }
        else
        {
            idx = _currentMatches.FindIndex(m => m.Start + m.Length > caret);
            if (idx < 0) idx = 0; // 循环查找
        }
        _currentMatchIndex = idx;
        var (start, length) = _currentMatches[idx];
        vm.Editor.Select(start, length);
        vm.Editor.TextArea.Caret.BringCaretToView();
        MatchesChanged?.Invoke(_currentMatches, _currentMatchIndex);
    }

    private void ReplaceNext()
    {
        var vm = ActiveDocument;
        if (vm is null || _currentMatches.Count == 0) return;
        int caret = vm.Editor.TextArea.Caret.Offset;
        int idx = _currentMatches.FindIndex(m => m.Start + m.Length > caret);
        if (idx < 0) idx = 0;
        var (start, length) = _currentMatches[idx];
        string replacement = BuildReplacement(start, length);
        vm.Document.Replace(start, length, replacement);
        RefreshMatches();
        FindNext(backward: false);
    }

    private string BuildReplacement(int start, int length)
    {
        var regex = BuildFindRegex();
        if (regex is null) return ReplaceText ?? "";
        var m = regex.Match(ActiveDocument!.Document.Text.Substring(start, length));
        return m.Success ? m.Result(ReplaceText ?? "") : (ReplaceText ?? "");
    }

    private void ReplaceAll()
    {
        var vm = ActiveDocument;
        var regex = BuildFindRegex();
        if (vm is null || regex is null) return;

        string text = vm.Document.Text;
        var matches = new List<(int Index, int Length, string Repl)>();
        try
        {
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
            {
                if (m.Length == 0) continue;
                matches.Add((m.Index, m.Length, m.Result(ReplaceText ?? "")));
            }
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            return;
        }

        vm.Document.BeginUpdate();
        try
        {
            // 从后往前替换，避免偏移失效；BeginUpdate/EndUpdate 合并为单个撤销步骤
            for (int i = matches.Count - 1; i >= 0; i--)
                vm.Document.Replace(matches[i].Index, matches[i].Length, matches[i].Repl);
        }
        finally
        {
            vm.Document.EndUpdate();
        }
        RefreshMatches();
        UpdateStatus(vm);
    }

    // ---------- 路径操作 ----------

    private void CopyActivePath()
    {
        if (ActiveDocument?.FilePath is string path)
            Clipboard.SetText(path);
    }

    private void RevealActiveInExplorer()
    {
        if (ActiveDocument?.FilePath is not string path) return;
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch
        {
            // 忽略
        }
    }

    // ---------- 设置 / 标签库窗口 ----------

    private void OpenSettings()
    {
        var dlg = new Views.SettingsWindow() { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        ApplySettings();
    }

    /// <summary>设置变更后应用到所有标签。</summary>
    public void ApplySettings()
    {
        App.ApplyTheme(App.Settings.Theme);
        foreach (var t in Tabs)
            t.ApplySettings(App.Settings);
        OnPropertyChanged(nameof(EolMarkersEnabled));
    }

    private void OpenTagLibrary()
    {
        var dlg = new Views.TagLibraryWindow(TagLibrary) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
        if (dlg.Saved && dlg.ResultLibrary is not null)
        {
            SaveTagLibrary(dlg.ResultLibrary);
        }
    }

    // ---------- 标签操作 ----------

    private void RemoveTagAtCaret()
    {
        if (ActiveDocument is null) return;
        EditorOps.RemoveTag(ActiveDocument, TagLibrary.Tags.ToList());
    }

    // ---------- 文档事件 ----------

    /// <summary>从磁盘重新加载当前标签（外部修改后）。</summary>
    public void ReloadActiveFromDisk()
    {
        var vm = ActiveDocument;
        if (vm?.FilePath is not string path || !File.Exists(path)) return;
        try
        {
            var result = FileService.OpenText(path);
            vm.LoadText(result.Text, result.Encoding);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"重新加载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
