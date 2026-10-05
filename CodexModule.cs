using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soot;

internal sealed class CodexModule : IPetPanelModule
{
    public string Title => "Codex";

    public FrameworkElement CreateView(PetPanelContext context)
    {
        var content = new StackPanel { Margin = new Thickness(10) };
        content.Children.Add(new TextBlock
        {
            Text = "Open your existing Codex sidebar in VS Code.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 2, 0, 12)
        });

        var open = new Button { Content = "Open Codex Sidebar", Padding = new Thickness(9, 7, 9, 7) };
        open.Click += (_, _) => context.OpenCodex();
        content.Children.Add(open);
        return content;
    }

    public void SetPanelVisible(bool visible) { }
}
