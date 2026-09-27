using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace PromptEditor.Controls;

/// <summary>
/// 行尾显示（仿 cat -A）：CRLF → ^M$，LF → $，CR → ^M。
/// 以零文档长度的装饰元素渲染，浅色修饰，不改动真实文本；
/// 复制与保存始终输出真实文本。
/// </summary>
public sealed class LineEndMarkerGenerator : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public Brush? Brush { get; set; }

    /// <summary>宿主 TextView（由 DocumentViewModel 注入）。</summary>
    public TextView? HostTextView { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;
        var doc = HostTextView?.Document;
        if (doc is null) return -1;

        int end = doc.TextLength;
        for (int off = startOffset; off < end; off++)
        {
            char c = doc.GetCharAt(off);
            if (c == '\r' || c == '\n') return off;
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (!Enabled) return null;
        var doc = HostTextView?.Document;
        if (doc is null || offset >= doc.TextLength) return null;

        char c = doc.GetCharAt(offset);
        string marker;
        if (c == '\r')
        {
            bool followedByLf = offset + 1 < doc.TextLength && doc.GetCharAt(offset + 1) == '\n';
            marker = followedByLf ? "^M$" : "^M";
        }
        else if (c == '\n')
        {
            // CRLF 已由 ^M$ 一并显示
            if (offset > 0 && doc.GetCharAt(offset - 1) == '\r') return null;
            marker = "$";
        }
        else
        {
            return null;
        }

        return new MarkerElement(marker, Brush ?? Brushes.Gray);
    }

    /// <summary>零文档长度的文本装饰元素（浅色前景）。</summary>
    private sealed class MarkerElement : VisualLineElement
    {
        private readonly string _text;
        private readonly Brush _brush;

        public MarkerElement(string text, Brush brush)
            : base(text.Length, 0)
        {
            _text = text;
            _brush = brush;
        }

        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            if (TextRunProperties is null)
                throw new InvalidOperationException("MarkerElement 未初始化 TextRunProperties。");
            return new TextCharacters(_text, new MarkerTextRunProperties(TextRunProperties, _brush));
        }
    }

    /// <summary>以宿主 TextRunProperties 为基准、仅覆盖前景色的只读包装。</summary>
    private sealed class MarkerTextRunProperties : TextRunProperties
    {
        private readonly TextRunProperties _base;
        private readonly Brush _foreground;

        public MarkerTextRunProperties(TextRunProperties baseProps, Brush foreground)
        {
            _base = baseProps;
            _foreground = foreground;
        }

        public override Brush? ForegroundBrush => _foreground;
        public override Brush? BackgroundBrush => _base.BackgroundBrush;
        public override CultureInfo? CultureInfo => _base.CultureInfo;
        public override double FontRenderingEmSize => _base.FontRenderingEmSize;
        public override double FontHintingEmSize => _base.FontHintingEmSize;
        public override Typeface Typeface => _base.Typeface;
        public override TextDecorationCollection? TextDecorations => _base.TextDecorations;
        public override TextEffectCollection? TextEffects => _base.TextEffects;
        public override BaselineAlignment BaselineAlignment => _base.BaselineAlignment;
    }
}

/// <summary>跳转定位后的短暂高亮渲染器。</summary>
public sealed class FlashHighlightRenderer : IBackgroundRenderer
{
    private Brush _brush;
    private readonly DispatcherTimer _timer;
    private TextView? _host;
    private int _start = -1;
    private int _end = -1;

    public FlashHighlightRenderer(Brush? brush)
    {
        _brush = MakeBrush(brush);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _timer.Tick += (_, _) => Hide();
    }

    private static Brush MakeBrush(Brush? brush)
    {
        var b = brush?.Clone() ?? Brushes.LightBlue.Clone();
        b.Opacity = 0.55;
        b.Freeze();
        return b;
    }

    /// <summary>主题切换时更新刷子。</summary>
    public void UpdateBrush(Brush? brush) => _brush = MakeBrush(brush);

    public KnownLayer Layer => KnownLayer.Background;

    public void Show(int start, int end, int milliseconds)
    {
        _start = start;
        _end = end;
        _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _timer.Stop();
        _timer.Start();
    }

    private void Hide()
    {
        _timer.Stop();
        _start = _end = -1;
        _host?.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        _host = textView;
        if (_start < 0) return;
        var segment = new TextSegment { StartOffset = _start, EndOffset = _end };
        var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
        builder.AddSegment(textView, segment);
        var geometry = builder.CreateGeometry();
        if (geometry != null)
            drawingContext.DrawGeometry(_brush, null, geometry);
    }
}
