using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using PromptEditorLib.Markdown;
using PromptEditorLib.Tags;
using PromptEditorLib.Text;
using PromptEditor.ViewModels;

namespace PromptEditor.Services;

/// <summary>
/// 编辑操作（作用于当前文档），全部通过 Document.Replace 实现单步可撤销。
/// </summary>
public static class EditorOps
{
    private static TextDocument Doc(DocumentViewModel d) => d.Document;

    /// <summary>TextViewPosition → 文档偏移。</summary>
    private static int Off(TextDocument doc, TextViewPosition pos) => doc.GetOffset(pos.Line, pos.Column);

    // ---------- 标签 ----------

    /// <summary>快速包裹 / 插入空标签对。返回是否执行了修改。</summary>
    public static bool WrapWithTag(DocumentViewModel docVm, TagDefinition tag)
    {
        var area = docVm.Editor.TextArea;
        var doc = Doc(docVm);

        int selStart = area.Selection.IsEmpty
            ? area.Caret.Offset
            : Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));
        int selLength = area.Selection.IsEmpty ? 0
            : Math.Abs(Off(doc, area.Selection.EndPosition) - Off(doc, area.Selection.StartPosition));

        var (newText, caret) = TagEditOps.Wrap(doc.Text, selStart, selLength, tag);

        // newText = 前缀(不变) + 插入内容 + 后缀(不变)，替换选区即可
        int insertLen = newText.Length - doc.TextLength + selLength;
        string inserted = newText.Substring(selStart, insertLen);
        doc.Replace(selStart, selLength, inserted);

        if (selLength == 0)
            area.Caret.Offset = caret; // 空选区：落在标签中间
        else
            docVm.Editor.Select(selStart, insertLen);
        return true;
    }

    /// <summary>移除最内层配对标签（光标在标签对内部或选区完整包含一对）。</summary>
    public static bool RemoveTag(DocumentViewModel docVm, IReadOnlyList<TagDefinition> tags)
    {
        var area = docVm.Editor.TextArea;
        var doc = Doc(docVm);
        int selStart = area.Selection.IsEmpty ? area.Caret.Offset
            : Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));
        int selEnd = area.Selection.IsEmpty ? selStart
            : Math.Max(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));

        var pairs = TagPairScanner.FindPairs(doc.Text, tags);
        var pair = TagPairScanner.FindInnermostPairForRemoval(pairs, selStart, selEnd);
        if (pair is null) return false;

        int caret = pair.Start.Index;
        using (doc.RunUpdate())
        {
            doc.Remove(pair.End.Index, pair.End.Length);
            doc.Remove(pair.Start.Index, pair.Start.Length);
        }
        area.Caret.Offset = Math.Min(caret, doc.TextLength);
        return true;
    }

    // ---------- 标题 ----------

    public static void SetHeading(DocumentViewModel docVm, int? level)
    {
        var doc = Doc(docVm);
        ReplaceSelectedLines(docVm, line => HeadingOps.SetHeading(line, level));
    }

    public static void ToggleHeading(DocumentViewModel docVm, int level)
        => ReplaceSelectedLines(docVm, line => HeadingOps.ToggleHeading(line, level));

    public static void ApplyHeadingLevel(DocumentViewModel docVm, int level) => ToggleHeading(docVm, level);

    public static void PromoteHeading(DocumentViewModel docVm) => ReplaceSelectedLines(docVm, HeadingOps.Promote);

    public static void DemoteHeading(DocumentViewModel docVm) => ReplaceSelectedLines(docVm, HeadingOps.Demote);

    // ---------- 行内标记 ----------

    public static void ToggleInlineMarker(DocumentViewModel docVm, string marker)
    {
        var area = docVm.Editor.TextArea;
        var doc = Doc(docVm);

        if (area.Selection.IsEmpty)
        {
            // 插入空标记，光标放中间
            doc.Insert(area.Caret.Offset, marker + marker);
            area.Caret.Offset += marker.Length;
            return;
        }

        int selStart = Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));
        int selLength = Math.Abs(Off(doc, area.Selection.EndPosition) - Off(doc, area.Selection.StartPosition));
        string selected = doc.GetText(selStart, selLength);
        var (newText, removed) = InlineMarkerOps.ToggleMarker(selected, marker);
        doc.Replace(selStart, selLength, newText);
        docVm.Editor.Select(selStart, newText.Length);
    }

    // ---------- 列表 ----------

    public static void ToggleUnorderedList(DocumentViewModel docVm)
        => ReplaceSelectedLines(docVm, null, lines => ListOps.ToggleUnordered(lines));

    public static void ToggleOrderedList(DocumentViewModel docVm)
        => ReplaceSelectedLines(docVm, null, lines => ListOps.ToggleOrdered(lines));

    /// <summary>引用：每行切换 "&gt; " 前缀。</summary>
    public static void ToggleQuote(DocumentViewModel docVm)
        => ReplaceSelectedLines(docVm, line =>
        {
            if (line.StartsWith("> ")) return line[2..];
            if (line == ">") return "";
            return "> " + line;
        });

    /// <summary>代码块：选区行前后包 ``` 围栏；无选区时插入空围栏对。</summary>
    public static void ToggleCodeBlock(DocumentViewModel docVm)
    {
        var doc = Doc(docVm);
        var area = docVm.Editor.TextArea;

        if (area.Selection.IsEmpty)
        {
            int offset = area.Caret.Offset;
            doc.Insert(offset, "```\n\n```");
            // 光标移到围栏内的空行
            area.Caret.Offset = offset + 4;
            area.Caret.BringCaretToView();
            return;
        }

        int startOffset = Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));
        int endOffset = Math.Max(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));

        int firstLineNo = doc.GetLineByOffset(startOffset).LineNumber;
        int lastLineNo = doc.GetLineByOffset(endOffset).LineNumber;

        // 已是代码块（首行是 ``` 围栏）则剥掉
        var firstText = doc.GetText(doc.GetLineByNumber(firstLineNo));
        if (firstText.TrimStart().StartsWith("```"))
        {
            ReplaceSelectedLines(docVm, null, lines =>
            {
                var result = new List<string>(lines);
                if (result.Count > 0 && result[0].TrimStart().StartsWith("```")) result.RemoveAt(0);
                if (result.Count > 0 && result[^1].TrimStart().StartsWith("```")) result.RemoveAt(result.Count - 1);
                return result;
            });
            return;
        }

        var wrapped = doc.GetText(doc.GetLineByNumber(firstLineNo).Offset,
            doc.GetLineByNumber(lastLineNo).EndOffset - doc.GetLineByNumber(firstLineNo).Offset);
        var lines2 = wrapped.Split('\n');
        var sb = new System.Text.StringBuilder();
        sb.Append("```\n");
        for (int i = 0; i < lines2.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines2[i].TrimEnd('\r'));
        }
        sb.Append("\n```");
        doc.Replace(doc.GetLineByNumber(firstLineNo).Offset,
            doc.GetLineByNumber(lastLineNo).EndOffset - doc.GetLineByNumber(firstLineNo).Offset, sb.ToString());
    }

    /// <summary>Tab / Shift+Tab：列表行调整层级；非列表行返回 false 由默认 Tab 处理。</summary>
    public static bool IndentSelection(DocumentViewModel docVm, bool indent)
    {
        var doc = Doc(docVm);
        var area = docVm.Editor.TextArea;

        int firstLineNo = doc.GetLineByOffset(area.Selection.IsEmpty ? area.Caret.Offset
            : Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition))).LineNumber;
        int lastLineNo = doc.GetLineByOffset(area.Selection.IsEmpty ? area.Caret.Offset
            : Math.Max(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition))).LineNumber;

        // 收缩选区到行首时，最后一行不参与
        var endLine = doc.GetLineByOffset(Math.Max(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition)));
        if (!area.Selection.IsEmpty && Off(doc, area.Selection.EndPosition) == endLine.Offset && lastLineNo > firstLineNo)
            lastLineNo--;

        bool anyList = false;
        for (int no = firstLineNo; no <= lastLineNo; no++)
        {
            var line = doc.GetLineByNumber(no);
            string text = doc.GetText(line);
            if (ListOps.IsListItem(text)) { anyList = true; break; }
        }
        if (!anyList) return false;

        var newLines = new List<string>();
        var oldLines = new List<string>();
        for (int no = firstLineNo; no <= lastLineNo; no++)
        {
            var line = doc.GetLineByNumber(no);
            string text = doc.GetText(line);
            oldLines.Add(text);
            newLines.Add(indent ? ListOps.Indent(text) : ListOps.Unindent(text));
        }
        ReplaceLines(docVm, firstLineNo, oldLines, newLines);
        return true;
    }

    // ---------- 表格 ----------

    public static bool CaretInTable(DocumentViewModel docVm, out (int StartLine, int EndLine) range)
    {
        var doc = Doc(docVm);
        int lineNo = doc.GetLineByOffset(docVm.Editor.TextArea.Caret.Offset).LineNumber;
        var lines = doc.Text.Split('\n');
        var (start, end) = TableOps.FindTableRange(lines, lineNo - 1);
        bool ok = start <= end && TableOps.IsTableLine(lines[start]);
        range = (start + 1, end + 1);
        return ok;
    }

    public static void InsertTableTemplate(DocumentViewModel docVm, int rows, int cols, IReadOnlyList<string> headers)
    {
        var doc = Doc(docVm);
        int caret = docVm.Editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(caret);
        bool atLineStart = caret == line.Offset;

        string template = TableOps.CreateTable(rows, cols, headers);
        if (!atLineStart && line.Length > 0)
            template = "\n" + template;
        template += "\n";
        doc.Insert(caret, template);
        // 光标置于第一个单元格
        docVm.Editor.TextArea.Caret.Offset = caret + (template.StartsWith("\n") ? 1 : 0) + 2;
    }

    public static void AddTableRow(DocumentViewModel docVm)
    {
        if (!CaretInTable(docVm, out var range)) return;
        TransformTable(docVm, range, lines => TableOps.AddRow(lines, docVm.Document.GetLineByOffset(docVm.Editor.TextArea.Caret.Offset).LineNumber - range.StartLine));
    }

    public static void RemoveTableRow(DocumentViewModel docVm)
    {
        if (!CaretInTable(docVm, out var range)) return;
        TransformTable(docVm, range, lines => TableOps.RemoveRow(lines, docVm.Document.GetLineByOffset(docVm.Editor.TextArea.Caret.Offset).LineNumber - range.StartLine));
    }

    public static void AddTableColumn(DocumentViewModel docVm)
    {
        if (!CaretInTable(docVm, out var range)) return;
        TransformTable(docVm, range, lines => TableOps.AddColumn(lines, TableOps.ColumnCount(lines)));
    }

    public static void RemoveTableColumn(DocumentViewModel docVm)
    {
        if (!CaretInTable(docVm, out var range)) return;
        int col = ColumnOfCaret(docVm);
        TransformTable(docVm, range, lines => TableOps.RemoveColumn(lines, col));
    }

    public static void RemoveTable(DocumentViewModel docVm)
    {
        if (!CaretInTable(docVm, out var range)) return;
        TransformTable(docVm, range, TableOps.ToPlainText);
    }

    private static int ColumnOfCaret(DocumentViewModel docVm)
    {
        var doc = Doc(docVm);
        var line = doc.GetLineByOffset(docVm.Editor.TextArea.Caret.Offset);
        int offsetInLine = docVm.Editor.TextArea.Caret.Offset - line.Offset;
        string text = doc.GetText(line);
        // 数光标前未转义的 | 个数
        int col = 0;
        for (int i = 0; i < Math.Min(offsetInLine, text.Length); i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '|') { i++; continue; }
            if (text[i] == '|') col++;
        }
        return Math.Max(0, col - 1);
    }

    private static void TransformTable(DocumentViewModel docVm, (int StartLine, int EndLine) range,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> transform)
    {
        var doc = Doc(docVm);
        var oldLines = new List<string>();
        for (int no = range.StartLine; no <= range.EndLine; no++)
            oldLines.Add(doc.GetText(doc.GetLineByNumber(no)));

        var newLines = transform(oldLines);
        ReplaceLines(docVm, range.StartLine, oldLines, newLines.ToList());
    }

    // ---------- 行尾统一 ----------

    /// <summary>统一行尾为指定类型：逐行替换分隔符，RunUpdate 内为单个可撤销操作。</summary>
    public static void UnifyLineEndings(DocumentViewModel docVm, LineEnding target)
    {
        var doc = Doc(docVm);
        string sep = target switch
        {
            LineEnding.Crlf => "\r\n",
            LineEnding.Cr => "\r",
            _ => "\n",
        };
        using (doc.RunUpdate())
        {
            // 最后一行没有分隔符；只替换与目标不一致的行尾
            for (int no = 1; no < doc.LineCount; no++)
            {
                var line = doc.GetLineByNumber(no);
                if (line.DelimiterLength > 0 && doc.GetText(line.EndOffset, line.DelimiterLength) != sep)
                    doc.Replace(line.EndOffset, line.DelimiterLength, sep);
            }
        }
    }

    // ---------- 通用行替换 ----------

    /// <summary>对选区覆盖（或光标所在）的行逐行执行转换。</summary>
    public static void ReplaceSelectedLines(DocumentViewModel docVm, Func<string, string> lineTransform)
        => ReplaceSelectedLines(docVm, lineTransform, null);

    public static void ReplaceSelectedLines(DocumentViewModel docVm,
        Func<string, string>? lineTransform,
        Func<IReadOnlyList<string>, IReadOnlyList<string>>? blockTransform)
    {
        var doc = Doc(docVm);
        var area = docVm.Editor.TextArea;

        int startOffset = area.Selection.IsEmpty ? area.Caret.Offset
            : Math.Min(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));
        int endOffset = area.Selection.IsEmpty ? area.Caret.Offset
            : Math.Max(Off(doc, area.Selection.StartPosition), Off(doc, area.Selection.EndPosition));

        int firstLineNo = doc.GetLineByOffset(startOffset).LineNumber;
        int lastLineNo = doc.GetLineByOffset(endOffset).LineNumber;
        var endLine = doc.GetLineByOffset(endOffset);
        if (!area.Selection.IsEmpty && endOffset == endLine.Offset && lastLineNo > firstLineNo)
            lastLineNo--; // 选区正好结束在行首，最后一行不参与

        var oldLines = new List<string>();
        for (int no = firstLineNo; no <= lastLineNo; no++)
            oldLines.Add(doc.GetText(doc.GetLineByNumber(no)));

        List<string> newLines;
        if (blockTransform is not null)
        {
            newLines = blockTransform(oldLines).ToList();
        }
        else if (lineTransform is not null)
        {
            newLines = oldLines.Select(lineTransform).ToList();
        }
        else
        {
            return;
        }

        ReplaceLines(docVm, firstLineNo, oldLines, newLines);
    }

    private static void ReplaceLines(DocumentViewModel docVm, int firstLineNo,
        IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        if (oldLines.SequenceEqual(newLines)) return;

        var doc = Doc(docVm);
        var firstLine = doc.GetLineByNumber(firstLineNo);
        var lastLine = doc.GetLineByNumber(firstLineNo + oldLines.Count - 1);
        int start = firstLine.Offset;
        int length = lastLine.EndOffset - firstLine.Offset;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < newLines.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(newLines[i]);
        }
        doc.Replace(start, length, sb.ToString());
        docVm.Editor.TextArea.Caret.BringCaretToView();
    }
}
