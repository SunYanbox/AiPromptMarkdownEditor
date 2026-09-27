using PromptEditorLib.Tabs;
using Xunit;

namespace PromptEditorLib.Tests;

/// <summary>
/// 标签显示名同名消歧单元测试（对应需求 8.2 的示例步骤）。
/// </summary>
public class DisplayNameDisambiguatorTests
{
    [Fact]
    public void SingleFile_ShowsFileNameOnly()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\p\docs\a\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal("txt.md", names["1"]);
    }

    [Fact]
    public void Step2_TwoSameNames_ExtendOneLevel()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\p\docs\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\p\docs\b\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal(@"…a\txt.md", names["1"]);
        Assert.Equal(@"…b\txt.md", names["2"]);
    }

    [Fact]
    public void Step3_ThirdCollision_ExtendsDeeperForCollidingOnly()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\p\docs\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\p\docs\b\txt.md"),
            TabNameSource.FromPath("3", @"C:\p\notes\a\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal(@"…docs\a\txt.md", names["1"]);
        Assert.Equal(@"…b\txt.md", names["2"]);
        Assert.Equal(@"…notes\a\txt.md", names["3"]);
    }

    [Fact]
    public void Step4_CloseCollision_ReturnsToShortest()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\p\docs\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\p\docs\b\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal(@"…a\txt.md", names["1"]);
        Assert.Equal(@"…b\txt.md", names["2"]);
    }

    [Fact]
    public void ExtendedName_HasEllipsis_UntilFullPathShown()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\b\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        // 扩展一级后左侧仍有 C:\ 未显示 → 加 …
        Assert.Equal(@"…a\txt.md", names["1"]);
        Assert.Equal(@"…b\txt.md", names["2"]);
    }

    [Fact]
    public void FullyShownPath_NoEllipsis()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\a\other.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        // 不同文件名互不冲突 → 只显示文件名，无 …
        Assert.Equal("txt.md", names["1"]);
        Assert.Equal("other.md", names["2"]);
    }

    [Fact]
    public void UntitledVsFile_FileExtends()
    {
        var tabs = new[]
        {
            TabNameSource.Untitled("u1", "未命名.md"),
            TabNameSource.FromPath("1", @"C:\p\未命名.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal("未命名.md", names["u1"]);
        Assert.Equal(@"…p\未命名.md", names["1"]);
    }

    [Fact]
    public void Untitled_NeverGetsEllipsis()
    {
        var tabs = new[] { TabNameSource.Untitled("u1", "未命名.md") };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal("未命名.md", names["u1"]);
    }

    [Fact]
    public void Fallback_AppendsNumber_WhenFullPathStillDuplicated()
    {
        // 同一路径出现在两个标签（理论上被 9 节去重阻止，测试兜底逻辑）
        var tabs = new[]
        {
            TabNameSource.FromPath("1", @"C:\a\txt.md"),
            TabNameSource.FromPath("2", @"C:\a\txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal(@"C:\a\txt.md", names["1"]);
        Assert.Equal(@"C:\a\txt.md 2", names["2"]);
    }

    [Fact]
    public void ForwardSlashPath_IsNormalized()
    {
        var tabs = new[]
        {
            TabNameSource.FromPath("1", "C:/p/docs/a/txt.md"),
            TabNameSource.FromPath("2", "C:/p/docs/b/txt.md"),
        };
        var names = DisplayNameDisambiguator.Compute(tabs);
        Assert.Equal(@"…a\txt.md", names["1"]);
        Assert.Equal(@"…b\txt.md", names["2"]);
    }
}
