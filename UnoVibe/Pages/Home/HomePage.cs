namespace UnoVibe.Pages.Home;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject HomeProvider Home;
    inject bool IsCompact;
    inject bool IsSidebarView;
    bool HasPins => `Home.CardsOrdered.Any()`;
    bool ShowEmpty => `!HasPins && !Home.IsCreatingSession`;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <ScrollViewer>
        <StackPanel Padding=`new Thickness(IsCompact ? 12 : 16, 12, IsCompact ? 12 : 16, 16)` Spacing=8 HorizontalAlignment=Stretch>
            if (`IsCompact`)
            {
                <StackPanel Orientation=Horizontal Spacing=8>
                    <Button Background=`transparent` BorderThickness=0 Padding=`new Thickness(6, 2, 6, 2)` CornerRadius=6
                            Foreground=`theme.SecondaryText` VerticalAlignment=Center @Click+=`IsSidebarView = true`
                            ToolTipService.ToolTip="Open session list">
                        <AppSymbolIcon Symbol=`Symbol.GlobalNavButton` FontSize=14 />
                    </Button>
                    <TextBlock Text="Home" FontSize=14 FontWeight=`FontWeights.SemiBold` Foreground=`theme.PrimaryText` VerticalAlignment=Center />
                </StackPanel>
            }
            if (`!Home.IsCreatingSession`)
            {
                <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Center Padding=`new Thickness(8, 6, 8, 6)` CornerRadius=6
                        @Click+=`Home.StartCreating()` ToolTipService.ToolTip="Start a new session">
                    <StackPanel Orientation=Horizontal Spacing=6>
                        <AppSymbolIcon Symbol=Add FontSize=12 VerticalAlignment=Center />
                        <TextBlock Text="New session" FontSize=12 VerticalAlignment=Center />
                    </StackPanel>
                </Button>
            }
            else
            {
                <HomeNewSessionCard />
            }
            if (`HasPins`)
            {
                foreach (var card in `Home.CardsOrdered`; `card.SessionId.Id`)
                {
                    <HomeCard Card=`card` />
                }
            }
            else
            {
                if (`ShowEmpty`)
                {
                    <StackPanel Spacing=6 Padding=`new Thickness(0, 24, 0, 0)` HorizontalAlignment=Center>
                        <TextBlock Text="No pinned chats" FontSize=14 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextAlignment=Center />
                        <TextBlock Text="Pin a chat from the sidebar menu to manage it here, or use the button above to start a new chat." FontSize=12 Foreground=`theme.TertiaryText` TextAlignment=Center TextWrapping=Wrap MaxWidth=420 />
                    </StackPanel>
                }
            }
        </StackPanel>
    </ScrollViewer>
    """)]
partial class HomePage : IQuickMarkupComponent;
