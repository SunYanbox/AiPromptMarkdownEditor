using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptEditorLib.Tags;

/// <summary>
/// 校验结果：错误（阻止保存）与警告（提示但允许保存）。
/// </summary>
public sealed record TagValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// 标签库：内置预设 + 用户自定义条目，持久化为本地 JSON，支持导入/导出与恢复默认。
/// </summary>
public sealed class TagLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<TagDefinition> Tags { get; set; } = new();

    /// <summary>默认内置预设（均为可编辑/可删除的普通条目）。</summary>
    public static List<TagDefinition> DefaultTags() =>
    [
        // ===== 对话角色 =====
        new TagDefinition("system", "<system>", "</system>", TagWrapMode.Block, true),
        new TagDefinition("system_reminder", "<system_reminder>", "</system_reminder>", TagWrapMode.Block, true),
        new TagDefinition("developer", "<developer>", "</developer>", TagWrapMode.Block),
        new TagDefinition("user", "<user>", "</user>", TagWrapMode.Block, true),
        new TagDefinition("assistant", "<assistant>", "</assistant>", TagWrapMode.Block, true),
        new TagDefinition("tool", "<tool>", "</tool>", TagWrapMode.Block),
        new TagDefinition("human", "<human>", "</human>", TagWrapMode.Block),
        new TagDefinition("ai", "<ai>", "</ai>", TagWrapMode.Block),

        // ===== 角色、指令与上下文 =====
        new TagDefinition("role", "<role>", "</role>", TagWrapMode.Block),
        new TagDefinition("persona", "<persona>", "</persona>", TagWrapMode.Block),
        new TagDefinition("instructions", "<instructions>", "</instructions>", TagWrapMode.Block, true),
        new TagDefinition("rules", "<rules>", "</rules>", TagWrapMode.Block),
        new TagDefinition("constraints", "<constraints>", "</constraints>", TagWrapMode.Block),
        new TagDefinition("context", "<context>", "</context>", TagWrapMode.Block, true),
        new TagDefinition("background", "<background>", "</background>", TagWrapMode.Block),
        new TagDefinition("task", "<task>", "</task>", TagWrapMode.Block),
        new TagDefinition("goal", "<goal>", "</goal>", TagWrapMode.Block),

        // ===== 示例与少样本 =====
        new TagDefinition("example", "<example>", "</example>", TagWrapMode.Block),
        new TagDefinition("examples", "<examples>", "</examples>", TagWrapMode.Block),
        new TagDefinition("few_shot", "<few_shot>", "</few_shot>", TagWrapMode.Block),

        // ===== 输入与数据 =====
        new TagDefinition("user_input", "<user_input>", "</user_input>", TagWrapMode.Block, true),
        new TagDefinition("user_query", "<user_query>", "</user_query>", TagWrapMode.Block),
        new TagDefinition("question", "<question>", "</question>", TagWrapMode.Block),
        new TagDefinition("document", "<document>", "</document>", TagWrapMode.Block),
        new TagDefinition("document_content", "<document_content>", "</document_content>", TagWrapMode.Block),
        new TagDefinition("data", "<data>", "</data>", TagWrapMode.Block),
        new TagDefinition("source", "<source>", "</source>", TagWrapMode.Block),
        new TagDefinition("quotes", "<quotes>", "</quotes>", TagWrapMode.Block),
        new TagDefinition("file", "<file>", "</file>", TagWrapMode.Block),
        new TagDefinition("code", "<code>", "</code>", TagWrapMode.Block),
        new TagDefinition("code_block", "<code_block>", "</code_block>", TagWrapMode.Block),

        // ===== 推理与输出 =====
        new TagDefinition("thinking", "<thinking>", "</thinking>", TagWrapMode.Block, true),
        new TagDefinition("thought", "<thought>", "</thought>", TagWrapMode.Block),
        new TagDefinition("scratchpad", "<scratchpad>", "</scratchpad>", TagWrapMode.Block),
        new TagDefinition("plan", "<plan>", "</plan>", TagWrapMode.Block),
        new TagDefinition("reflection", "<reflection>", "</reflection>", TagWrapMode.Block),
        new TagDefinition("critique", "<critique>", "</critique>", TagWrapMode.Block),
        new TagDefinition("answer", "<answer>", "</answer>", TagWrapMode.Block, true),
        new TagDefinition("final_answer", "<final_answer>", "</final_answer>", TagWrapMode.Block),
        new TagDefinition("output", "<output>", "</output>", TagWrapMode.Block),
        new TagDefinition("output_format", "<output_format>", "</output_format>", TagWrapMode.Block),
        new TagDefinition("output_schema", "<output_schema>", "</output_schema>", TagWrapMode.Block),
        new TagDefinition("formatting", "<formatting>", "</formatting>", TagWrapMode.Block),
        new TagDefinition("json", "<json>", "</json>", TagWrapMode.Block),

        // ===== 工具、函数与观察 =====
        new TagDefinition("tool_call", "<tool_call>", "</tool_call>", TagWrapMode.Block),
        new TagDefinition("tool_result", "<tool_result>", "</tool_result>", TagWrapMode.Block),
        new TagDefinition("function_call", "<function_call>", "</function_call>", TagWrapMode.Block),
        new TagDefinition("function_response", "<function_response>", "</function_response>", TagWrapMode.Block),
        new TagDefinition("observation", "<observation>", "</observation>", TagWrapMode.Block),
        new TagDefinition("action", "<action>", "</action>", TagWrapMode.Block),

        // ===== 记忆、元数据与系统提醒 =====
        new TagDefinition("memory", "<memory>", "</memory>", TagWrapMode.Block),
        new TagDefinition("metadata", "<metadata>", "</metadata>", TagWrapMode.Block),
        new TagDefinition("reminder", "<reminder>", "</reminder>", TagWrapMode.Block),
        new TagDefinition("note", "<note>", "</note>", TagWrapMode.Block),
        new TagDefinition("warning", "<warning>", "</warning>", TagWrapMode.Block),
        new TagDefinition("error", "<error>", "</error>", TagWrapMode.Block),
        new TagDefinition("ide_opened_file", "<ide_opened_file>", "</ide_opened_file>", TagWrapMode.Block),
        new TagDefinition("ide_selection", "<ide_selection>", "</ide_selection>", TagWrapMode.Block),
        new TagDefinition("local_command_caveat", "<local-command-caveat>", "</local-command-caveat>", TagWrapMode.Block),
        new TagDefinition("command_name", "<command-name>", "</command-name>", TagWrapMode.Block),

        // ===== 行内变量/属性 =====
        new TagDefinition("name", "<name>", "</name>", TagWrapMode.Inline),
        new TagDefinition("user_name", "<user_name>", "</user_name>", TagWrapMode.Inline),
        new TagDefinition("date", "<date>", "</date>", TagWrapMode.Inline),
        new TagDefinition("time", "<time>", "</time>", TagWrapMode.Inline),
        new TagDefinition("variable", "<variable>", "</variable>", TagWrapMode.Inline),
        new TagDefinition("value", "<value>", "</value>", TagWrapMode.Inline),
        new TagDefinition("placeholder", "<placeholder>", "</placeholder>", TagWrapMode.Inline),
        new TagDefinition("language", "<language>", "</language>", TagWrapMode.Inline),
        new TagDefinition("tone", "<tone>", "</tone>", TagWrapMode.Inline),
        new TagDefinition("style", "<style>", "</style>", TagWrapMode.Inline),
        new TagDefinition("audience", "<audience>", "</audience>", TagWrapMode.Inline),
        new TagDefinition("length", "<length>", "</length>", TagWrapMode.Inline),
        new TagDefinition("max_words", "<max_words>", "</max_words>", TagWrapMode.Inline),
    ];

    public static TagLibrary CreateDefault() => new() { Tags = DefaultTags() };

    /// <summary>
    /// 校验标签库。
    /// 错误：短名为空/重复、开始或结束标签为空、开始与结束标签相同。
    /// 警告：某开始标签与其他标签的开始标签重复（导致配对检测歧义），允许保存。
    /// </summary>
    public TagValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var seenStarts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
                errors.Add("短名不能为空。");
            else if (!seenNames.Add(tag.Name))
                errors.Add($"短名重复：{tag.Name}");

            if (string.IsNullOrEmpty(tag.StartTag) || string.IsNullOrEmpty(tag.EndTag))
                errors.Add($"标签 “{tag.Name}” 的开始/结束标签不能为空。");
            else if (string.Equals(tag.StartTag, tag.EndTag, StringComparison.Ordinal))
                errors.Add($"标签 “{tag.Name}” 的开始标签与结束标签不能相同。");
            else if (seenStarts.TryGetValue(tag.StartTag, out var other))
                warnings.Add($"“{tag.Name}” 的开始标签与 “{other}” 重复，可能导致配对检测歧义。");
            else
                seenStarts[tag.StartTag] = tag.Name;
        }

        return new TagValidationResult(errors, warnings);
    }

    /// <summary>菜单/大纲排序：置顶优先，其余按短名排序。</summary>
    public IEnumerable<TagDefinition> OrderedForMenu() =>
        Tags.OrderByDescending(t => t.IsPinned).ThenBy(t => t.Name, StringComparer.Ordinal);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static TagLibrary FromJson(string json) =>
        JsonSerializer.Deserialize<TagLibrary>(json, JsonOptions) ?? CreateDefault();

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson());
    }

    public static TagLibrary Load(string path) => FromJson(File.ReadAllText(path));
}
