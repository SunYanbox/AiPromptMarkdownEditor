namespace PromptEditorLib.Markdown;

/// <summary>
/// 行内标记（加粗 ** / 斜体 * / 删除线 ~~）的切换逻辑。
/// 对已格式化选区再次点击为移除（切换语义）。
/// </summary>
public static class InlineMarkerOps
{
    /// <summary>
    /// 对选中文本切换标记。
    /// 已被完整包裹（以 marker 开头且以 marker 结尾）则移除，否则包裹。
    /// </summary>
    /// <returns>新文本与是否为移除操作。</returns>
    public static (string Text, bool Removed) ToggleMarker(string selected, string marker)
    {
        if (selected.Length >= marker.Length * 2 &&
            selected.StartsWith(marker, StringComparison.Ordinal) &&
            selected.EndsWith(marker, StringComparison.Ordinal))
        {
            return (selected[marker.Length..^marker.Length], true);
        }
        return (marker + selected + marker, false);
    }

    /// <summary>无选中文本时插入空标记的文本（光标应放在正中）。</summary>
    public static string EmptyPair(string marker) => marker + marker;
}
