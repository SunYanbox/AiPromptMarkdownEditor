using PromptEditorLib.Text;
using Xunit;

namespace PromptEditorLib.Tests;

public class LineEndingUtilTests
{
    [Fact]
    public void Detect_PureLf()
    {
        var info = LineEndingUtil.Detect("a\nb\nc");
        Assert.Equal(LineEnding.Lf, info.Dominant);
        Assert.False(info.Mixed);
        Assert.Equal(2, info.LfCount);
    }

    [Fact]
    public void Detect_PureCrLf()
    {
        var info = LineEndingUtil.Detect("a\r\nb\r\nc");
        Assert.Equal(LineEnding.Crlf, info.Dominant);
        Assert.False(info.Mixed);
        Assert.Equal(2, info.CrLfCount);
    }

    [Fact]
    public void Detect_PureCr()
    {
        var info = LineEndingUtil.Detect("a\rb\rc");
        Assert.Equal(LineEnding.Cr, info.Dominant);
        Assert.False(info.Mixed);
    }

    [Fact]
    public void Detect_Mixed_FindsFirstMixedLine()
    {
        // 第 1 行 CRLF（主流），第 2 行 LF，第 4 行 CR
        var text = "1\r\n2\n3\r\n4\r5\r\n";
        var info = LineEndingUtil.Detect(text);
        Assert.True(info.Mixed);
        Assert.Equal(LineEnding.Crlf, info.Dominant);
        Assert.Equal(2, info.FirstMixedLine);
    }

    [Fact]
    public void Unify_ToLf()
    {
        Assert.Equal("a\nb\nc", LineEndingUtil.Unify("a\r\nb\rc", LineEnding.Lf));
    }

    [Fact]
    public void Unify_ToCrLf()
    {
        Assert.Equal("a\r\nb\r\nc", LineEndingUtil.Unify("a\nb\rc", LineEnding.Crlf));
    }

    [Fact]
    public void Unify_ToCr()
    {
        Assert.Equal("a\rb\rc", LineEndingUtil.Unify("a\r\nb\nc", LineEnding.Cr));
    }

    [Fact]
    public void Unify_DoesNotMutateOriginal()
    {
        var text = "a\r\nb";
        _ = LineEndingUtil.Unify(text, LineEnding.Lf);
        Assert.Equal("a\r\nb", text);
    }

    [Fact]
    public void Detect_Empty()
    {
        var info = LineEndingUtil.Detect("");
        Assert.False(info.Mixed);
    }
}

public class TokenEstimatorTests
{
    [Fact]
    public void EmptyText_Zero()
    {
        Assert.Equal(0, TokenEstimator.Estimate(""));
    }

    [Fact]
    public void Chinese_AboutOneTokenPerChar()
    {
        // 100 个中文字 → 约 80 token（0.6~1 区间的估算）
        var text = new string('中', 100);
        long tokens = TokenEstimator.Estimate(text);
        Assert.InRange(tokens, 60, 100);
    }

    [Fact]
    public void Ascii_AboutFourCharsPerToken()
    {
        var text = new string('a', 100);
        Assert.Equal(25, TokenEstimator.Estimate(text));
    }
}
