using PromptEditorLib.Tags;
using Xunit;

namespace PromptEditorLib.Tests;

public class TagPairScannerTests
{
    private static readonly List<TagDefinition> Tags =
    [
        new("system", "<system>", "</system>", TagWrapMode.Block),
        new("user", "<user>", "</user>", TagWrapMode.Block),
    ];

    [Fact]
    public void FindsPair_InText()
    {
        var text = "前\n<system>内容</system>\n后";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        var pair = Assert.Single(pairs);
        Assert.Equal(text.IndexOf("<system>"), pair.Start.Index);
        Assert.Equal(text.IndexOf("</system>"), pair.End.Index);
    }

    [Fact]
    public void NestedPairs_AreAllFound()
    {
        var text = "<system><user>inner</user>rest</system>";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        Assert.Equal(2, pairs.Count);
    }

    [Fact]
    public void UnmatchedStart_IsDropped()
    {
        var text = "<system>没有结束";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        Assert.Empty(pairs);
    }

    [Fact]
    public void UnmatchedEnd_IsIgnored()
    {
        var text = "没有开始</system>";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        Assert.Empty(pairs);
    }

    [Fact]
    public void Matching_IsCaseSensitive()
    {
        var text = "<SYSTEM>大写</system>";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        Assert.Empty(pairs);
    }

    [Fact]
    public void InnermostPairAt_CursorInsideContent()
    {
        var text = "<system><user>x</user></system>";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        int cursor = text.IndexOf("x");
        var pair = TagPairScanner.FindInnermostPairAt(pairs, cursor);
        Assert.NotNull(pair);
        Assert.Equal("user", pair!.Start.Def.Name);
    }

    [Fact]
    public void InnermostPairAt_CursorOnStartTag()
    {
        var text = "<system>abc</system>";
        var pairs = TagPairScanner.FindPairs(text, Tags);
        int cursor = text.IndexOf("<s");
        var pair = TagPairScanner.FindInnermostPairAt(pairs, cursor);
        Assert.NotNull(pair);
        Assert.Equal("system", pair!.Start.Def.Name);
    }
}

public class TagEditOpsTests
{
    private static readonly TagDefinition Block = new("system", "<system>", "</system>", TagWrapMode.Block);
    private static readonly TagDefinition Inline = new("em", "[EM START]", "[EM END]", TagWrapMode.Inline);

    [Fact]
    public void Wrap_Block_WithSelection()
    {
        var text = "abc选中def";
        int start = text.IndexOf("选中");
        var (newText, caret) = TagEditOps.Wrap(text, start, 2, Block);
        Assert.Equal("abc<system>\n选中\n</system>def", newText);
        // 光标位于内容之后（结束标签前）
        Assert.Equal(newText.IndexOf("</system>"), caret);
    }

    [Fact]
    public void Wrap_Block_NoSelection_CaretInMiddleBlankLine()
    {
        var (newText, caret) = TagEditOps.Wrap("abc", 3, 0, Block);
        Assert.Equal("abc<system>\n\n</system>", newText);
        Assert.Equal(newText.IndexOf("\n", newText.IndexOf("<system>")) + 1, caret);
    }

    [Fact]
    public void Wrap_Inline_WithSelection()
    {
        var text = "ab选c";
        int start = text.IndexOf("选");
        var (newText, caret) = TagEditOps.Wrap(text, start, 1, Inline);
        Assert.Equal("ab[EM START]选[EM END]c", newText);
        Assert.Equal(newText.IndexOf("[EM END]"), caret);
    }

    [Fact]
    public void RemoveTag_KeepsContent()
    {
        var text = "a\n<system>内容\n更多</system>\nb";
        var tags = new List<TagDefinition> { Block };
        int caret = text.IndexOf("内容");
        var (newText, _) = TagEditOps.RemoveTag(text, caret, 0, tags);
        Assert.Equal("a\n内容\n更多\nb", newText);
    }

    [Fact]
    public void RemoveTag_SelectionContainsPair()
    {
        var text = "<system>内容</system>";
        var tags = new List<TagDefinition> { Block };
        var (newText, _) = TagEditOps.RemoveTag(text, 0, text.Length, tags);
        Assert.Equal("内容", newText);
    }

    [Fact]
    public void RemoveTag_Innermost_WhenNested()
    {
        var tags = new List<TagDefinition> { Block, new("u", "<user>", "</user>", TagWrapMode.Block) };
        var text = "<system><user>内</user></system>";
        int caret = text.IndexOf("内");
        var (newText, _) = TagEditOps.RemoveTag(text, caret, 0, tags);
        Assert.Equal("<system>内</system>", newText);
    }

    [Fact]
    public void RemoveTag_NoPair_ReturnsOriginal()
    {
        var tags = new List<TagDefinition> { Block };
        var text = "普通文本";
        var (newText, caret) = TagEditOps.RemoveTag(text, 0, 0, tags);
        Assert.Equal(text, newText);
        Assert.Equal(0, caret);
    }
}

public class TagLibraryValidationTests
{
    [Fact]
    public void DefaultTags_AreValid()
    {
        var lib = TagLibrary.CreateDefault();
        var result = lib.Validate();
        Assert.False(result.HasErrors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EmptyName_IsError()
    {
        var lib = new TagLibrary { Tags = [new TagDefinition("", "<a>", "</a>", TagWrapMode.Block)] };
        Assert.True(lib.Validate().HasErrors);
    }

    [Fact]
    public void SameStartEnd_IsError()
    {
        var lib = new TagLibrary { Tags = [new TagDefinition("a", "<x>", "<x>", TagWrapMode.Block)] };
        Assert.True(lib.Validate().HasErrors);
    }

    [Fact]
    public void DuplicateStart_IsWarningNotError()
    {
        var lib = new TagLibrary
        {
            Tags =
            [
                new TagDefinition("a", "<x>", "</x>", TagWrapMode.Block),
                new TagDefinition("b", "<x>", "</y>", TagWrapMode.Block),
            ]
        };
        var result = lib.Validate();
        Assert.False(result.HasErrors);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void DuplicateName_IsError()
    {
        var lib = new TagLibrary
        {
            Tags =
            [
                new TagDefinition("a", "<x>", "</x>", TagWrapMode.Block),
                new TagDefinition("a", "<y>", "</y>", TagWrapMode.Block),
            ]
        };
        Assert.True(lib.Validate().HasErrors);
    }

    [Fact]
    public void JsonRoundTrip()
    {
        var lib = TagLibrary.CreateDefault();
        var restored = TagLibrary.FromJson(lib.ToJson());
        Assert.Equal(lib.Tags.Count, restored.Tags.Count);
        Assert.Equal(lib.Tags[0].StartTag, restored.Tags[0].StartTag);
    }
}
