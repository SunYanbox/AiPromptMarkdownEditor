using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PromptEditor.ViewModels;

namespace PromptEditor.Views;

public partial class UnsavedFilesWindow : Window
{
    public MultiCloseDecision Decision { get; private set; } = MultiCloseDecision.Cancel;

    public UnsavedFilesWindow(IEnumerable<DocumentViewModel> docs)
    {
        InitializeComponent();
        foreach (var doc in docs)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = "●",
                Foreground = new SolidColorBrush(Colors.Orange),
                Margin = new Thickness(0, 0, 6, 0)
            });
            row.Children.Add(new TextBlock
            {
                Text = doc.FilePath is not null
                    ? $"{doc.DisplayTitle}  —  {doc.FilePath}"
                    : doc.DisplayTitle,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            FilesList.Items.Add(row);
        }
    }

    private void OnSaveAll(object sender, RoutedEventArgs e) { Decision = MultiCloseDecision.SaveAll; DialogResult = true; }
    private void OnDiscardAll(object sender, RoutedEventArgs e) { Decision = MultiCloseDecision.DiscardAll; DialogResult = true; }
    private void OnCancel(object sender, RoutedEventArgs e) { Decision = MultiCloseDecision.Cancel; DialogResult = false; }
}
