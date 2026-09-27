namespace PromptEditorLib.Text;

/// <summary>
/// 粗略 token 估算：中文（CJK）按 1 字 ≈ 0.6~1 token，取 0.8；
/// 其余字符按约 4 字符 ≈ 1 token（0.25）。结果必须标注「估算值」。
/// </summary>
public static class TokenEstimator
{
    public static long Estimate(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        long cjk = 0, other = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                int code = char.ConvertToUtf32(text, i);
                if ((code >= 0x20000 && code <= 0x2FA1F) || (code >= 0x30000 && code <= 0x3134F))
                {
                    cjk++; i++;
                }
                else other += 2;
            }
            else if (IsBmpCjk(text[i])) cjk++;
            else other++;
        }
        return (long)Math.Ceiling(cjk * 0.8 + other * 0.25);
    }

    private static bool IsBmpCjk(char c)
    {
        // CJK 统一表意文字、扩展 A、兼容表意文字
        return (c >= 0x4E00 && c <= 0x9FFF)
            || (c >= 0x3400 && c <= 0x4DBF)
            || (c >= 0xF900 && c <= 0xFAFF);
    }
}
