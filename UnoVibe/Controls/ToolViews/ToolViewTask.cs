namespace UnoVibe.Controls.ToolViews;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    inject SessionsStateProvider Sessions;
    required ToolCallPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Button CornerRadius=8
            Padding=`new Thickness(10,  8, 10,  8)` HorizontalAlignment=Left MaxWidth=680 Margin=`new Thickness(0, 2, 0, 2)`
            IsEnabled=`Part.ToolSessionId is not null`
            ToolTipService.ToolTip=`Part.ToolSessionId is not null ? "Open the subagent session" : "Waiting for the subagent session…"`
            @Click+=`Sessions.ActiveSessionId = new(Part.ToolSessionId ?? "")`>
        <Grid ColumnSpacing=8 ColumnDefinitions=<>
            <ColumnDefinition Width=Auto />
            <ColumnDefinition />
            <ColumnDefinition Width=Auto />
        </>>
            <Grid Width=14 Height=14 VerticalAlignment=Center>
                if (`Part.IsBusy`)
                    <ToolBusyIndicator Part=`Part` />
                else if (`Part.ToolStatus == "completed"`)
                    <AppSymbolIcon Symbol=Accept FontSize=10 Foreground=`theme.SystemSuccess` HorizontalAlignment=Center VerticalAlignment=Center />
                else if (`Part.ToolStatus == "error"`)
                    <TextBlock Text="⚠" FontSize=12 Foreground=`theme.SystemCritical` HorizontalAlignment=Center VerticalAlignment=Center />
            </Grid>
            <StackPanel Grid.Column=1 Spacing=2 VerticalAlignment=Center>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <TextBlock Text=`Part.DisplayName` FontSize=12 FontWeight=`FontWeights.SemiBold`
                               Foreground=`theme.SecondaryText` TextWrapping=Wrap VerticalAlignment=Center />
                    if (`Part.ToolSubagentType is not null`)
                        <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(6, 1, 6, 2)` VerticalAlignment=Center>
                            <TextBlock Text=`Part.ToolSubagentType` FontSize=10 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                        </Border>
                </StackPanel>
                <TextBlock Text=`Part.StatusText` FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
            </StackPanel>
            <StackPanel Grid.Column=2 Orientation=Horizontal Spacing=6 VerticalAlignment=Center>
                <AppSymbolIcon Symbol=Forward FontSize=11 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
            </StackPanel>
        </Grid>
    </Button>
    """)]
partial class ToolViewTask : IQuickMarkupComponent;
