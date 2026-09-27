using System.Windows.Input;

namespace PromptEditor.ViewModels;

/// <summary>「最近文件」菜单条目：显示绝对路径，点击打开对应文件。</summary>
public sealed class RecentFileCommand
{
    public string Header { get; }
    public ICommand Command { get; }

    public RecentFileCommand(string header, ICommand command)
    {
        Header = header;
        Command = command;
    }
}
