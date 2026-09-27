namespace PromptEditorLib.Markdown;

/// <summary>
/// GFM 表格操作（按行处理，纯逻辑）：
/// 创建模板、定位表格、增删行/列、移除表格（转纯文本行）。
/// </summary>
public static class TableOps
{
    /// <summary>判断一行是否为表格行（行首为 |）。</summary>
    public static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    /// <summary>分隔行（| --- | --- |）。</summary>
    public static bool IsSeparatorLine(string line)
    {
        if (!IsTableLine(line)) return false;
        var cells = ParseCells(line);
        return cells.Count > 0 && cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ':' or ' '));
    }

    /// <summary>生成 GFM 表格模板（含表头行与分隔行）。</summary>
    public static string CreateTable(int rows, int cols, IReadOnlyList<string> headers)
    {
        rows = Math.Max(1, rows);
        cols = Math.Max(1, cols);

        var sb = new System.Text.StringBuilder();
        for (int r = 0; r <= rows; r++) // 0 为表头行
        {
            sb.Append('|');
            for (int c = 0; c < cols; c++)
            {
                string cell = r == 0
                    ? (c < headers.Count ? headers[c] : $"列{c + 1}")
                    : "";
                sb.Append(' ').Append(cell).Append(" |");
            }
            sb.Append('\n');
            if (r == 0)
            {
                sb.Append('|');
                for (int c = 0; c < cols; c++) sb.Append(" --- |");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>解析一行中的单元格（去掉首尾 | 后按 | 切分并 Trim）。</summary>
    public static List<string> ParseCells(string line)
    {
        var trimmed = line.Trim().TrimStart('|').TrimEnd('|');
        // 转义 \| 视作字面竖线的简单处理：先替换为占位符
        const string esc = "\u0001";
        trimmed = trimmed.Replace("\\|", esc);
        return trimmed.Split('|').Select(c => c.Trim().Replace(esc, "|")).ToList();
    }

    private static string BuildRow(IReadOnlyList<string> cells) => "| " + string.Join(" | ", cells) + " |";

    /// <summary>在表格中插入一行：光标在表头行时插到分隔行之后，否则插到当前行之后。</summary>
    public static List<string> AddRow(IReadOnlyList<string> tableLines, int caretIndex)
    {
        var result = tableLines.ToList();
        if (result.Count == 0) return result;
        int idx = Math.Clamp(caretIndex, 0, result.Count - 1);

        int insertAt;
        if (idx == 0 && result.Count > 1 && IsSeparatorLine(result[1]))
            insertAt = 2;               // 光标在表头 → 分隔行之后
        else if (IsSeparatorLine(result[idx]))
            insertAt = idx + 1;         // 光标在分隔行 → 其后
        else
            insertAt = idx + 1;         // 数据行 → 其下

        insertAt = Math.Min(insertAt, result.Count);
        int cols = ColumnCount(result);
        result.Insert(insertAt, BuildRow(Enumerable.Repeat("", cols).ToList()));
        return result;
    }

    /// <summary>删除指定行（分隔行不可删除，返回原表）。</summary>
    public static List<string> RemoveRow(IReadOnlyList<string> tableLines, int index)
    {
        var result = tableLines.ToList();
        if (index >= 0 && index < result.Count && !IsSeparatorLine(result[index]))
            result.RemoveAt(index);
        return result;
    }

    /// <summary>在指定列后插入一列（newColumnIndex 为新列的位置，0-based）。</summary>
    public static List<string> AddColumn(IReadOnlyList<string> tableLines, int newColumnIndex)
    {
        var result = new List<string>(tableLines.Count);
        foreach (var line in tableLines)
        {
            var cells = ParseCells(line);
            int idx = Math.Clamp(newColumnIndex, 0, cells.Count);
            if (IsSeparatorLine(line))
                cells.Insert(idx, "---");
            else
                cells.Insert(idx, "");
            result.Add(BuildRow(cells));
        }
        return result;
    }

    /// <summary>删除指定列（仅剩 1 列时不删除，返回原表）。</summary>
    public static List<string> RemoveColumn(IReadOnlyList<string> tableLines, int columnIndex)
    {
        if (ColumnCount(tableLines) <= 1) return tableLines.ToList();
        var result = new List<string>(tableLines.Count);
        foreach (var line in tableLines)
        {
            var cells = ParseCells(line);
            if (columnIndex >= 0 && columnIndex < cells.Count) cells.RemoveAt(columnIndex);
            result.Add(BuildRow(cells));
        }
        return result;
    }

    /// <summary>移除表格：转为纯文本行，保留单元格文字（以两个空格分隔）。</summary>
    public static List<string> ToPlainText(IReadOnlyList<string> tableLines)
    {
        var result = new List<string>(tableLines.Count);
        foreach (var line in tableLines)
        {
            if (IsSeparatorLine(line)) continue;
            var cells = ParseCells(line);
            result.Add(string.Join("  ", cells));
        }
        return result;
    }

    /// <summary>表格列数（取第一行单元格数）。</summary>
    public static int ColumnCount(IReadOnlyList<string> tableLines) =>
        tableLines.Count == 0 ? 0 : ParseCells(tableLines[0]).Count;

    /// <summary>在给定的行列表中，从 caretLine（0-based）向上/向下扩展出完整表格范围。</summary>
    public static (int Start, int End) FindTableRange(IReadOnlyList<string> lines, int caretLine)
    {
        int start = Math.Min(caretLine, lines.Count - 1);
        while (start > 0 && IsTableLine(lines[start - 1])) start--;
        int end = start;
        while (end + 1 < lines.Count && IsTableLine(lines[end + 1])) end++;
        return (start, end);
    }
}
