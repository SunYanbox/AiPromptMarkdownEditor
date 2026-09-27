using System.IO;
using System.Text;
using System.Text.Json;

namespace PromptEditor.Services;

/// <summary>应用设置（持久化到本地 JSON）。</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Light";
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 14;
    public int TabSize { get; set; } = 4;
    /// <summary>Tab 键插入制表符（默认插入空格）。</summary>
    public bool TabUsesTabChar { get; set; }
    public bool SoftWrap { get; set; }
    public bool ShowLineNumbers { get; set; } = true;
    /// <summary>默认行尾格式：LF / CRLF / CR。</summary>
    public string DefaultLineEnding { get; set; } = "LF";
    public bool ShowLineEndMarkers { get; set; }
    public double WindowX { get; set; } = double.NaN;
    public double WindowY { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public List<string> RecentFiles { get; set; } = new();
}

/// <summary>JSON 持久化存储（设置 / 标签库 / 最近文件共用）。</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>%APPDATA%\AiPromptMarkdownEditor</summary>
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AiPromptMarkdownEditor");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");
    public static string TagsPath => Path.Combine(AppDataDir, "tags.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // 设置损坏时回退默认值
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 保存失败不致命
        }
    }
}
