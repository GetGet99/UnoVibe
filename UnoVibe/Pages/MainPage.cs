namespace UnoVibe.Pages;

/// <summary>
/// Root page: session sidebar on the left, chat page on the right.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.Pages;
    using QuickMarkup.WinUI;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid Background=`theme.SolidBackground` ColumnDefinitions=<>
            <ColumnDefinition Width=280 />
            <ColumnDefinition />
        </>>
            <SessionSidebar />
            <ChatPage Grid.Column=1 />
        </Grid>
    </root>
    """)]
public partial class MainPage : Page
{
}
