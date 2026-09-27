using System.Windows;
using System.Windows.Media;
using PromptEditor.Services;

namespace PromptEditor.Views;

public partial class SettingsWindow : Window
{
    /// <summary>确定后为 true，调用方读取 Settings 并应用。</summary>
    public bool Saved { get; private set; }
    public AppSettings Settings { get; } = App.Settings;

    public SettingsWindow()
    {
        InitializeComponent();
        var s = Settings;

        foreach (var family in Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            FontBox.Items.Add(family.Source);
        FontBox.Text = s.FontFamily;

        FontSizeBox.Text = s.FontSize.ToString();
        TabSizeBox.Text = s.TabSize.ToString();
        TabCharCheck.IsChecked = s.TabUsesTabChar;
        WrapCheck.IsChecked = s.SoftWrap;
        LineNumbersCheck.IsChecked = s.ShowLineNumbers;
        EolBox.SelectedIndex = s.DefaultLineEnding.ToUpperInvariant() switch
        {
            "CRLF" => 1,
            "CR" => 2,
            _ => 0,
        };
        ThemeBox.SelectedIndex = s.Theme == "Light" ? 1 : 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(FontSizeBox.Text, out double size) || size < 8 || size > 72)
        {
            MessageBox.Show(this, "字号必须是 8~72 的数字。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(TabSizeBox.Text, out int tabSize) || tabSize < 1 || tabSize > 16)
        {
            MessageBox.Show(this, "Tab 宽度必须是 1~16 的整数。", "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Settings.FontFamily = FontBox.Text is { Length: > 0 } f ? f : "Consolas";
        Settings.FontSize = size;
        Settings.TabSize = tabSize;
        Settings.TabUsesTabChar = TabCharCheck.IsChecked == true;
        Settings.SoftWrap = WrapCheck.IsChecked == true;
        Settings.ShowLineNumbers = LineNumbersCheck.IsChecked == true;
        Settings.DefaultLineEnding = EolBox.SelectedIndex switch { 1 => "CRLF", 2 => "CR", _ => "LF" };
        Settings.Theme = ThemeBox.SelectedIndex == 1 ? "Light" : "Dark";

        Saved = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
