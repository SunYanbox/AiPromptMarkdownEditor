using Markdig;
using Markdig.Syntax;
using PromptEditorLib.Tags;

namespace PromptEditorLib.Markdown;

/// <summary>大纲条目：标题或顶层标签。</summary>
public sealed record OutlineItem(
    int Line,        // 0-based 行号
    int Level,       // 标题级别 1-6；标签为 0
    string Title,    // 标题文字或标签预览
    bool IsTag,
    string TagName)  // 标签短名，非标签条目为 ""
{
    public string Display => IsTag ? $"◈ {TagName}" : Title;
}

/// <summary>
/// 大纲提取：Markdig 解析标题（顶层块），逐行扫描顶层标签。
/// </summary>
public static class OutlineBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static List<OutlineItem> Build(string markdown, IReadOnlyList<TagDefinition> tags)
    {
        var items = new List<OutlineItem>();

        // 标题：仅取文档顶层块（不递归进入引用/列表内部）
        var doc = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
        foreach (var block in doc)
        {
            if (block is HeadingBlock h && h.Line >= 0)
            {
                string text = ExtractHeadingText(markdown, h);
                items.Add(new OutlineItem(h.Line, h.Level, text, IsTag: false, TagName: ""));
            }
        }

        // 顶层标签：行首（允许前导空白）以某开始标签开头的行
        if (tags.Count > 0 && !string.IsNullOrEmpty(markdown))
        {
            var lines = markdown.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                foreach (var tag in tags)
                {
                    if (trimmed.StartsWith(tag.StartTag, StringComparison.Ordinal))
                    {
                        items.Add(new OutlineItem(i, 0, tag.StartTag, IsTag: true, TagName: tag.Name));
                        break;
                    }
                }
            }
        }

        return items.OrderBy(x => x.Line).ThenBy(x => x.IsTag).ToList();
    }

    /// <summary>从标题行的 SourceSpan 中提取文字，去掉 # 前缀与首尾空白。</summary>
    private static string ExtractHeadingText(string markdown, HeadingBlock h)
    {
        // Markdig 的 SourceSpan.End 为闭区间
        int len = h.Span.End - h.Span.Start + 1;
        if (h.Span.Start < 0 || h.Span.End >= markdown.Length || len <= 0)
            return "";
        var raw = markdown.Substring(h.Span.Start, len);
        raw = raw.TrimStart();
        int i = 0;
        while (i < raw.Length && raw[i] == '#') i++;
        raw = raw[i..].Trim();
        // 去掉结尾的 ATX 闭合井号（如 "标题 ###"）
        raw = raw.TrimEnd();
        while (raw.EndsWith("#", StringComparison.Ordinal))
        {
            raw = raw[..^1].TrimEnd();
        }
        return raw.Trim();
    }
}
