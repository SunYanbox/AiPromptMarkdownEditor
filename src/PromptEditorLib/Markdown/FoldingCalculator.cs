using PromptEditorLib.Tags;

namespace PromptEditorLib.Markdown;

/// <summary>可折叠区域（文档偏移区间，半开 [StartOffset, EndOffset)）。</summary>
public sealed record FoldRegion(int StartOffset, int EndOffset, string Title)
{
    public bool IsValid => EndOffset > StartOffset;
}

/// <summary>
/// 折叠区域计算（纯逻辑）：
/// - 成对标签（起止标签字符串精确配对；不配对的标签不产生折叠）
/// - ``` 围栏代码块
/// - 标题章节（自标题行末到下一个同级或更高级标题之前）
/// </summary>
public static class FoldingCalculator
{
    public static List<FoldRegion> Calculate(string text, IReadOnlyList<TagDefinition> tags)
    {
        var regions = new List<FoldRegion>();
        if (string.IsNullOrEmpty(text)) return regions;

        // 1. 标签对
        foreach (var pair in TagPairScanner.FindPairs(text, tags))
        {
            regions.Add(new FoldRegion(pair.Start.Index, pair.End.EndIndex, pair.Start.Def.StartTag));
        }

        // 2. 围栏代码块 与 3. 标题章节
        var lineStarts = GetLineStarts(text);
        int lineCount = lineStarts.Count;

        int fenceStart = -1;
        var headings = new List<(int Line, int Level, int ContentStart)>();

        for (int i = 0; i < lineCount; i++)
        {
            int start = lineStarts[i];
            int end = i + 1 < lineCount ? lineStarts[i + 1] - 1 : text.Length; // 不含行尾换行
            if (end < start) end = start;
            var line = text.Substring(start, Math.Min(end, text.Length) - start);
            var trimmed = line.TrimStart();

            // 围栏（含 ```markdown 等信息串）；以 ``` 开头即切换状态
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (fenceStart < 0)
                {
                    fenceStart = start;
                }
                else
                {
                    int closeEnd = i + 1 < lineCount ? lineStarts[i + 1] : text.Length;
                    regions.Add(new FoldRegion(fenceStart, closeEnd, "``` ⋯"));
                    fenceStart = -1;
                }
                continue;
            }

            // ATX 标题
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                int level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                if (level <= 6 && (level == trimmed.Length || trimmed[level] is ' ' or '\t'))
                {
                    // 章节折叠自标题行的下一行开始（标题文字保持可见）
                    int contentStart = i + 1 < lineCount ? lineStarts[i + 1] : text.Length;
                    headings.Add((i, level, Math.Min(contentStart, text.Length)));
                }
            }
        }
        // 未闭合围栏：折叠到文末
        if (fenceStart >= 0)
            regions.Add(new FoldRegion(fenceStart, text.Length, "``` ⋯"));

        // 标题章节：到下一个同级或更高级标题之前
        for (int i = 0; i < headings.Count; i++)
        {
            var (line, level, contentStart) = headings[i];
            int endOffset = text.Length;
            for (int j = i + 1; j < headings.Count; j++)
            {
                if (headings[j].Level <= level)
                {
                    endOffset = lineStarts[headings[j].Line];
                    break;
                }
            }
            // 折叠内容区（不含标题行本身）；章节内容非空才可折叠
            if (contentStart < endOffset)
                regions.Add(new FoldRegion(contentStart, endOffset, "章节 ⋯"));
        }

        // 仅保留互不部分重叠的区域（允许完全嵌套）
        regions = regions.Where(r => r.IsValid).OrderBy(r => r.StartOffset).ThenBy(r => -r.EndOffset).ToList();
        var result = new List<FoldRegion>();
        foreach (var r in regions)
        {
            // 与已接受区域部分重叠则丢弃
            bool ok = true;
            foreach (var accepted in result)
            {
                bool nested = r.StartOffset >= accepted.StartOffset && r.EndOffset <= accepted.EndOffset;
                bool disjoint = r.EndOffset <= accepted.StartOffset || r.StartOffset >= accepted.EndOffset;
                if (!nested && !disjoint) { ok = false; break; }
            }
            if (ok) result.Add(r);
        }
        return result;
    }

    /// <summary>各行起始偏移。</summary>
    public static List<int> GetLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts.Add(i + 1);
        return starts;
    }
}
