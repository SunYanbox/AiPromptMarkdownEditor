using PromptEditorLib.Markdown;
using Xunit;

namespace PromptEditorLib.Tests;

public class ListOpsTests
{
    [Fact]
    public void ToggleUnordered_AddsThenRemoves()
    {
        var lines = new[] { "苹果", "香蕉" };
        var added = ListOps.ToggleUnordered(lines);
        Assert.Equal(new[] { "- 苹果", "- 香蕉" }, added);

        var removed = ListOps.ToggleUnordered(added);
        Assert.Equal(lines, removed);
    }

    [Fact]
    public void ToggleUnordered_PreservesIndent()
    {
        var added = ListOps.ToggleUnordered(new[] { "  子项" });
        Assert.Equal("  - 子项", added[0]);

        var removed = ListOps.ToggleUnordered(added);
        Assert.Equal("  子项", removed[0]);
    }

    [Fact]
    public void ToggleOrdered_AddsThenRemoves()
    {
        var added = ListOps.ToggleOrdered(new[] { "第一", "第二" });
        Assert.Equal(new[] { "1. 第一", "2. 第二" }, added);

        var removed = ListOps.ToggleOrdered(added);
        Assert.Equal(new[] { "第一", "第二" }, removed);
    }

    [Fact]
    public void ToggleOrdered_ContinuesFromExistingNumber()
    {
        var added = ListOps.ToggleOrdered(new[] { "3. 已有", "新行" });
        Assert.Equal(new[] { "3. 已有", "4. 新行" }, added);
    }

    [Fact]
    public void RenumberOrdered_RenumbersWholeBlock()
    {
        var lines = new List<string> { "# 标题", "1. 甲", "2. 乙", "9. 丙", "其他" };
        var result = ListOps.RenumberOrdered(lines, 3); // 编辑了 "9. 丙"
        Assert.Equal(new[] { "# 标题", "1. 甲", "2. 乙", "3. 丙", "其他" }, result);
    }

    [Fact]
    public void RenumberOrdered_UsesBlockStartNumber()
    {
        var lines = new List<string> { "5. 甲", "5. 乙" };
        var result = ListOps.RenumberOrdered(lines, 1);
        Assert.Equal(new[] { "5. 甲", "6. 乙" }, result);
    }

    [Fact]
    public void IndentAndUnindent()
    {
        Assert.Equal("    项目", ListOps.Indent("项目"));
        Assert.Equal("项目", ListOps.Unindent("    项目"));
        Assert.Equal("项目", ListOps.Unindent("  项目")); // 不足一个缩进单位时全移除
    }
}

public class TableOpsTests
{
    [Fact]
    public void CreateTable_WithHeaders()
    {
        var table = TableOps.CreateTable(2, 2, new[] { "名称", "值" });
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // 表头 + 分隔 + 2 数据行
        Assert.Equal("| 名称 | 值 |", lines[0]);
        Assert.Equal("| --- | --- |", lines[1]);
        Assert.Equal("|  |  |", lines[2]);
    }

    [Fact]
    public void CreateTable_FillsMissingHeaders()
    {
        var lines = TableOps.CreateTable(1, 3, new[] { "A" }).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("| A | 列2 | 列3 |", lines[0]);
    }

    [Fact]
    public void AddRow_AfterSeparator()
    {
        var lines = new List<string>
        {
            "| A | B |",
            "| --- | --- |",
            "| 1 | 2 |",
        };
        var result = TableOps.AddRow(lines, 0); // 参考行是表头 → 插到分隔行之后
        Assert.Equal(4, result.Count);
        Assert.Equal("|  |  |", result[2]);
    }

    [Fact]
    public void RemoveRow_SkipsSeparator()
    {
        var lines = new List<string> { "| A | B |", "| --- | --- |", "| 1 | 2 |" };
        var result = TableOps.RemoveRow(lines, 1); // 分隔行不删
        Assert.Equal(3, result.Count);
        var result2 = TableOps.RemoveRow(lines, 2);
        Assert.Equal(2, result2.Count);
    }

    [Fact]
    public void AddColumn_AppendsToAllLines()
    {
        var lines = new List<string> { "| A | B |", "| --- | --- |" };
        var result = TableOps.AddColumn(lines, 2);
        Assert.Equal("| A | B |  |", result[0]);
        Assert.Equal("| --- | --- | --- |", result[1]);
    }

    [Fact]
    public void RemoveColumn()
    {
        var lines = new List<string> { "| A | B | C |", "| --- | --- | --- |", "| 1 | 2 | 3 |" };
        var result = TableOps.RemoveColumn(lines, 1);
        Assert.Equal("| A | C |", result[0]);
        Assert.Equal("| 1 | 3 |", result[2]);
    }

    [Fact]
    public void RemoveColumn_LastColumn_NotRemoved()
    {
        var lines = new List<string> { "| A |" };
        Assert.Equal(lines, TableOps.RemoveColumn(lines, 0));
    }

    [Fact]
    public void ToPlainText_KeepsCellText()
    {
        var lines = new List<string> { "| A | B |", "| --- | --- |", "| 1 | 2 |" };
        var result = TableOps.ToPlainText(lines);
        Assert.Equal(new[] { "A  B", "1  2" }, result);
    }

    [Fact]
    public void EscapedPipe_IsLiteral()
    {
        var cells = TableOps.ParseCells("| a\\|b | c |");
        Assert.Equal(2, cells.Count);
        Assert.Equal("a|b", cells[0]);
    }

    [Fact]
    public void FindTableRange()
    {
        var lines = new List<string> { "前言", "| A |", "| --- |", "| 1 |", "后记" };
        var (start, end) = TableOps.FindTableRange(lines, 2);
        Assert.Equal(1, start);
        Assert.Equal(3, end);
    }
}
