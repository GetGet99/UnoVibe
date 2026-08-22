using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using UnoVibe.Services;
    using QuickMarkup.WinUI;
    inject ChatStore Store;
    required PartItem Part;
    bool Expanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <Border Background=`Part.ToolStatus == "error" ? theme.SystemCriticalBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
            <StackPanel Spacing=2>
                if (`WorkdirLabel().Length > 0`)
                    <TextBlock Text=`"Running in " + WorkdirLabel()` FontSize=11 Foreground=`theme.TertiaryText`
                                TextWrapping=Wrap IsTextSelectionEnabled=true />
                <ToolViewTitle Part=`Part` Text=`ToolViewShared.Shell(Part)` SemiBold=true Emphasized=true CodeFont=`Part.ToolCommand.Length > 0` />
            </StackPanel>
        </Border>
        if (`Part.ShellOutput.Length > 0`)
        {
            <Border Background=`theme.LayerFill` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Expanded ? Part.ShellOutput : ToolViewShared.ShellCollapsed(Part)` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
            if (`ToolViewShared.ShellOverflow(Part)`)
                <Button Background=`theme.LayerFill` BorderThickness=0 CornerRadius=4 Padding=`new Thickness(8, 2, 8, 2)` HorizontalAlignment=Left Click+=`(s, e) => Expanded = !Expanded`>
                    <TextBlock Text=`Expanded ? "Show less ▴" : "Show more ▾"` FontSize=11 Foreground=`theme.TertiaryText` />
                </Button>
        }
        if (`Part.ToolError.Length > 0`)
            <Border Background=`theme.SystemCriticalBackground` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`Part.ToolError` FontSize=12 Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
    </StackPanel>
    """)]
public partial class ToolViewShell : IQuickMarkupComponent
{
    /// <summary>
    /// The "Running in …" label above the command: the tool's workdir relative to the
    /// session's directory (same reference <see cref="ChatStore.ActiveDirectory"/> uses),
    /// empty when it matches that directory. Rendered in the default font so it reads as
    /// context, not code.
    /// </summary>
    private string WorkdirLabel()
    {
        var dir = Store.ActiveDirectory();
        if (dir.Length == 0) dir = Store.ServerDirectory;
        return ToolViewShared.ShellWorkdir(Part, dir);
    }
}