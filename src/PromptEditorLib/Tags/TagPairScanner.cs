namespace PromptEditorLib.Tags;

/// <summary>一次标签字符串出现（开始或结束）。</summary>
public sealed record TagOccurrence(int Index, int Length, bool IsStart, TagDefinition Def)
{
    public int EndIndex => Index + Length;
}

/// <summary>一对成功配对的标签。</summary>
public sealed record TagPair(TagOccurrence Start, TagOccurrence End)
{
    /// <summary>内容区间长度（不含标签本身）。</summary>
    public int ContentLength => End.Index - Start.EndIndex;

    public bool ContainsOffset(int offset) => offset >= Start.Index && offset <= End.EndIndex;
}

/// <summary>
/// 标签配对解析（纯逻辑，全文档通用）：
/// 识别基于开始/结束标签字符串本身，大小写敏感、精确匹配，与短名无关。
/// 允许嵌套；不配对的开始标签被丢弃，不配对的结束标签被忽略。
/// </summary>
public static class TagPairScanner
{
    /// <summary>按出现位置升序返回所有标签字符串出现。</summary>
    public static List<TagOccurrence> FindOccurrences(string text, IReadOnlyList<TagDefinition> tags)
    {
        var result = new List<TagOccurrence>();
        if (text.Length == 0) return result;

        foreach (var tag in tags)
        {
            if (tag.StartTag.Length == 0 || tag.EndTag.Length == 0) continue;
            Collect(text, tag.StartTag, isStart: true, tag, result);
            Collect(text, tag.EndTag, isStart: false, tag, result);
        }

        result.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : b.Length.CompareTo(a.Length));
        return result;
    }

    private static void Collect(string text, string needle, bool isStart, TagDefinition tag, List<TagOccurrence> result)
    {
        int idx = 0;
        while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            result.Add(new TagOccurrence(idx, needle.Length, isStart, tag));
            idx += needle.Length;
        }
    }

    /// <summary>
    /// 用栈做配对：遇到开始标签压栈；遇到结束标签时弹出最近的同名开始标签配对，
    /// 中间被跨越的不同标签视为未配对丢弃。
    /// 返回按开始位置升序的配对列表。
    /// </summary>
    public static List<TagPair> FindPairs(string text, IReadOnlyList<TagDefinition> tags)
    {
        var occurrences = FindOccurrences(text, tags);
        var pairs = new List<TagPair>();
        var stack = new Stack<TagOccurrence>();

        foreach (var occ in occurrences)
        {
            if (occ.IsStart)
            {
                stack.Push(occ);
            }
            else
            {
                TagOccurrence? matched = null;
                while (stack.Count > 0)
                {
                    var top = stack.Pop();
                    if (!top.IsStart) continue; // 不应发生
                    if (string.Equals(top.Def.Name, occ.Def.Name, StringComparison.Ordinal))
                    {
                        matched = top;
                        break;
                    }
                    // 跨越了另一个未关闭的开始标签：丢弃
                }
                if (matched != null)
                    pairs.Add(new TagPair(matched, occ));
            }
        }

        pairs.Sort((a, b) => a.Start.Index.CompareTo(b.Start.Index));
        return pairs;
    }

    /// <summary>
    /// 找到覆盖给定偏移的最内层标签对（光标位于标签对内部，含位于标签字符串本身上）。
    /// 找不到返回 null。
    /// </summary>
    public static TagPair? FindInnermostPairAt(IReadOnlyList<TagPair> pairs, int offset)
    {
        TagPair? best = null;
        foreach (var pair in pairs)
        {
            if (pair.ContainsOffset(offset) &&
                (best is null || pair.ContentLength < best.ContentLength))
                best = pair;
        }
        return best;
    }

    /// <summary>
    /// 找到被选区完整包含的最内层标签对（用于「移除标签」）。
    /// 无选区时退化为 FindInnermostPairAt。
    /// </summary>
    public static TagPair? FindInnermostPairForRemoval(IReadOnlyList<TagPair> pairs, int selStart, int selEnd)
    {
        bool hasSelection = selEnd > selStart;
        TagPair? best = null;

        foreach (var pair in pairs)
        {
            bool contained = hasSelection
                ? selStart <= pair.Start.Index && selEnd >= pair.End.EndIndex
                : pair.ContainsOffset(selStart);

            if (contained && (best is null || pair.ContentLength < best.ContentLength))
                best = pair;
        }
        return best;
    }

    /// <summary>
    /// 描述首个未配对标签（供状态栏提示错误行号）；全部配对良好时返回空串。
    /// </summary>
    public static string FindUnpairedDescription(string text, IReadOnlyList<TagDefinition> tags)
    {
        if (text.Length == 0) return "";

        var occurrences = FindOccurrences(text, tags);
        var pairs = FindPairs(text, tags);
        var paired = new HashSet<TagOccurrence>(ReferenceEqualityComparer.Instance);
        foreach (var p in pairs)
        {
            paired.Add(p.Start);
            paired.Add(p.End);
        }

        static int LineOf(string s, int offset)
        {
            int line = 1;
            for (int i = 0; i < offset && i < s.Length; i++)
                if (s[i] == '\n') line++;
            return line;
        }

        // 未配对的结束标签（被丢弃的）
        foreach (var occ in occurrences)
        {
            if (!occ.IsStart && !paired.Contains(occ))
                return $"第 {LineOf(text, occ.Index)} 行的结束标签 “{occ.Def.EndTag}” 没有对应的开始标签";
        }
        // 未配对的开始标签（栈中残留，取最早的）
        TagOccurrence? firstUnmatchedStart = null;
        foreach (var occ in occurrences)
        {
            if (occ.IsStart && !paired.Contains(occ) &&
                (firstUnmatchedStart is null || occ.Index < firstUnmatchedStart.Index))
                firstUnmatchedStart = occ;
        }
        if (firstUnmatchedStart is not null)
            return $"第 {LineOf(text, firstUnmatchedStart.Index)} 行的开始标签 “{firstUnmatchedStart.Def.StartTag}” 没有对应的结束标签";

        return "";
    }
}
