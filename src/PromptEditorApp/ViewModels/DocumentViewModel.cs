using System.IO;
using System.Text;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using PromptEditor.Controls;
using PromptEditor.Services;
using PromptEditorLib.Markdown;
using PromptEditorLib.Text;

namespace PromptEditor.ViewModels;

/// <summary>
/// 一个标签页对应的文档及其全部独立状态：
/// 文档内容、撤销栈（TextDocument 自带）、光标/选区/滚动（AvalonEdit 实例自带）、
/// 折叠状态、关联文件路径、编码、行尾类型、修改标记。
/// 每个标签持有独立的 TextEditor 实例，切换标签时整实例替换进布局，
/// 不触发重新加载，状态完整保留。
/// </summary>
public sealed class DocumentViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public TextDocument Document { get; }

    /// <summary>本标签专属编辑器实例（承载视图状态）。</summary>
    public TextEditor Editor { get; }

    public FoldingManager Folding { get; }

    /// <summary>查找匹配高亮渲染器（每文档独立）。</summary>
    public MatchHighlightRenderer MatchRenderer { get; }

    private readonly LineEndMarkerGenerator _lineEndGenerator = new();

    private string _untitledName = "未命名.md";
    /// <summary>未命名标签的基础名（未命名.md / 未命名2.md …）。</summary>
    public string UntitledName
    {
        get => _untitledName;
        set => SetProperty(ref _untitledName, value);
    }

    private string? _filePath;
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
                OnPropertyChanged(nameof(BaseDisplayName));
        }
    }

    private Encoding _encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    public Encoding Encoding
    {
        get => _encoding;
        set => SetProperty(ref _encoding, value);
    }

    private bool _isModified;
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (SetProperty(ref _isModified, value))
                OnPropertyChanged(nameof(DisplaySuffix));
        }
    }

    /// <summary>消歧后的显示名（不含修改标记）。</summary>
    private string _displayTitle = "未命名.md";
    public string DisplayTitle
    {
        get => _displayTitle;
        private set => SetProperty(ref _displayTitle, value);
    }

    public string DisplaySuffix => IsModified ? "*" : "";

    /// <summary>标签页 / 标题栏显示文本（含修改标记）。</summary>
    public string DisplayTitleWithSuffix => DisplayTitle + DisplaySuffix;

    /// <summary>标签悬停 tooltip：完整绝对路径与修改状态。</summary>
    public string TooltipText =>
        FilePath is not null ? $"{FilePath}\n{(IsModified ? "已修改（未保存）" : "未修改")}"
                             : $"{UntitledName}（未保存的新文档）";

    /// <summary>基础显示名（用于同名消歧计算）：文件标签为文件名，未命名标签为 未命名N.md。</summary>
    public string BaseDisplayName => FilePath is not null ? Path.GetFileName(FilePath) : UntitledName;

    /// <summary>同名消歧结果回填（含「…」前缀）。</summary>
    public void SetDisambiguatedName(string name) => DisplayTitle = name;

    /// <summary>是否有磁盘文件（决定能否被 Ctrl+Shift+T 恢复）。</summary>
    public bool HasFile => FilePath is not null;

    /// <summary>行尾信息（检测缓存）。</summary>
    public LineEndingInfo LineEndingInfo { get; private set; } =
        new(LineEnding.Lf, Mixed: false, FirstMixedLine: -1, CrLfCount: 0, LfCount: 0, CrCount: 0);

    /// <summary>标签配对错误提示（由结构刷新线程回填），空串表示无错误。</summary>
    public string TagErrorText { get; set; } = "";

    /// <summary>当前缓存的折叠区域（用于 Ctrl+M 定位光标所在区域）。</summary>
    public IReadOnlyList<FoldRegion> CachedFoldRegions { get; private set; } = Array.Empty<FoldRegion>();

    /// <summary>状态变化（光标/选区/修改标记/文本），用于状态栏刷新。</summary>
    public event Action<DocumentViewModel>? StatusChanged;

    /// <summary>文档结构变化（文本内容变化），用于折叠/大纲刷新。</summary>
    public event Action<DocumentViewModel>? StructureChanged;

    public DocumentViewModel(AppSettings settings, string? text = null)
    {
        Document = new TextDocument(text ?? "");
        Document.UndoStack.ClearAll(); // 初始状态不可撤销

        Editor = new TextEditor
        {
            Document = Document,
            FontFamily = new FontFamily(settings.FontFamily),
            FontSize = settings.FontSize,
            WordWrap = settings.SoftWrap,
            ShowLineNumbers = settings.ShowLineNumbers,
            Background = TryFindEditorBrush("Editor.Background"),
            Foreground = TryFindEditorBrush("Editor.Foreground"),
            LineNumbersForeground = TryFindEditorBrush("Editor.LineNumbers"),
        };
        Editor.Options.ConvertTabsToSpaces = !settings.TabUsesTabChar;
        Editor.Options.IndentationSize = settings.TabSize;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableHyperlinks = false;
        Editor.SyntaxHighlighting = LoadMarkdownHighlighting(settings.Theme);
        Editor.TextArea.TextView.CurrentLineBackground = TryFindEditorBrush("Editor.LineHighlight");

        // 行尾显示（仿 cat -A）：纯显示，不改文本
        _lineEndGenerator.Brush = TryFindEditorBrush("Marker.Light");
        _lineEndGenerator.Enabled = settings.ShowLineEndMarkers;
        _lineEndGenerator.HostTextView = Editor.TextArea.TextView;
        Editor.TextArea.TextView.ElementGenerators.Add(_lineEndGenerator);

        Folding = FoldingManager.Install(Editor.TextArea);

        // 查找匹配高亮渲染器
        MatchRenderer = new MatchHighlightRenderer(
            TryFindEditorBrush("Match.Background"),
            TryFindEditorBrush("Match.Current"));
        Editor.TextArea.TextView.BackgroundRenderers.Add(MatchRenderer);
        Editor.TextArea.SelectionBrush = TryFindEditorBrush("Editor.Selection");

        Document.TextChanged += (_, _) =>
        {
            if (!_suppressModifiedFlag) IsModified = true;
            LineEndingInfo = LineEndingUtil.Detect(Document.Text);
            StatusChanged?.Invoke(this);
            StructureChanged?.Invoke(this);
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) => StatusChanged?.Invoke(this);
        Editor.TextArea.SelectionChanged += (_, _) => StatusChanged?.Invoke(this);

        LineEndingInfo = LineEndingUtil.Detect(Document.Text);
    }

    private bool _suppressModifiedFlag;

    private static Brush? TryFindEditorBrush(string key)
        => System.Windows.Application.Current?.TryFindResource(key) as Brush;

    /// <summary>加载内置 Markdown 高亮定义（按主题）。</summary>
    public static IHighlightingDefinition? LoadMarkdownHighlighting(string theme)
    {
        var name = theme == "Light"
            ? "PromptEditor.Themes.MarkdownLight.xshd"
            : "PromptEditor.Themes.MarkdownDark.xshd";
        var asm = typeof(DocumentViewModel).Assembly;
        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var reader = new System.Xml.XmlTextReader(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>主题切换 / 设置变更时刷新编辑器外观。</summary>
    public void ApplySettings(AppSettings settings)
    {
        Editor.FontFamily = new FontFamily(settings.FontFamily);
        Editor.FontSize = settings.FontSize;
        Editor.WordWrap = settings.SoftWrap;
        Editor.ShowLineNumbers = settings.ShowLineNumbers;
        Editor.Background = TryFindEditorBrush("Editor.Background");
        Editor.Foreground = TryFindEditorBrush("Editor.Foreground");
        Editor.LineNumbersForeground = TryFindEditorBrush("Editor.LineNumbers");
        Editor.Options.ConvertTabsToSpaces = !settings.TabUsesTabChar;
        Editor.Options.IndentationSize = settings.TabSize;
        Editor.SyntaxHighlighting = LoadMarkdownHighlighting(settings.Theme);
        _lineEndGenerator.Enabled = settings.ShowLineEndMarkers;
        _lineEndGenerator.Brush = TryFindEditorBrush("Marker.Light");
        UpdateThemeBrushes();
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>主题切换时同步编辑器内所有与主题相关的刷子。</summary>
    public void UpdateThemeBrushes()
    {
        Editor.TextArea.TextView.CurrentLineBackground = TryFindEditorBrush("Editor.LineHighlight");
        Editor.TextArea.SelectionBrush = TryFindEditorBrush("Editor.Selection");
        MatchRenderer.UpdateBrushes(
            TryFindEditorBrush("Match.Background"),
            TryFindEditorBrush("Match.Current"));
        _flash?.UpdateBrush(TryFindEditorBrush("Flash.Background"));
    }

    public void SetLineEndMarkersEnabled(bool enabled)
    {
        _lineEndGenerator.Enabled = enabled;
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>从磁盘重载（Ctrl+Shift+T 场景外的「重新加载」）。</summary>
    public void LoadText(string text, Encoding encoding)
    {
        _suppressModifiedFlag = true;
        try
        {
            Document.Text = text;
            Document.UndoStack.ClearAll();
        }
        finally
        {
            _suppressModifiedFlag = false;
        }
        Encoding = encoding;
        IsModified = false;
        LineEndingInfo = LineEndingUtil.Detect(text);
        StatusChanged?.Invoke(this);
        StructureChanged?.Invoke(this);
    }

    // ---------- 折叠 ----------

    private readonly List<FoldingSection> _sections = new();

    /// <summary>应用结构刷新线程计算的折叠区域（UI 线程调用），保留已有折叠状态。</summary>
    public void UpdateFolding(IReadOnlyList<FoldRegion> regions) => ApplyFoldRegions(regions);

    public void ApplyFoldRegions(IReadOnlyList<FoldRegion> regions)
    {
        CachedFoldRegions = regions;
        var desired = new List<(int Start, int End, string Title)>();
        foreach (var r in regions)
        {
            int startLine = Document.GetLineByOffset(r.StartOffset).LineNumber;
            int endLine = Document.GetLineByOffset(Math.Min(Math.Max(r.EndOffset - 1, r.StartOffset), Math.Max(0, Document.TextLength - 1))).LineNumber;
            int foldedLines = Math.Max(0, endLine - startLine);
            desired.Add((r.StartOffset, r.EndOffset, $"{r.Title} ⋯ 已折叠 {foldedLines} 行"));
        }

        // 复用相同区间的既有 section（保留折叠状态），创建缺失的，移除多余的
        var kept = new List<FoldingSection>();
        foreach (var d in desired)
        {
            var section = _sections.FirstOrDefault(s => s.StartOffset == d.Start && s.EndOffset == d.End);
            if (section is null)
            {
                if (d.End <= d.Start) continue;
                section = Folding.CreateFolding(d.Start, d.End);
                _sections.Add(section);
            }
            section.Title = d.Title;
            kept.Add(section);
        }

        foreach (var stale in _sections.Except(kept).ToList())
        {
            Folding.RemoveFolding(stale);
            _sections.Remove(stale);
        }
    }

    /// <summary>折叠 / 展开光标所在区域（最内层包含区域）。</summary>
    public void ToggleFoldAtOffset(int offset)
    {
        FoldingSection? best = null;
        foreach (var s in _sections)
        {
            if (s.StartOffset <= offset && offset <= s.EndOffset &&
                (best is null || (s.EndOffset - s.StartOffset) < (best.EndOffset - best.StartOffset)))
                best = s;
        }
        if (best is not null)
            best.IsFolded = !best.IsFolded;
    }

    public void SetAllFolds(bool folded)
    {
        foreach (var s in _sections)
            s.IsFolded = folded;
    }

    /// <summary>Ctrl+0 复位字号。</summary>
    public void ResetZoom() => Editor.FontSize = App.Settings.FontSize;

    /// <summary>首个未配对标签所在行号（结构刷新时计算，供状态栏提示错误行号）。</summary>
    public int FirstUnmatchedTagLine { get; set; } = -1;

    // ---------- 跳转 ----------

    /// <summary>跳转到指定行（1-based），滚动定位并短暂高亮。</summary>
    public void JumpToLine(int lineNumber, int flashMilliseconds = 600)
    {
        lineNumber = Math.Clamp(lineNumber, 1, Math.Max(1, Document.LineCount));
        var line = Document.GetLineByNumber(lineNumber);
        Editor.TextArea.Caret.Offset = line.Offset;
        Editor.ScrollToLine(lineNumber);
        Editor.TextArea.Caret.BringCaretToView();
        FlashLines(line, flashMilliseconds);
    }

    private FlashHighlightRenderer? _flash;

    public void FlashLines(DocumentLine line, int milliseconds)
    {
        _flash ??= new FlashHighlightRenderer(TryFindEditorBrush("Flash.Background"));
        if (!Editor.TextArea.TextView.BackgroundRenderers.Contains(_flash))
            Editor.TextArea.TextView.BackgroundRenderers.Add(_flash);
        _flash.Show(line.Offset, line.EndOffset, milliseconds);
    }

    /// <summary>标题栏显示。</summary>
    public override string ToString() => DisplayTitle + DisplaySuffix;
}
