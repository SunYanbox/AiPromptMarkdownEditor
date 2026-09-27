using System.IO;
using System.Text;

namespace PromptEditor.Services;

/// <summary>文件读写服务：BOM 探测、编码保留、编码转换另存。</summary>
public static class FileService
{
    public sealed record OpenResult(string Text, Encoding Encoding);

    public static readonly IReadOnlyList<EncodingInfo> SupportedEncodings = Encoding.GetEncodings();

    /// <summary>
    /// 读取文本文件：探测 BOM（UTF-8 / UTF-16 LE/BE / UTF-32），无 BOM 时默认按 UTF-8 读取。
    /// 返回文本与实际使用的编码（供保存时保留）。
    /// </summary>
    public static OpenResult OpenText(string path)
    {
        using var fs = File.OpenRead(path);
        var (bomEncoding, bomLength) = DetectBom(fs);
        fs.Position = bomLength;

        Encoding encoding;
        long length = fs.Length - bomLength;
        if (bomEncoding is not null)
        {
            encoding = bomEncoding;
        }
        else
        {
            // 无 BOM：默认 UTF-8
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        using var reader = new StreamReader(fs, encoding);
        string text = reader.ReadToEnd();
        return new OpenResult(text, reader.CurrentEncoding);
    }

    /// <summary>保存文本，使用指定编码（决定是否写 BOM）。</summary>
    public static void SaveText(string path, string text, Encoding encoding)
    {
        // 统一去掉 StreamReader 读取时可能保留的 BOM 字符，BOM 由编码器在文件头写入
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, append: false, encoding);
        writer.Write(text);
    }

    private static (Encoding? encoding, int bomLength) DetectBom(FileStream fs)
    {
        Span<byte> bom = stackalloc byte[4];
        int read = fs.Read(bom);

        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return (new UTF8Encoding(true), 3);
        if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
        if (read >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        if (read >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);

        return (null, 0);
    }

    /// <summary>编码显示名。</summary>
    public static string DisplayName(Encoding e)
    {
        if (e is UTF8Encoding u)
            return u.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8";
        if (e is UnicodeEncoding le)
            return le.GetPreamble().Length > 0 ? "UTF-16 LE" : "UTF-16";
        if (e is UTF32Encoding)
            return "UTF-32";
        if (e is ASCIIEncoding)
            return "ASCII";
        return e.EncodingName;
    }
}
