namespace PromptEditorLib.Text;

/// <summary>行尾类型。</summary>
public enum LineEnding
{
    Lf,
    Crlf,
    Cr
}

/// <summary>行尾检测结果。</summary>
public sealed record LineEndingInfo(
    LineEnding Dominant,
    bool Mixed,
    int FirstMixedLine,   // 首个与主流行尾不一致的行号（1-based），无混合时为 -1
    int CrLfCount,
    int LfCount,
    int CrCount)
{
    public static string NameOf(LineEnding e) => e switch
    {
        LineEnding.Crlf => "CRLF",
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => "?"
    };
}

/// <summary>
/// 行尾检测与统一（纯逻辑）。
/// </summary>
public static class LineEndingUtil
{
    /// <summary>统计并检测全文行尾。 Dominant 取数量最多者；数量并列时按 CRLF &gt; LF &gt; CR。</summary>
    public static LineEndingInfo Detect(string text)
    {
        int crlf = 0, lf = 0, cr = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (c == '\n') lf++;
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return new LineEndingInfo(LineEnding.Lf, false, -1, 0, 0, 0);

        LineEnding dominant =
            crlf >= lf && crlf >= cr ? LineEnding.Crlf :
            lf >= cr ? LineEnding.Lf : LineEnding.Cr;

        bool mixed = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0) > 1;
        int firstMixedLine = mixed ? FindFirstMixedLine(text, dominant) : -1;

        return new LineEndingInfo(dominant, mixed, firstMixedLine, crlf, lf, cr);
    }

    /// <summary>找首个行尾类型与主流类型不同的行（1-based）。</summary>
    public static int FindFirstMixedLine(string text, LineEnding dominant)
    {
        int line = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                bool isCrlf = i + 1 < text.Length && text[i + 1] == '\n';
                var type = isCrlf ? LineEnding.Crlf : LineEnding.Cr;
                if (type != dominant) return line;
                if (isCrlf) i++;
                line++;
            }
            else if (c == '\n')
            {
                if (LineEnding.Lf != dominant) return line;
                line++;
            }
        }
        return -1;
    }

    /// <summary>全文统一为目标行尾，返回新字符串（原串不变）。</summary>
    public static string Unify(string text, LineEnding target)
    {
        string sep = target switch
        {
            LineEnding.Crlf => "\r\n",
            LineEnding.Cr => "\r",
            _ => "\n",
        };

        var sb = new System.Text.StringBuilder(text.Length + 8);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\r')
            {
                sb.Append(sep);
                i += (i + 1 < text.Length && text[i + 1] == '\n') ? 2 : 1;
            }
            else if (c == '\n')
            {
                sb.Append(sep);
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>解析设置中的默认行尾字符串（"CRLF"/"LF"/"CR"）。</summary>
    public static LineEnding ParseOrDefault(string? name, LineEnding fallback = LineEnding.Lf) =>
        name?.ToUpperInvariant() switch
        {
            "CRLF" => LineEnding.Crlf,
            "LF" => LineEnding.Lf,
            "CR" => LineEnding.Cr,
            _ => fallback,
        };
}
