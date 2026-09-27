using System.Text.RegularExpressions;

namespace PromptEditorLib.Markdown;

/// <summary>
/// Markdown 标题操作（ATX 风格）：设置/移除/升级/降级。
/// </summary>
public static partial class HeadingOps
{
    [GeneratedRegex(@"^\s{0,3}(#{1,6})(\s+|$)")]
    private static partial Regex HeadingPrefix();

    /// <summary>
    /// 把一行设为指定级别标题；level 为 null 时移除标题前缀（保留文字）。
    /// 返回转换后的整行文本（不含行尾换行）。
    /// </summary>
    public static string SetHeading(string line, int? level)
    {
        string text = StripHeading(line);
        if (level is null) return text;
        level = Math.Clamp(level.Value, 1, 6);
        return new string('#', level.Value) + " " + text;
    }

    /// <summary>去除行内标题前缀（含闭合 #），保留文字。</summary>
    public static string StripHeading(string line)
    {
        var m = HeadingPrefix().Match(line);
        if (!m.Success) return line.TrimEnd();
        string rest = line[m.Length..].TrimEnd();
        // 去掉 ATX 闭合井号
        while (rest.EndsWith("#", StringComparison.Ordinal))
            rest = rest[..^1].TrimEnd();
        return rest;
    }

    /// <summary>判断该行是否标题，返回级别（0 = 不是标题）。</summary>
    public static int GetHeadingLevel(string line)
    {
        var m = HeadingPrefix().Match(line);
        return m.Success ? m.Groups[1].Length : 0;
    }

    /// <summary>升级（减小级别，最低到 H1；已是 H1 则不变）。</summary>
    public static string Promote(string line)
    {
        int level = GetHeadingLevel(line);
        if (level == 0) return line;
        return SetHeading(line, Math.Max(1, level - 1));
    }

    /// <summary>降级（增大级别，最高到 H6；已是 H6 则不变）。</summary>
    public static string Demote(string line)
    {
        int level = GetHeadingLevel(line);
        if (level == 0) return line;
        return SetHeading(line, Math.Min(6, level + 1));
    }

    /// <summary>
    /// Ctrl+数字 语义：当前行已是对应级别则去掉标题样式，否则切换至目标级别。
    /// </summary>
    public static string ToggleHeading(string line, int level)
    {
        return GetHeadingLevel(line) == level ? StripHeading(line) : SetHeading(line, level);
    }
}
