namespace UnoVibe.Controls;

[QuickMarkup("""
    required Symbol Symbol;
    required string Tooltip;
    double IconSize = 11;
    <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip=`Tooltip`>
        <AppSymbolIcon Symbol=`Symbol` FontSize=`IconSize` />
    </Button>
    """)]
partial class SmallButtonIcon : IQuickMarkupComponent<Button>;
