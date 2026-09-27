using System.Text.RegularExpressions;

namespace PromptEditorLib.Markdown;

/// <summary>
/// 列表操作：无序/有序列表逐行添加/移除前缀、有序列表自动重新编号、Tab/Shift+Tab 缩进。
/// </summary>
public static partial class ListOps
{
    [GeneratedRegex(@"^(\s*)([-*+])\s+(.*)$")]
    private static partial Regex UnorderedPattern();

    [GeneratedRegex(@"^(\s*)(\d+)([.)])\s+(.*)$")]
    private static partial Regex OrderedPattern();

    /// <summary>判断一行是否为列表项（无序或有序）。</summary>
    public static bool IsListItem(string line) =>
        UnorderedPattern().IsMatch(line) || OrderedPattern().IsMatch(line);

    /// <summary>无序列表切换：全部已有前缀则移除，否则添加 "- "。</summary>
    public static List<string> ToggleUnordered(IReadOnlyList<string> lines)
    {
        bool allMarked = lines.Count > 0 && lines.All(l => UnorderedPattern().IsMatch(l) && !IsActuallyOrdered(l));
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (allMarked)
            {
                var m = UnorderedPattern().Match(line);
                result.Add(m.Groups[1].Value + m.Groups[3].Value);
            }
            else
            {
                result.Add(AddPrefix(line, "- "));
            }
        }
        return result;
    }

    private static bool IsActuallyOrdered(string line) => OrderedPattern().IsMatch(line);

    /// <summary>有序列表切换：全部已有前缀则移除，否则添加编号（若首行已有编号则沿用其数字起始）。</summary>
    public static List<string> ToggleOrdered(IReadOnlyList<string> lines)
    {
        bool allMarked = lines.Count > 0 && lines.All(l => OrderedPattern().IsMatch(l));
        var result = new List<string>(lines.Count);
        if (allMarked)
        {
            foreach (var line in lines)
            {
                var m = OrderedPattern().Match(line);
                result.Add(m.Groups[1].Value + m.Groups[4].Value);
            }
            return result;
        }

        int start = 1;
        if (lines.Count > 0 && OrderedPattern().Match(lines[0]) is { Success: true } first)
            start = int.Parse(first.Groups[2].Value);

        int n = start;
        foreach (var line in lines)
        {
            var m = OrderedPattern().Match(line);
            if (m.Success)
                result.Add(m.Groups[1].Value + $"{n}. " + m.Groups[4].Value);
            else
                result.Add(AddPrefix(line, $"{n}. "));
            n++;
        }
        return result;
    }

    /// <summary>
    /// 对有序列表块自动重新编号：从 startIndex（含）向前扩展到连续有序行块的头部，
    /// 起始数字沿用块首行的原编号，逐行 1,2,3… 递增。
    /// </summary>
    public static List<string> RenumberOrdered(IReadOnlyList<string> lines, int startIndex)
    {
        var result = lines.ToList();
        if (startIndex < 0 || startIndex >= result.Count) return result;

        int blockStart = startIndex;
        while (blockStart > 0 && OrderedPattern().IsMatch(result[blockStart - 1])) blockStart--;
        int blockEnd = startIndex;
        while (blockEnd + 1 < result.Count && OrderedPattern().IsMatch(result[blockEnd + 1])) blockEnd++;

        var first = OrderedPattern().Match(result[blockStart]);
        int n = first.Success ? int.Parse(first.Groups[2].Value) : 1;

        for (int i = blockStart; i <= blockEnd; i++)
        {
            var m = OrderedPattern().Match(result[i]);
            if (m.Success)
                result[i] = m.Groups[1].Value + $"{n}. " + m.Groups[4].Value;
            n++;
        }
        return result;
    }

    /// <summary>Tab：行首插入 4 空格（层级缩进）。</summary>
    public static string Indent(string line, string indentUnit = "    ") => indentUnit + line;

    /// <summary>Shift+Tab：移除行首最多一个缩进单位。</summary>
    public static string Unindent(string line, string indentUnit = "    ")
    {
        var trimmed = line.TrimStart();
        int leading = line.Length - trimmed.Length;
        int remove = Math.Min(leading, indentUnit.Length);
        return line[remove..];
    }

    private static string AddPrefix(string line, string prefix)
    {
        // 保留原有缩进，前缀加在缩进之后
        var trimmed = line.TrimStart();
        int leading = line.Length - trimmed.Length;
        string indent = line[..leading];
        return trimmed.Length == 0 ? indent + prefix.TrimEnd() : indent + prefix + trimmed;
    }
}
