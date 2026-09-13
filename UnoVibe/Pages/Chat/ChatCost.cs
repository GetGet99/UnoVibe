namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.Controls;
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
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageCostLabel` FontSize=12 TextAlignment=Right VerticalAlignment=Center />
                </Grid>
                <TextBlock Text=`StoreToUpdate.SubagentCount > 0 ? "Tokens (excludes subagents)" : "Tokens"` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Input*" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensInput.ToString("N0")` FontSize=12 TextAlignment=Right />
                </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Output*" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensOutput.ToString("N0")` FontSize=12 TextAlignment=Right />
                </Grid>
                if (`StoreToUpdate.Active.UsageTokensReasoning > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Reasoning*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensReasoning.ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                if (`StoreToUpdate.Active.UsageTokensCacheRead > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Cache read*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensCacheRead.ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                if (`StoreToUpdate.Active.UsageTokensCacheWrite > 0`)
                    <Grid ColumnSpacing=12 ColumnDefinitions=<>
                        <ColumnDefinition Width=96 />
                        <ColumnDefinition />
                    </>>
                        <TextBlock Text="Cache write*" FontSize=12 Foreground=`theme.SecondaryText` />
                        <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensCacheWrite.ToString("N0")` FontSize=12 TextAlignment=Right />
                    </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Total" FontSize=12 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensLabel` FontSize=12 FontWeight=`FontWeights.SemiBold` TextAlignment=Right />
                </Grid>
                <TextBlock Text="*based on last message" FontSize=11 Foreground=`theme.TertiaryText` />
                <TextBlock Text="Context" FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.TertiaryText` />
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Used" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.UsageTokensLabel` FontSize=12 TextAlignment=Right />
                </Grid>
                <Grid ColumnSpacing=12 ColumnDefinitions=<>
                    <ColumnDefinition Width=96 />
                    <ColumnDefinition />
                </>>
                    <TextBlock Text="Max" FontSize=12 Foreground=`theme.SecondaryText` />
                    <TextBlock Grid.Column=1 Text=`StoreToUpdate.Active.ContextLimit > 0 ? StoreToUpdate.Active.ContextLimit.ToString("N0") : "--"` FontSize=12 TextAlignment=Right />
                </Grid>
                <ProgressBar Value=`StoreToUpdate.Active.ContextUsage` Minimum=0 Maximum=100 Height=4 />
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
    <StackPanel Orientation=Horizontal Spacing=8>
        <TextBlock Text=`StoreToUpdate.Active.UsageCostLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text=`StoreToUpdate.Active.UsageTokensLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="tokens" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text="·" FontSize=12 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <TextBlock Text=`StoreToUpdate.Active.ContextLabel` FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
        <TextBlock Text="ctx" FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
        <ProgressBar Value=`StoreToUpdate.Active.ContextUsage` Minimum=0 Maximum=100 Width=70 Height=4 VerticalAlignment=Center />
    </StackPanel>
    """)]
partial class ChatCostInline : IQuickMarkupComponent
{
    
}