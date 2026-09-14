namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.States;
    using Microsoft.UI;
    inject ChatMessagesState? ChatState;
    inject bool IsCompact;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <Button
        Background=`transparent`
        BorderThickness=0
        Padding=`new Thickness(8,  2, 8,  2)`
        CornerRadius=6 VerticalAlignment=Center
        ToolTipService.ToolTip="Session stats"
        Flyout=<Flyout Placement=Bottom>
            <StackPanel Spacing=8 MinWidth=260>
                <TextBlock Text="Session stats" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                <Border Background=`theme.DividerStroke` Height=1 />
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Cost" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`$"${ChatState?.Cost ?? 0:F2}"` FontSize=12 TextAlignment=Right VerticalAlignment=Center />
                </Grid>
                <TextBlock Text="Tokens" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Input*" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.Input ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Output*" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.Output ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                </Grid>
                if (`(ChatState?.Tokens.Reasoning ?? 0) > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Reasoning*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.Reasoning ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                if (`(ChatState?.Tokens.CacheRead ?? 0) > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Cache read*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.CacheRead ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                if (`(ChatState?.Tokens.CacheWrite ?? 0) > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Cache write*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.CacheWrite ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Total" FontSize=12 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.TokensTotal ?? 0).ToString("N0")` FontSize=12 FontWeight=`FontWeights.SemiBold` TextAlignment=Right />
                </Grid>
                <TextBlock Text="*based on last message" FontSize=11 Foreground=`theme.TertiaryText` />
                <TextBlock Text="Context" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Used" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`(ChatState?.Tokens.TokensTotal ?? 0).ToString("N0")` FontSize=12 TextAlignment=Right />
                </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Max" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`ChatState?.ContextLimit > 0 ? ChatState!.ContextLimit.ToString("N0") : "--"` FontSize=12 TextAlignment=Right />
                </Grid>
                <ProgressBar Value=`(ChatState?.ContextLimit > 0 ? Math.Round((double)(ChatState?.Tokens.TokensTotal ?? 0) / ChatState!.ContextLimit * 100) : 0)` Minimum=0 Maximum=100 Height=4 />
            </StackPanel>
        </Flyout>
    >
        // On compact the inline cost summary lives on the second header line, so the stats
        // button itself shrinks to a "more details" icon (the flyout stays reachable).
        if (`IsCompact`)
        {
            <AppSymbolIcon Symbol=More FontSize=11 Foreground=`theme.SecondaryText` />
        }
        else
        {
            <ChatCostInline />
        }
    </Button>
    """)]
partial class ChatCost : IQuickMarkupComponent
{
    
}

[QuickMarkup("""
    using UnoVibe.States;
    inject ChatMessagesState? ChatState;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <StackPanel Orientation=Horizontal Spacing=8>
        <TextBlock Text=`$"${ChatState?.Cost ?? 0:F2}"` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text=`(ChatState?.Tokens.TokensTotal ?? 0).ToString("N0")` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="tokens" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text=`ChatState?.ContextLimit > 0 ? $"{Math.Round((double)(ChatState?.Tokens.TokensTotal ?? 0) / ChatState!.ContextLimit * 100)}%" : "--"` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="ctx" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <ProgressBar Value=`(ChatState?.ContextLimit > 0 ? Math.Round((double)(ChatState?.Tokens.TokensTotal ?? 0) / ChatState!.ContextLimit * 100) : 0)` Minimum=0 Maximum=100 Width=70 Height=4 VerticalAlignment=Center />
    </StackPanel>
    """)]
partial class ChatCostInline : IQuickMarkupComponent
{
    
}
