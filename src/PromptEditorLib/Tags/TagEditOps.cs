namespace PromptEditorLib.Tags;

/// <summary>
/// 标签插入 / 包裹 / 移除（纯文本运算，返回新文本与新光标偏移）。
/// </summary>
public static class TagEditOps
{
    /// <summary>
    /// 用指定标签包裹选区。
    /// 块级：开始标签\n选中内容\n结束标签；行内：开始标签选中内容结束标签。
    /// 无选中文本时插入空标签对，光标落在中间（块级时落在中间空行）。
    /// </summary>
    /// <returns>新文本与新光标绝对偏移（位于插入内容末尾，空选区时位于中间）。</returns>
    public static (string Text, int Caret) Wrap(string text, int selStart, int selLength, TagDefinition tag)
    {
        if (selStart < 0 || selStart > text.Length) throw new ArgumentOutOfRangeException(nameof(selStart));
        selLength = Math.Clamp(selLength, 0, text.Length - selStart);
        int selEnd = selStart + selLength;

        string head = text[..selStart];
        string body = text[selStart..selEnd];
        string tail = text[selEnd..];

        if (tag.WrapMode == TagWrapMode.Inline)
        {
            string inserted = tag.StartTag + body + tag.EndTag;
            string newText = head + inserted + tail;
            int caret = selStart + tag.StartTag.Length + body.Length;
            return (newText, caret);
        }
        else
        {
            string inserted = tag.StartTag + "\n" + body + "\n" + tag.EndTag;
            string newText = head + inserted + tail;
            int caret = selStart + tag.StartTag.Length + 1 + body.Length + (body.Length > 0 ? 1 : 0);
            return (newText, caret);
        }
    }

    /// <summary>
    /// 移除最内层配对标签：光标位于某标签对内部，或选区完整包含一对标签时生效。
    /// 仅移除开始/结束标签字符串本身，保留内容。
    /// 找不到配对时原样返回（caret 为原选区起点）。
    /// </summary>
    public static (string Text, int Caret) RemoveTag(
        string text, int selStart, int selLength, IReadOnlyList<TagDefinition> tags)
    {
        var pairs = TagPairScanner.FindPairs(text, tags);
        int selEnd = selStart + Math.Max(0, selLength);
        var pair = TagPairScanner.FindInnermostPairForRemoval(pairs, selStart, selEnd);
        if (pair is null)
            return (text, selStart);

        var chars = text.ToCharArray();
        // 从后往前删除，避免偏移失效
        RemoveRange(chars, pair.End.Index, pair.End.Length);
        RemoveRange(chars, pair.Start.Index, pair.Start.Length);

        string newText = new string(chars, 0, chars.Length - pair.Start.Length - pair.End.Length);
        int caret = Math.Min(selStart, newText.Length);
        return (newText, caret);
    }

    private static void RemoveRange(char[] chars, int index, int length)
    {
        Array.Copy(chars, index + length, chars, index, chars.Length - index - length);
    }
}
