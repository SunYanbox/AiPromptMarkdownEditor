namespace PromptEditorLib.Tags;

/// <summary>标签包裹方式：块级（起止标签独占一行）或行内（紧贴内容）。</summary>
public enum TagWrapMode
{
    Block,
    Inline
}

/// <summary>
/// 标签定义。短名仅用于菜单/列表显示与搜索排序；
/// 文本匹配基于开始/结束标签字符串本身（大小写敏感、精确匹配）。
/// </summary>
public sealed record TagDefinition
{
    /// <summary>短名（菜单与列表中的显示名），如 user_action。</summary>
    public string Name { get; init; } = "";

    /// <summary>开始标签（实际写入文本的字符串），如 [USER_ACTION START]。</summary>
    public string StartTag { get; init; } = "";

    /// <summary>结束标签，如 [USER_ACTION END]。</summary>
    public string EndTag { get; init; } = "";

    /// <summary>包裹方式。</summary>
    public TagWrapMode WrapMode { get; init; } = TagWrapMode.Block;

    /// <summary>是否置顶（菜单与大纲中优先排序）。</summary>
    public bool IsPinned { get; init; }

    public TagDefinition() { }

    public TagDefinition(string name, string startTag, string endTag, TagWrapMode wrapMode, bool isPinned = false)
    {
        Name = name;
        StartTag = startTag;
        EndTag = endTag;
        WrapMode = wrapMode;
        IsPinned = isPinned;
    }

    public override string ToString() => Name;
}
