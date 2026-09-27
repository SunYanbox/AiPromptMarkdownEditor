using PromptEditorLib.Markdown;
using PromptEditorLib.Tags;
using Xunit;

namespace PromptEditorLib.Tests;

public class FoldingCalculatorTests
{
    private static readonly List<TagDefinition> Tags =
    [
        new("system", "<system>", "</system>", TagWrapMode.Block),
    ];

    [Fact]
    public void TagPair_ProducesFoldRegion()
    {
        var text = "前\n<system>\n内容1\n内容2\n</system>\n后";
        var regions = FoldingCalculator.Calculate(text, Tags);
        Assert.Contains(regions, r =>
            r.StartOffset == text.IndexOf("<system>") &&
            r.EndOffset == text.IndexOf("</system>") + "</system>".Length);
    }

    [Fact]
    public void UnpairedTag_NoFoldRegion()
    {
        var text = "<system>没有结束标签";
        var regions = FoldingCalculator.Calculate(text, Tags);
        Assert.DoesNotContain(regions, r => r.Title == "<system>");
    }

    [Fact]
    public void CodeFence_ProducesFoldRegion()
    {
        var text = "a\n```csharp\ncode\n```\nb";
        var regions = FoldingCalculator.Calculate(text, Tags);
        Assert.Contains(regions, r =>
            r.StartOffset == text.IndexOf("```") && r.EndOffset == text.IndexOf("\nb") + 1);
    }

    [Fact]
    public void HeadingSection_EndsBeforeNextSameOrHigherHeading()
    {
        var text = "# A\n内容A\n## B\n内容B\n# C\n内容C";
        var regions = FoldingCalculator.Calculate(text, Tags);
        // "# A" 的章节应到 "# C" 行首；"## B" 的章节也到 "# C" 行首
        var aSection = regions.First(r => r.StartOffset == text.IndexOf("内容A"));
        Assert.Equal(text.IndexOf("# C"), aSection.EndOffset);
        var bSection = regions.First(r => r.StartOffset == text.IndexOf("内容B"));
        Assert.Equal(text.IndexOf("# C"), bSection.EndOffset);
        // "# C" 的章节到文末
        var cSection = regions.First(r => r.StartOffset == text.IndexOf("内容C"));
        Assert.Equal(text.Length, cSection.EndOffset);
    }

    [Fact]
    public void Regions_CanNest_ButNotPartiallyOverlap()
    {
        var text = "# A\n<system>\n内容\n</system>\n尾部";
        var regions = FoldingCalculator.Calculate(text, Tags);
        foreach (var r in regions)
            Assert.True(r.IsValid);
        // 无部分重叠
        for (int i = 0; i < regions.Count; i++)
            for (int j = i + 1; j < regions.Count; j++)
            {
                var a = regions[i]; var b = regions[j];
                bool nested = b.StartOffset >= a.StartOffset && b.EndOffset <= a.EndOffset;
                bool disjoint = b.EndOffset <= a.StartOffset || b.StartOffset >= a.EndOffset;
                Assert.True(nested || disjoint, $"区域 {a} 与 {b} 部分重叠");
            }
    }
}

public class OutlineBuilderTests
{
    private static readonly List<TagDefinition> Tags =
    [
        new("system", "<system>", "</system>", TagWrapMode.Block),
        new("user", "<user>", "</user>", TagWrapMode.Block),
    ];

    [Fact]
    public void ExtractsHeadings_WithLineNumbers()
    {
        var md = "正文\n## 二级标题\n更多";
        var items = OutlineBuilder.Build(md, Tags);
        var h = Assert.Single(items, i => !i.IsTag);
        Assert.Equal(1, h.Line);
        Assert.Equal(2, h.Level);
        Assert.Equal("二级标题", h.Title);
    }

    [Fact]
    public void ExtractsTopLevelTags()
    {
        var md = "<system>\n内容\n</system>\n<user>x</user>";
        var items = OutlineBuilder.Build(md, Tags);
        var tags = items.Where(i => i.IsTag).ToList();
        Assert.Equal(2, tags.Count);
        Assert.Equal("system", tags[0].TagName);
        Assert.Equal(0, tags[0].Line);
        Assert.Equal("user", tags[1].TagName);
        Assert.Equal(3, tags[1].Line);
    }

    [Fact]
    public void HeadingText_StripsClosingHashes()
    {
        var md = "### 标题 ###";
        var items = OutlineBuilder.Build(md, Tags);
        Assert.Equal("标题", Assert.Single(items).Title);
    }
}
