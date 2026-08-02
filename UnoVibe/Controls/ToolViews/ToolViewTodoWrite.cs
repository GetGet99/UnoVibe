using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    PartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <TextBlock Text=`ToolViewShared.TodoTitle(Part)` FontSize=12 FontFamily="Consolas" Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
        foreach (var todo in `ToolViewShared.ParseTodos(Part)`)
            <TextBlock Text=`ToolViewShared.TodoLine(todo)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true
                       Foreground=`todo.Status == "in_progress" ? theme.SystemCaution : theme.TertiaryText` />
    </StackPanel>
    """)]
public partial class ToolViewTodoWrite : IQuickMarkupComponent;
