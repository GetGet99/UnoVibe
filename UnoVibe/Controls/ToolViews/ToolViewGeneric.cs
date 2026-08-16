using UnoVibe.Models;

namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    required PartItem Part;
    bool InputExpanded = false;
    bool OutputExpanded = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Spacing=4>
        <ToolViewTitle Part=`Part` Text=`ToolViewShared.Generic(Part)` />
        if (`Part.ToolInput.Length > 0`)
        {
            <Border Background=`theme.LayerFill` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`InputExpanded ? Part.ToolInput : ToolViewShared.GenericInputCollapsed(Part)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
            if (`ToolViewShared.GenericInputOverflow(Part)`)
                <Button Background=`theme.LayerFill` BorderThickness=0 CornerRadius=4 Padding=`new Thickness(8, 2, 8, 2)` HorizontalAlignment=Left Click+=`(s, e) => InputExpanded = !InputExpanded`>
                    <TextBlock Text=`InputExpanded ? "Show less ▴" : "Show more ▾"` FontSize=11 FontFamily="Consolas" Foreground=`theme.TertiaryText` />
                </Button>
        }
        if (`Part.ToolOutput.Length > 0`)
        {
            <Border Background=`theme.LayerFill` CornerRadius=4 Padding=`new Thickness(8, 6, 8, 6)`>
                <TextBlock Text=`OutputExpanded ? Part.ToolOutput : ToolViewShared.GenericOutputCollapsed(Part)` FontSize=12 FontFamily="Consolas" TextWrapping=Wrap IsTextSelectionEnabled=true />
            </Border>
            if (`ToolViewShared.GenericOutputOverflow(Part)`)
                <Button Background=`theme.LayerFill` BorderThickness=0 CornerRadius=4 Padding=`new Thickness(8, 2, 8, 2)` HorizontalAlignment=Left Click+=`(s, e) => OutputExpanded = !OutputExpanded`>
                    <TextBlock Text=`OutputExpanded ? "Show less ▴" : "Show more ▾"` FontSize=11 FontFamily="Consolas" Foreground=`theme.TertiaryText` />
                </Button>
        }
        if (`Part.ToolError.Length > 0`)
            <TextBlock Text=`Part.ToolError` FontSize=11 FontFamily="Consolas" Foreground=`theme.SystemCritical` TextWrapping=Wrap IsTextSelectionEnabled=true />
    </StackPanel>
    """)]
public partial class ToolViewGeneric : IQuickMarkupComponent;
