namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    required SessionHead Session;
    inject ChatStore Store;
    inject? bool IsSidebarView;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <Button
        Margin=`new Thickness(0, 4, 0, 0)`
        Padding=`new Thickness(8, 6, 8, 6)`
        HorizontalAlignment=Stretch HorizontalContentAlignment=Left
        @Click+=`SwitchSession()`
        Background=`Store.ActiveSessionId == Session.Id ? theme.ControlFill : transparent`
        ContextFlyout=<MenuFlyout Placement=BottomEdgeAlignedRight>
        if (`Session.IsRead`) {
            <MenuFlyoutItem Text="Mark as unread" @Click+=`Session.IsRead = false` />
        } else {
            <MenuFlyoutItem Text="Mark as read" @Click+=`Session.IsRead = true` />
        }
    </MenuFlyout>>
        <Grid ColumnDefinitions=<>
            <ColumnDefinition Width=Auto />
            <ColumnDefinition />
            <ColumnDefinition Width=Auto />
        </>>
            <SessionIndicator State=`Session.State` Margin=`new Thickness(0, 0, 6, 0)` />
            <TextBlock Grid.Column=1 Text=`Session.Title` FontSize=12 TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
            <TextBlock Grid.Column=2 Text=`Session.TimeLabel` FontSize=10 Foreground=`theme.TertiaryText` Margin=`new Thickness(8, 0, 0, 0)` VerticalAlignment=Center />
        </Grid>
    </Button>
    """)]
partial class SessionButton : IQuickMarkupComponent
{
    void SwitchSession()
    {
        IsSidebarView = false;
        if (Session.Id != Store.CurrentSessionId)
            _ = Store.SwitchSessionAsync(Session.Id);
    }
}
