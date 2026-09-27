using PromptEditorLib.Markdown;
using Xunit;

namespace PromptEditorLib.Tests;

public class HeadingOpsTests
{
    [Fact]
    public void SetHeading_Level3()
    {
        Assert.Equal("### 标题", HeadingOps.SetHeading("标题", 3));
    }

    [Fact]
    public void SetHeading_ReplacesExistingLevel()
    {
        Assert.Equal("## 标题", HeadingOps.SetHeading("##### 标题", 2));
    }

    [Fact]
    public void RemoveHeading_KeepsText()
    {
        Assert.Equal("标题", HeadingOps.SetHeading("### 标题", null));
        // 含闭合井号
        Assert.Equal("标题", HeadingOps.SetHeading("### 标题 ###", null));
    }

    [Fact]
    public void Promote_Demote_Clamp()
    {
        Assert.Equal("# 标题", HeadingOps.Promote("## 标题"));
        Assert.Equal("# 标题", HeadingOps.Promote("# 标题")); // 已是 H1 不变
        Assert.Equal("###### 标题", HeadingOps.Demote("##### 标题"));
        Assert.Equal("###### 标题", HeadingOps.Demote("###### 标题"));
    }

    [Fact]
    public void ToggleHeading()
    {
        Assert.Equal("标题", HeadingOps.ToggleHeading("## 标题", 2));   // 已是 H2 → 移除
        Assert.Equal("### 标题", HeadingOps.ToggleHeading("标题", 3));  // 切到 H3
    }

    [Fact]
    public void NonHeading_Demote_IsNoOp()
    {
        Assert.Equal("普通行", HeadingOps.Demote("普通行"));
    }
}

public class InlineMarkerOpsTests
{
    [Fact]
    public void Bold_WrapAndToggle()
    {
        var (wrapped, removed) = InlineMarkerOps.ToggleMarker("文本", "**");
        Assert.False(removed);
        Assert.Equal("**文本**", wrapped);

        var (unwrapped, removed2) = InlineMarkerOps.ToggleMarker("**文本**", "**");
        Assert.True(removed2);
        Assert.Equal("文本", unwrapped);
    }

    [Fact]
    public void Italic_SingleStar()
    {
        var (t, _) = InlineMarkerOps.ToggleMarker("文本", "*");
        Assert.Equal("*文本*", t);
    }

    [Fact]
    public void Strikethrough()
    {
        var (t, r) = InlineMarkerOps.ToggleMarker("~~文本~~", "~~");
        Assert.Equal("文本", t);
        Assert.True(r);
    }

    [Fact]
    public void TextShorterThanMarkers_DoesNotRemove()
    {
        var (t, r) = InlineMarkerOps.ToggleMarker("**", "**");
        Assert.False(r);
        Assert.Equal("******", t);
    }

    [Fact]
    public void EmptyPair()
    {
        Assert.Equal("****", InlineMarkerOps.EmptyPair("**"));
    }
}
