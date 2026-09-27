namespace PromptEditorLib.Tabs;

/// <summary>标签显示名来源：Id 用于标识标签；FullPath 为规范后的完整路径（未命名标签为 null）。</summary>
public sealed record TabNameSource(string Id, string? FullPath, string FileName)
{
    /// <summary>把路径规范化为 Windows 风格（\ 分隔）。</summary>
    public static TabNameSource FromPath(string id, string fullPath) =>
        new(id, fullPath.Replace('/', '\\'), Path.GetFileName(fullPath));

    public static TabNameSource Untitled(string id, string fileName) => new(id, null, fileName);
}

/// <summary>
/// 标签显示名同名消歧（纯函数）：
/// 1. 所有标签按显示名分组，凡存在重名，对每个仍重名的「文件标签」把显示名向左扩展一级父目录（基于规范后的完整路径）；
/// 2. 重复上一步直到全部显示名互不重复；比较范围是所有标签（扩展出的名字可能与其他标签撞名，需一并处理）；
/// 3. 未命名标签无路径、不参与扩展；与文件标签同名时由文件标签扩展路径解决；
/// 4. 兜底：扩展到完整绝对路径仍重名时在末尾追加序号（" 2"、" 3"…）。
/// 「…」前缀：显示名包含目录且左侧仍有未显示的路径部分时加「…」；已显示到完整路径或只显示文件名时不加。
/// </summary>
public static class DisplayNameDisambiguator
{
    public static IReadOnlyDictionary<string, string> Compute(IEnumerable<TabNameSource> tabs)
    {
        var list = tabs.ToList();
        // 每个文件标签当前显示的路径段数（1 = 仅文件名），最多为路径总段数
        var levels = list.ToDictionary(t => t.Id, _ => 1);
        var depth = list.ToDictionary(t => t.Id, t => t.FullPath is null ? 1 : t.FullPath.Split('\\').Length);

        string BaseName(TabNameSource t)
        {
            if (t.FullPath is null) return t.FileName;
            int lv = Math.Min(levels[t.Id], depth[t.Id]);
            var parts = t.FullPath.Split('\\');
            return string.Join('\\', parts[^lv..]);
        }

        while (true)
        {
            bool extended = false;
            var duplicateGroups = list
                .GroupBy(BaseName, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Where(g => g.Any(t => t.FullPath is not null))
                .ToList();

            foreach (var group in duplicateGroups)
            {
                foreach (var t in group.Where(t => t.FullPath is not null))
                {
                    if (levels[t.Id] < depth[t.Id])
                    {
                        levels[t.Id]++;
                        extended = true;
                    }
                }
            }

            if (!extended) break;
        }

        // 兜底：扩展到完整路径仍重名 → 追加序号
        var results = new Dictionary<string, string>();
        var suffixCounters = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in list)
        {
            string name = BaseName(t);
            if (t.FullPath is not null)
            {
                bool hasDir = levels[t.Id] > 1;
                bool moreLeft = levels[t.Id] < depth[t.Id];
                if (hasDir && moreLeft) name = "…" + name;
            }

            if (results.Values.Contains(name, StringComparer.Ordinal))
            {
                int n = suffixCounters.TryGetValue(name, out var c) ? c + 1 : 2;
                suffixCounters[name] = n;
                name = $"{name} {n}";
            }
            results[t.Id] = name;
        }
        return results;
    }
}
