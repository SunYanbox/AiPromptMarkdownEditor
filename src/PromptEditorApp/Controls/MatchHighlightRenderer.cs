using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace PromptEditor.Controls;

/// <summary>
/// 查找匹配项高亮渲染器（全部匹配 + 当前匹配），纯显示层。
/// 数据由 ViewModel 推送；调用方负责 InvalidateLayer 触发重绘。
/// </summary>
public sealed class MatchHighlightRenderer : IBackgroundRenderer
{
    private Brush _allBrush;
    private Brush _currentBrush;
    private IReadOnlyList<(int Start, int Length)> _matches = Array.Empty<(int, int)>();
    private int _currentIndex = -1;

    public MatchHighlightRenderer(Brush? allBrush, Brush? currentBrush)
    {
        _allBrush = FreezeCopy(allBrush, 0.35);
        _currentBrush = FreezeCopy(currentBrush, 0.55);
    }

    /// <summary>主题切换时更新刷子。</summary>
    public void UpdateBrushes(Brush? allBrush, Brush? currentBrush)
    {
        _allBrush = FreezeCopy(allBrush, 0.35);
        _currentBrush = FreezeCopy(currentBrush, 0.55);
    }

    private static Brush FreezeCopy(Brush? b, double opacity)
    {
        var brush = b?.Clone() ?? Brushes.Yellow.Clone();
        brush.Opacity = opacity;
        brush.Freeze();
        return brush;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetMatches(IReadOnlyList<(int Start, int Length)> matches, int currentIndex)
    {
        _matches = matches;
        _currentIndex = currentIndex;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        for (int i = 0; i < _matches.Count; i++)
        {
            var (start, length) = _matches[i];
            if (length <= 0) continue;

            bool isCurrent = i == _currentIndex;
            var segment = new ICSharpCode.AvalonEdit.Document.TextSegment
            {
                StartOffset = start,
                Length = length
            };

            var builder = new BackgroundGeometryBuilder { CornerRadius = 2 };
            builder.AddSegment(textView, segment);
            var geometry = builder.CreateGeometry();
            if (geometry != null)
                drawingContext.DrawGeometry(isCurrent ? _currentBrush : _allBrush, null, geometry);
        }
    }
}
