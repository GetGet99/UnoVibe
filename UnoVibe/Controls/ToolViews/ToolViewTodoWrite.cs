namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    required ToolCallPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <ToolViewTitle Part=`Part` Text=`Part.DisplayName` />
        foreach (var todo in `Part.Todos`)
            <TextBlock Text=`ToolViewShared.TodoLine(todo)` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true
                       Foreground=`todo.Status == "in_progress" ? theme.SystemCaution : theme.TertiaryText` />
    </StackPanel>
    """)]
public partial class ToolViewTodoWrite : IQuickMarkupComponent;
